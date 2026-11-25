using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    const string SolarDefinition = "SolarInstallation";
    readonly ConcurrentDictionary<string, (bool Solar, WidgetSize Size)> active = new();
    readonly SemaphoreSlim updates = new(1);
    readonly Timer refresh;

    internal WidgetProvider()
    {
        refresh = new Timer(_ =>
        {
            if (active.IsEmpty) return;
            Schedule(async () =>
            {
                foreach (string id in active.Keys)
                    if (active.TryGetValue(id, out var widget)) await UpdateAsync(id, widget.Solar, widget.Size);
            }, skipIfBusy: true);
        }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    public void CreateWidget(WidgetContext context)
    {
        string id = context.Id;
        var card = new WidgetCard();
        card.Message(UiText.Get("Home-Assistant-Werte werden geladen…", "Loading Home Assistant values…"));
        string? profileId = null;
        if (context.DefinitionId != SolarDefinition)
        {
            try
            {
                var assigned = WidgetManager.GetDefault().GetWidgetInfos()
                    .Where(widget => widget.WidgetContext.Id != id && widget.WidgetContext.DefinitionId == "HomeAssistant")
                    .Select(widget => ProfileId(widget.CustomState)).ToHashSet(StringComparer.Ordinal);
                profileId = LoadConfiguration().AllProfiles().FirstOrDefault(profile => !assigned.Contains(profile.Id))?.Id ?? "";
            }
            catch (Exception error) { Trace.TraceWarning("Widget profile selection failed (0x{0:X8}).", error.HResult); }
        }
        Publish(id, card, placeholder: true, profileId: profileId);
        Activate(context);
    }
    public void DeleteWidget(string id, string customState) => Deactivate(id);
    public void OnWidgetContextChanged(WidgetContextChangedArgs args)
    {
        var context = args.WidgetContext;
        string id = context.Id;
        var layout = (context.DefinitionId == SolarDefinition, context.Size);
        if (active.TryGetValue(id, out var previous)) active.TryUpdate(id, layout, previous);
        QueueUpdate(id, layout.Item1, layout.Size);
    }

    public void Activate(WidgetContext context)
    {
        string id = context.Id;
        bool solar = context.DefinitionId == SolarDefinition;
        active[id] = (solar, context.Size);
        QueueUpdate(id, solar, context.Size);
    }

    public void Deactivate(string id) => active.TryRemove(id, out _);

    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs args)
    {
        var context = args.WidgetContext;
        QueueUpdate(context.Id, solar: false, size: context.Size);
    }

    public void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        string verb = args.Verb;
        if (verb is not ("set-switch" or "press-button" or "select-profile") || args.WidgetContext.DefinitionId != "HomeAssistant") return;
        string id = args.WidgetContext.Id;
        WidgetSize size = args.WidgetContext.Size;
        string data = args.Data;
        Schedule(() => UpdateAsync(id, solar: false, size, verb, data));
    }

    void QueueUpdate(string id, bool solar, WidgetSize size) => Schedule(() => UpdateAsync(id, solar, size));

    void Schedule(Func<Task> update, bool skipIfBusy = false)
    {
        _ = Task.Run(async () =>
        {
            if (!await updates.WaitAsync(skipIfBusy ? 0 : Timeout.Infinite)) return;
            try { await update(); }
            catch (Exception error) { Trace.TraceError("Widget board update failed (0x{0:X8}).", error.HResult); }
            finally { updates.Release(); }
        });
    }

    static BoardConfiguration LoadConfiguration()
    {
        if (!File.Exists(BoardSelection.FilePath)) return new BoardConfiguration();
        string contents = HomeAssistant.ReadProfileFile(BoardSelection.FilePath).TrimStart();
        return contents.StartsWith("[", StringComparison.Ordinal)
            ? new BoardConfiguration { Entities = JsonSerializer.Deserialize<string[]>(contents)! }
            : JsonSerializer.Deserialize<BoardConfiguration>(contents) ?? new BoardConfiguration();
    }

    static async Task UpdateAsync(string id, bool solar, WidgetSize size, string? verb = null, string? action = null)
    {
        var card = new WidgetCard(size == WidgetSize.Small, size == WidgetSize.Large);
        BoardProfile[]? profiles = null;
        string? profileId = null;
        string? saveProfile = null;
        try
        {
            var config = LoadConfiguration();
            if (!solar)
            {
                profiles = config.AllProfiles();
                profileId = ProfileId(WidgetManager.GetDefault().GetWidgetInfo(id).CustomState);
                if (verb == "select-profile" && action != null)
                {
                    using var selection = JsonDocument.Parse(action);
                    if (selection.RootElement.TryGetProperty("profile", out var input))
                    {
                        string? requested = input.GetString();
                        if (requested == "default") requested = "";
                        if (profiles.Any(profile => profile.Id == requested)) saveProfile = profileId = requested;
                    }
                }
            }
            var connection = HomeAssistant.Load();
            if (connection == null)
                card.Message(UiText.Get("Home Assistant in den Programmeinstellungen verbinden.",
                    "Connect Home Assistant in the app settings."));
            else if (solar)
                await AddSolarReadings(card, connection, config);
            else
            {
                var selected = profiles!.FirstOrDefault(profile => profile.Id == profileId) ?? profiles![0];
                var entities = (selected.Entities ?? []).OfType<string>().Where(BoardSelection.IsSupported)
                    .Distinct(StringComparer.Ordinal).Take(BoardSelection.Capacity).ToArray();
                if (selected.Id.Length != 0) card.ProfileName(selected.Name);
                if (entities.Length == 0)
                    card.Message(UiText.Get("Entitäten unter Einstellungen → Windows-Widgetboard auswählen.",
                        "Select entities under Settings → Windows Widgets Board."));
                if (verb is "set-switch" or "press-button" && action != null)
                {
                    using var payload = JsonDocument.Parse(action);
                    string? entity = payload.RootElement.GetProperty("entity").GetString();
                    if (entity != null && entities.Contains(entity, StringComparer.Ordinal))
                    {
                        try
                        {
                            if (verb == "press-button" && BoardSelection.IsButton(entity))
                                await connection.PressButtonAsync(entity);
                            else if (verb == "set-switch" && BoardSelection.IsSwitch(entity))
                                await connection.SetSwitchAsync(entity, payload.RootElement.GetProperty("on").GetBoolean());
                        }
                        catch (Exception)
                        {
                            card.Message(UiText.Get("Aktion fehlgeschlagen.", "Action failed."));
                        }
                    }
                }
                var readings = await Task.WhenAll(entities.Select(connection.ReadAsync));
                for (int i = 0; i < entities.Length; i++)
                {
                    if (selected.HighlightValues) card.AddHighlightedEntity(entities[i], readings[i], i, entities.Length);
                    else card.Add(entities[i], readings[i]);
                }
            }
        }
        catch (Exception)
        {
            card = new WidgetCard();
            card.Message(UiText.Get("Home Assistant nicht erreichbar. Verbindung in der App prüfen.",
                "Home Assistant is unavailable. Check the connection in the app."));
        }
        if (profiles != null) card.ProfileSettings(profiles, profileId);
        Publish(id, card, profileId: saveProfile);
    }

    static async Task AddSolarReadings(WidgetCard card, HomeAssistant connection, BoardConfiguration config)
    {
        // The stored desktop order is PV, battery, home, grid. The Echo card shows battery last.
        var entities = new[] { 0, 2, 1 }.Select(i => connection.SolarEntities.ElementAtOrDefault(i))
            .Select(entity => entity != null && entity.StartsWith("sensor.", StringComparison.Ordinal) &&
                BoardSelection.IsSupported(entity) ? entity : null).ToArray();
        if (entities.All(entity => entity == null))
        {
            card.Message(UiText.Get("PV, Hausverbrauch und Akku in den Einstellungen der Solar-Kachel auswählen.",
                "Select PV, home consumption and battery sensors in the desktop Solar widget settings."));
            return;
        }

        var readings = await Task.WhenAll(entities.Select(entity =>
            entity == null ? Task.FromResult<EntityState?>(null) : connection.ReadAsync(entity)));
        card.AddSolarReadings(readings, config);
    }

    static string ProfileId(string? state) => state is null or "default" ? "" : state;

    static void Publish(string id, WidgetCard card, bool placeholder = false, string? profileId = null)
    {
        var request = new WidgetUpdateRequestOptions(id)
        {
            Template = card.Template,
            Data = card.Data,
            IsPlaceholderContent = placeholder
        };
        if (profileId != null) request.CustomState = profileId.Length == 0 ? "default" : profileId;
        WidgetManager.GetDefault().UpdateWidget(request);
    }
}

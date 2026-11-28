using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    const string SolarDefinition = "SolarInstallation";
    const string SolarIconsDefinition = "SolarInstallationIcons";
    const string HomeAssistantDefinition = "HomeAssistant";
    const string SwitchesDefinition = "HomeAssistantSwitches";
    const string SensorsDefinition = "HomeAssistantSensors";
    const string TwoColumnsDefinition = "HomeAssistantTwoColumns";
    readonly ConcurrentDictionary<string, (string Definition, WidgetSize Size)> active = new();
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
                    if (active.TryGetValue(id, out var widget)) await UpdateAsync(id, widget.Definition, widget.Size);
            }, skipIfBusy: true);
        }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    public void CreateWidget(WidgetContext context)
    {
        string id = context.Id;
        var card = new WidgetCard();
        card.Message(UiText.Get("Home-Assistant-Werte werden geladen…", "Loading Home Assistant values…"));
        string? profileId = null;
        bool solar = IsSolar(context.DefinitionId);
        if (solar || IsHomeAssistant(context.DefinitionId))
        {
            try
            {
                var assigned = WidgetManager.GetDefault().GetWidgetInfos()
                    .Where(widget => widget.WidgetContext.Id != id && (solar
                        ? IsSolar(widget.WidgetContext.DefinitionId) : widget.WidgetContext.DefinitionId == context.DefinitionId))
                    .Select(widget => ProfileId(widget.CustomState)).ToHashSet(StringComparer.Ordinal);
                var config = LoadConfiguration();
                var choices = solar ? config.SolarChoices() : ProfilesFor(config, context.DefinitionId);
                int type = context.DefinitionId switch
                {
                    SwitchesDefinition => 1,
                    SensorsDefinition => 2,
                    TwoColumnsDefinition => 3,
                    _ => 0
                };
                var matching = solar ? choices : choices.Where(profile => profile.WidgetText?.PreferredWidgetType == type).ToArray();
                profileId = (matching.Length == 0 ? choices : matching)
                    .FirstOrDefault(profile => !assigned.Contains(profile.Id))?.Id ?? "";
                if (!solar && matching.Length != 0 && matching.All(profile => assigned.Contains(profile.Id)))
                    profileId = matching[0].Id;
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
        var layout = (context.DefinitionId, context.Size);
        if (active.TryGetValue(id, out var previous)) active.TryUpdate(id, layout, previous);
        QueueUpdate(id, layout.Item1, layout.Size);
    }

    public void Activate(WidgetContext context)
    {
        string id = context.Id;
        string definition = context.DefinitionId;
        active[id] = (definition, context.Size);
        QueueUpdate(id, definition, context.Size);
    }

    public void Deactivate(string id) => active.TryRemove(id, out _);

    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs args)
    {
        var context = args.WidgetContext;
        QueueUpdate(context.Id, context.DefinitionId, context.Size);
    }

    public void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        string verb = args.Verb;
        string definition = args.WidgetContext.DefinitionId;
        if (verb != "select-profile" && (!IsHomeAssistant(definition)
            || verb is not ("set-switch" or "press-button"))) return;
        if (!IsHomeAssistant(definition) && !IsSolar(definition)) return;
        string id = args.WidgetContext.Id;
        WidgetSize size = args.WidgetContext.Size;
        string data = args.Data;
        Schedule(() => UpdateAsync(id, definition, size, verb, data));
    }

    void QueueUpdate(string id, string definition, WidgetSize size) => Schedule(() => UpdateAsync(id, definition, size));

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

    static async Task UpdateAsync(string id, string definition, WidgetSize size, string? verb = null, string? action = null)
    {
        var card = new WidgetCard(size == WidgetSize.Small, size == WidgetSize.Large);
        BoardProfile[]? profiles = null;
        string? profileId = null;
        string? saveProfile = null;
        try
        {
            var config = LoadConfiguration();
            bool solar = IsSolar(definition);
            if (IsHomeAssistant(definition) || solar)
            {
                profiles = solar ? config.SolarChoices() : ProfilesFor(config, definition);
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
            {
                var selected = profiles!.FirstOrDefault(profile => profile.Id == profileId) as SolarBoardProfile;
                string? solarName = selected?.Name ?? config.DefaultSolarName;
                if (!string.IsNullOrWhiteSpace(solarName)) card.ProfileName(solarName.Trim());
                var solarSettings = selected?.Solar ?? (SolarBoardSettings)config;
                await AddSolarReadings(card, connection, solarSettings,
                    solarSettings.SolarShowIcons ?? (definition == SolarIconsDefinition), size == WidgetSize.Small, selected);
            }
            else
            {
                var selected = profiles!.FirstOrDefault(profile => profile.Id == profileId) ?? profiles![0];
                var text = selected.WidgetText ?? new BoardWidgetText();
                string control = text.ActionEntity ?? "";
                bool hasControl = definition != SensorsDefinition && IsControl(control);
                bool small = size == WidgetSize.Small;
                bool twoColumns = definition == TwoColumnsDefinition;
                int layoutIndex = twoColumns ? 3 : small ? 0 : size == WidgetSize.Large ? 2 : 1;
                bool highlighted = text.HighlightFor(layoutIndex, !twoColumns && selected.HighlightValues);
                int visibleCapacity = twoColumns ? BoardSelection.TwoColumnCapacity : small ? BoardSelection.SmallCapacity
                    : size == WidgetSize.Large ? BoardSelection.Capacity : BoardSelection.MediumCapacity;
                var slots = (selected.Entities ?? []).Take(twoColumns ? BoardSelection.TwoColumnCapacity : BoardSelection.Capacity)
                    .Select((entity, index) => (entity, index))
                    .Where(slot => slot.entity != null && AllowsEntity(definition, slot.entity) &&
                        (!hasControl || slot.entity != control)).Take(visibleCapacity).ToArray();
                var entities = slots.Select(slot => slot.entity).ToArray();
                var rowControls = slots.Select(slot => (text.RowActions ?? []).ElementAtOrDefault(slot.index) ?? "")
                    .Where(IsControl).Distinct(StringComparer.Ordinal).ToArray();
                string? description = text.UseCustomDescription ? text.Description : selected.Id.Length != 0 ? selected.Name : null;
                if (entities.Length == 0 && !hasControl)
                    card.Message(UiText.Get("Entitäten unter Einstellungen → Windows Widgets auswählen.",
                        "Select entities under Settings → Windows Widgets."));
                if (verb is "set-switch" or "press-button" && action != null)
                {
                    using var payload = JsonDocument.Parse(action);
                    string? entity = payload.RootElement.GetProperty("entity").GetString();
                    if (entity != null && (rowControls.Contains(entity, StringComparer.Ordinal) ||
                        (definition != SensorsDefinition && (entities.Contains(entity, StringComparer.Ordinal) || (hasControl && entity == control)))))
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
                var readingTasks = entities.Distinct(StringComparer.Ordinal)
                    .ToDictionary(entity => entity, connection.ReadAsync, StringComparer.Ordinal);
                var readingsTask = Task.WhenAll(entities.Select(entity => readingTasks[entity]));
                var controlTask = hasControl ? ReadOptionalSensorAsync(connection, control, allowControl: true)
                    : Task.FromResult<EntityState?>(null);
                var rowControlTasks = rowControls.ToDictionary(entity => entity,
                    entity => ReadOptionalSensorAsync(connection, entity, allowControl: true), StringComparer.Ordinal);
                await Task.WhenAll(rowControlTasks.Values.Cast<Task>().Append(readingsTask).Append(controlTask));
                var readings = await readingsTask;
                bool dense = !small && entities.Length > BoardSelection.SmallCapacity;
                int firstContact = Array.FindIndex(entities, entity =>
                    WidgetCard.IsContact(entity, readings[Array.IndexOf(entities, entity)]));
                int controlRow = !hasControl || definition == SwitchesDefinition ? -1 : WidgetCard.IsGarage(control)
                    ? Array.FindIndex(entities, entity => entity.StartsWith("binary_sensor.", StringComparison.Ordinal)
                        && WidgetCard.IsGarage(entity))
                    : Math.Max(firstContact, 0);
                string? title = small && !string.IsNullOrWhiteSpace(text.CompactTitle) ? text.CompactTitle
                    : (!small || firstContact < 0) ? description : null;
                if (!text.HideTitle && !string.IsNullOrWhiteSpace(title)) card.ProfileName(title);
                var rowAction = hasControl ? card.EntityAction(control, (await controlTask)?.State, text.ActionCaption) : null;
                int rowStart = card.RowCount;
                int columnSplit = -1;
                var rightColumns = twoColumns ? BoardWidgetText.RightColumns(slots.Select(slot => slot.index).ToArray(), text.Columns)
                    : new bool[entities.Length];
                foreach (int i in Enumerable.Range(0, entities.Length).OrderBy(index => rightColumns[index]))
                {
                    if (rightColumns[i] && columnSplit < 0) columnSplit = card.RowCount;
                    string? caption = i == 0 ? text.StatusCaption : null;
                    int slot = slots[i].index;
                    string rowControl = (text.RowActions ?? []).ElementAtOrDefault(slot) ?? "";
                    var actionForRow = rowControlTasks.TryGetValue(rowControl, out var rowControlTask)
                        ? card.EntityAction(rowControl, (await rowControlTask)?.State)
                        : i == controlRow ? rowAction : null;
                    bool hideName = (text.CompactHideNames ?? []).ElementAtOrDefault(slot);
                    string? customCaption = (text.CompactCaptions ?? []).ElementAtOrDefault(slot);
                    if (!string.IsNullOrWhiteSpace(customCaption)) caption = customCaption;
                    string? fontSize = (text.CompactSizes ?? []).ElementAtOrDefault(slot);
                    string? icon = (text.CompactIcons ?? []).ElementAtOrDefault(slot);
                    bool contact = WidgetCard.IsContact(entities[i], readings[i]);
                    if (highlighted && ((small && contact) || dense || twoColumns) && string.IsNullOrWhiteSpace(fontSize))
                        fontSize = "Large";
                    if (twoColumns)
                        card.Add(entities[i], readings[i], caption, actionForRow, icon, hideName, fontSize, true);
                    else if (dense || hideName || !string.IsNullOrWhiteSpace(fontSize)
                        || (small && (!string.IsNullOrWhiteSpace(icon) || !string.IsNullOrWhiteSpace(customCaption) || contact)))
                        card.AddCompactStatus(entities[i], readings[i], caption, actionForRow ?? card.EntityAction(entities[i], readings[i]?.State),
                            hideName, dense && string.IsNullOrWhiteSpace(fontSize) ? "Small" : fontSize, icon);
                    else if (highlighted)
                        card.AddHighlightedEntity(entities[i], readings[i], i, entities.Length, caption, actionForRow, icon);
                    else card.Add(entities[i], readings[i], caption, actionForRow, icon);
                }
                if (twoColumns && entities.Length != 0)
                    card.ArrangeColumns(rowStart, columnSplit < 0 ? card.RowCount : columnSplit, text.ShowColumnDivider);
                if (hasControl && (entities.Length == 0 || definition == SwitchesDefinition))
                    card.AddControl(control, await controlTask, text.ActionCaption);
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

    static bool IsSolar(string definition) => definition is SolarDefinition or SolarIconsDefinition;

    static bool IsHomeAssistant(string definition) =>
        definition is HomeAssistantDefinition or SwitchesDefinition or SensorsDefinition or TwoColumnsDefinition;

    static bool IsControl(string entity) => BoardSelection.IsSwitch(entity) || BoardSelection.IsButton(entity);

    static bool AllowsEntity(string definition, string entity) => BoardSelection.IsSupported(entity) &&
        (definition switch
        {
            SwitchesDefinition => IsControl(entity),
            SensorsDefinition => !IsControl(entity),
            _ => true
        });

    static BoardProfile[] ProfilesFor(BoardConfiguration config, string definition)
    {
        var profiles = config.AllProfiles();
        if (definition == HomeAssistantDefinition) return profiles;
        var matching = profiles.Where(profile =>
            (profile.Entities ?? []).Any(entity => entity != null && AllowsEntity(definition, entity)) ||
            (definition == SwitchesDefinition && IsControl(profile.WidgetText?.ActionEntity ?? ""))).ToArray();
        return matching.Length == 0 ? profiles : matching;
    }

    static async Task AddSolarReadings(WidgetCard card, HomeAssistant connection, SolarBoardSettings config,
        bool icons, bool compact, SolarBoardProfile? profile)
    {
        var liveSensors = profile == null
            ? ((BoardConfiguration)config).DefaultSolarSensors(connection.SolarEntities!, connection.GridExportEntity!)
            : Enumerable.Range(0, 4).Select(i => (profile.Entities ?? []).ElementAtOrDefault(i))
                .Append(profile.GridExportEntity).ToArray();
        var entities = liveSensors.Take(3)
            .Select(entity => entity != null && entity.StartsWith("sensor.", StringComparison.Ordinal) &&
                BoardSelection.IsSupported(entity) ? entity : null).ToArray();
        var dailyEntities = compact ? [] : (config.SolarDailyEntities ?? [])
            .Take(BoardSelection.SolarDailyLabels.Length).ToArray();
        string? grid = liveSensors[3];
        string? export = liveSensors[4];
        bool showGrid = BoardSelection.IsSolarDailySensor(grid ?? "");
        if (entities.All(entity => entity == null) && !showGrid && !dailyEntities.Any(BoardSelection.IsSolarDailySensor))
        {
            card.Message(UiText.Get("Sensoren in den Einstellungen der Solarkachel auswählen.",
                "Select sensors in the solar widget settings."));
            return;
        }
        var liveTask = Task.WhenAll(entities.Select(entity =>
            entity == null ? Task.FromResult<EntityState?>(null) : connection.ReadAsync(entity)));
        var dailyTask = Task.WhenAll(dailyEntities.Select(entity => ReadOptionalSensorAsync(connection, entity)));
        var gridTask = showGrid ? ReadGridAsync(connection, grid!, export) : Task.FromResult<EntityState?>(null);
        await Task.WhenAll(liveTask, dailyTask, gridTask);
        var live = await liveTask;
        var visible = profile == null && ((BoardConfiguration)config).SolarLiveEntities?.Length != 5 ? null
            : entities.Select(entity => entity != null).Append(showGrid).ToArray();
        card.AddSolarReadings(showGrid ? [.. live, await gridTask] : live, config, icons, await dailyTask, visible);
    }

    static async Task<EntityState?> ReadGridAsync(HomeAssistant connection, string grid, string? export)
    {
        var primary = ReadOptionalSensorAsync(connection, grid);
        bool separateExport = BoardSelection.IsSolarDailySensor(export ?? "");
        var exported = separateExport ? ReadOptionalSensorAsync(connection, export) : Task.FromResult<EntityState?>(null);
        var input = await primary;
        var output = await exported;
        if (PowerInWatts(input) is not decimal imported) return null;
        decimal? exportWatts = separateExport ? PowerInWatts(output) : 0;
        if (!exportWatts.HasValue) return null;
        return new EntityState((imported - exportWatts.Value).ToString(CultureInfo.InvariantCulture),
            new EntityAttributes(null, "W", "power", null));
    }

    static decimal? PowerInWatts(EntityState? reading)
    {
        if (!decimal.TryParse(reading?.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var power)) return null;
        return reading?.Attributes?.Unit switch
        {
            "W" => power,
            "kW" when decimal.Abs(power) <= decimal.MaxValue / 1000 => power * 1000,
            "MW" when decimal.Abs(power) <= decimal.MaxValue / 1000000 => power * 1000000,
            _ => null
        };
    }

    static async Task<EntityState?> ReadOptionalSensorAsync(HomeAssistant connection, string? entity, bool allowControl = false)
    {
        if (entity == null || !(allowControl ? BoardSelection.IsSupported(entity) : BoardSelection.IsSolarDailySensor(entity))) return null;
        try { return await connection.ReadAsync(entity); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return null;
        }
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

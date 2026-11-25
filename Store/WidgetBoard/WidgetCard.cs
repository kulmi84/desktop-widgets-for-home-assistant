using System.Globalization;
using System.Text.Json;

sealed class WidgetCard(bool compact = false, bool large = false)
{
    readonly List<object> body = [];
    readonly Dictionary<string, string> data = [];
    static readonly string[] HighlightColors = ["Warning", "Accent", "Good"];
    static readonly int[] HighlightRgb = [0xFFE55C, 0x84D8F2, 0x8FF3D8];
    bool? highContrast;
    object? profileSettings;

    internal string Template => JsonSerializer.Serialize(new
    {
        type = "AdaptiveCard",
        version = "1.5",
        body = profileSettings == null ? body : new List<object>
        {
            new Dictionary<string, object>
            {
                ["type"] = "Container", ["items"] = body, ["$when"] = "${!$host.isSettingsPayload}"
            },
            profileSettings
        }
    });

    internal string Data => JsonSerializer.Serialize(data);

    internal void Message(string message) => body.Add(new { type = "TextBlock", text = Bind(message), wrap = true });

    internal void ProfileName(string name) => body.Add(new { type = "TextBlock", text = Bind(name),
        weight = "Bolder", size = compact ? "Small" : "Medium", wrap = true, maxLines = 1 });

    internal void ProfileSettings(BoardProfile[] profiles, string? selected)
    {
        string value = profiles.Any(profile => profile.Id == selected) ? ChoiceValue(selected!) : "default";
        profileSettings = new Dictionary<string, object>
        {
            ["type"] = "Container", ["$when"] = "${$host.isSettingsPayload}",
            ["items"] = new object[]
            {
                new { type = "Input.ChoiceSet", id = "profile", label = UiText.Get("Kachel", "Widget"),
                    style = "compact", value,
                    choices = profiles.Select(profile => new { title = Bind(profile.Name), value = ChoiceValue(profile.Id) }).ToArray() },
                new { type = "ActionSet", actions = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["type"] = "Action.Execute", ["verb"] = "select-profile",
                        ["title"] = UiText.Get("Übernehmen", "Apply"), ["$when"] = "${$host.isUserContextAuthenticated}"
                    }
                }}
            }
        };
    }

    static string ChoiceValue(string id) => id.Length == 0 ? "default" : id;

    internal void AddSolarReadings(EntityState?[] readings, BoardConfiguration config)
    {
        var labels = BoardSelection.SolarLabels;
        var culture = CultureInfo.GetCultureInfo(UiText.English ? "en-US" : "de-DE");
        bool hasReading = false;
        for (int i = 0; i < labels.Length; i++)
        {
            var reading = readings[i];
            string value = "—";
            if (decimal.TryParse(reading?.State, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
            {
                int? places = config.DecimalPlaces(i);
                value = places.HasValue ? number.ToString("F" + places.Value, culture) : number.ToString(culture);
                string? unit = reading?.Attributes?.Unit;
                if (!string.IsNullOrWhiteSpace(unit)) value += " " + unit;
                hasReading = true;
            }
            AddHighlight(body, labels[i], value, i, i == 0, compact);
        }
        if (hasReading && !compact)
            body.Add(new { type = "TextBlock", text = Bind(UiText.Get("Stand: ", "Updated: ") +
                DateTime.Now.ToString("t", culture)), spacing = "Medium", size = "Small", isSubtle = true });
    }

    void AddHighlight(List<object> items, string label, string value, int colorIndex, bool first, bool shortRow)
    {
        string color = HighlightColors[colorIndex];
        if (shortRow)
        {
            items.Add(new { type = "ColumnSet", spacing = "Small", columns = new object[]
                {
                    new { type = "Column", width = "stretch", items = new[] {
                        new { type = "TextBlock", text = Bind(label), size = "Small", maxLines = 1, wrap = true } } },
                    new { type = "Column", width = "auto", items = new[] {
                        new { type = "TextBlock", text = Bind(value), weight = "Bolder", color } } }
                } });
            return;
        }
        items.Add(new { type = "TextBlock", text = Bind(label), maxLines = 1, wrap = true,
            spacing = first ? "Small" : large ? "Large" : "Default", size = large ? "Default" : "Small" });
        string valueBinding = Bind(value);
        var text = new Dictionary<string, object>
        {
            ["type"] = "TextBlock", ["text"] = valueBinding, ["spacing"] = "None",
            ["size"] = "ExtraLarge", ["weight"] = "Bolder", ["color"] = color,
            ["maxLines"] = 1, ["wrap"] = true
        };
        highContrast ??= new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        if (!highContrast.Value)
        {
            items.Add(new Dictionary<string, object>
            {
                ["type"] = "Image", ["url"] = Bind(SolarValueImage.Render(value, HighlightRgb[colorIndex], large)),
                ["altText"] = valueBinding, ["width"] = SolarValueImage.Width + "px",
                ["height"] = (large ? SolarValueImage.LargeHeight : SolarValueImage.Height) + "px", ["spacing"] = "None",
                ["fallback"] = new Dictionary<string, object>(text), ["$when"] = "${$host.hostTheme == 'dark'}"
            });
            text["$when"] = "${$host.hostTheme != 'dark'}";
        }
        items.Add(text);
    }

    internal void AddHighlightedEntity(string entity, EntityState? reading, int index, int count)
    {
        var (name, value, state, contact) = Describe(entity, reading);
        object? action = EntityAction(entity, state);
        if (action != null && BoardSelection.IsButton(entity)) value = UiText.Get("Drücken", "Press");
        int color = contact && state is "on" or "off" ? (state == "on" ? 0 : 2) : index;
        var items = new List<object>();
        AddHighlight(items, name, value, color, index == 0, compact && count > 1);
        var row = new Dictionary<string, object>
        {
            ["type"] = "Container", ["items"] = items, ["spacing"] = large ? "Large" : "Small"
        };
        if (action != null)
            AddActionRow(row, action);
        else body.Add(row);
    }

    static (string Name, string Value, string? State, bool Contact) Describe(string entity, EntityState? reading)
    {
        string name = string.IsNullOrWhiteSpace(reading?.Attributes?.Name) ? entity : reading.Attributes!.Name!;
        string? state = reading?.State;
        bool contact = entity.StartsWith("binary_sensor.", StringComparison.Ordinal) &&
            reading?.Attributes?.DeviceClass is "door" or "garage_door" or "opening" or "window";
        string value = state switch
        {
            null or "unavailable" => UiText.Get("Nicht verfügbar", "Unavailable"),
            _ when BoardSelection.IsButton(entity) => UiText.Get("Taster", "Button"),
            "unknown" => UiText.Get("Unbekannt", "Unknown"),
            "on" => contact ? UiText.Get("Offen", "Open") : UiText.Get("Ein", "On"),
            "off" => contact ? UiText.Get("Geschlossen", "Closed") : UiText.Get("Aus", "Off"),
            _ => FormatReading(state, reading?.Attributes?.Unit)
        };
        return (name, value, state, contact);
    }

    internal void Add(string entity, EntityState? reading)
    {
        var (name, value, state, _) = Describe(entity, reading);
        object? action = EntityAction(entity, state);
        if (compact && action != null && BoardSelection.IsButton(entity)) value = UiText.Get("Drücken", "Press");

        var columns = new List<object>
        {
            new
            {
                type = "Column", width = "stretch", verticalContentAlignment = "Center",
                items = compact ? new object[]
                {
                    new { type = "TextBlock", text = Bind(name), size = "Small", maxLines = 1, wrap = true }
                } : new object[]
                {
                    new { type = "TextBlock", text = Bind(name), size = large ? "Default" : "Small", maxLines = 1, wrap = true },
                    new { type = "TextBlock", text = Bind(value), spacing = "None", weight = "Bolder",
                        size = large ? "Large" : "Default", maxLines = 1, wrap = true }
                }
            }
        };
        if (compact)
            columns.Add(new { type = "Column", width = "auto", verticalContentAlignment = "Center", items = new[]
            {
                new { type = "TextBlock", text = Bind(value), size = "Small", weight = "Bolder", maxLines = 1, wrap = true }
            } });
        if (action != null && !compact)
        {
            columns.Add(new
            {
                type = "Column", width = "auto", verticalContentAlignment = "Center",
                items = new[]
                {
                    new
                    {
                        type = "ActionSet", actions = new[] { action }
                    }
                }
            });
        }
        var row = new Dictionary<string, object>
        {
            ["type"] = "ColumnSet", ["spacing"] = large ? "Large" : "Small",
            ["separator"] = !compact && body.Count != 0, ["columns"] = columns
        };
        if (compact && action != null) AddActionRow(row, action);
        else body.Add(row);
    }

    static object? EntityAction(string entity, string? state)
    {
        if (BoardSelection.IsButton(entity) && state is not (null or "unavailable"))
            return new Dictionary<string, object>
            {
                ["type"] = "Action.Execute", ["verb"] = "press-button", ["data"] = new { entity },
                ["title"] = UiText.Get("Drücken", "Press"), ["$when"] = "${$host.isUserContextAuthenticated}"
            };
        if (!BoardSelection.IsSwitch(entity) || state is not ("on" or "off")) return null;
        bool on = state == "off";
        return new Dictionary<string, object>
        {
            ["type"] = "Action.Execute", ["verb"] = "set-switch", ["data"] = new { entity, on },
            ["title"] = on ? UiText.Get("Einschalten", "Turn on") : UiText.Get("Ausschalten", "Turn off"),
            ["$when"] = "${$host.isUserContextAuthenticated}"
        };
    }

    void AddActionRow(Dictionary<string, object> row, object action)
    {
        body.Add(new Dictionary<string, object>(row)
        {
            ["$when"] = "${$host.isUserContextAuthenticated}",
            ["selectAction"] = action
        });
        row["$when"] = "${!$host.isUserContextAuthenticated}";
        body.Add(row);
    }

    static string FormatReading(string state, string? unit)
    {
        if (decimal.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            state = number.ToString(UiText.English ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("de-DE"));
        return string.IsNullOrWhiteSpace(unit) ? state : state + " " + unit;
    }

    string Bind(string value)
    {
        string key = "text" + data.Count;
        data.Add(key, value);
        return "${" + key + "}";
    }
}

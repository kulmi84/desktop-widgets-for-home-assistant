using System.Globalization;
using System.Text.Json;

sealed class WidgetCard(bool compact = false, bool large = false)
{
    readonly List<object> body = [];
    readonly Dictionary<string, string> data = [];
    static readonly string[] HighlightColors = ["Warning", "Accent", "Good", "Default"];
    static readonly int[] HighlightRgb = [0xFFE55C, 0x84D8F2, 0x8FF3D8, 0xF3F3F3];
    static readonly int[] LightIconRgb = [0x805B00, 0x176789, 0x127668, 0x424242];
    static readonly string[] SolarIcons = ["solar-panel", "home-lightning-bolt", "battery", "flash"];
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

    internal void AddSolarReadings(EntityState?[] readings, SolarBoardSettings config, bool icons,
        EntityState?[] dailyReadings, bool[]? visible = null)
    {
        var labels = BoardSelection.SolarLabels;
        var culture = CultureInfo.GetCultureInfo(UiText.English ? "en-US" : "de-DE");
        var dailyEntities = config.SolarDailyEntities ?? [];
        bool showDaily = !compact && dailyEntities.Take(BoardSelection.SolarDailyLabels.Length)
            .Any(BoardSelection.IsSolarDailySensor);
        List<object> live = showDaily ? [] : body;
        int imageWidth = showDaily ? (icons ? 110 : 140) : icons ? 222 : SolarValueImage.Width;
        bool hasReading = false;
        bool dense = readings.Length > labels.Length;
        for (int i = 0; i < readings.Length; i++)
        {
            if (visible != null && !visible[i]) continue;
            var reading = readings[i];
            string value = "—";
            if (decimal.TryParse(reading?.State, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
            {
                int? places = config.DecimalPlaces(i);
                string? unit = reading?.Attributes?.Unit;
                if (unit is "W" or "kW")
                {
                    bool inputKilowatts = unit == "kW";
                    bool useKilowatts = decimal.Abs(number) >= (inputKilowatts ? 1 : 1000);
                    if (useKilowatts && !inputKilowatts) number /= 1000;
                    if (!useKilowatts && inputKilowatts) number *= 1000;
                    unit = useKilowatts ? "kW" : "W";
                    string format = useKilowatts ? (places.HasValue ? "F" + places.Value : "0.###") : "F0";
                    value = number.ToString(format, culture);
                }
                else value = places.HasValue ? number.ToString("F" + places.Value, culture) : number.ToString(culture);
                if (!string.IsNullOrWhiteSpace(unit)) value += " " + unit;
                hasReading = true;
            }
            string caption = config.SolarCaption(i == labels.Length ? BoardSelection.SolarGridCaptionIndex : i);
            AddHighlight(live, caption, value, i, i == 0, compact, imageWidth, dense,
                icons ? SolarIcons[i] : null);
        }
        if (showDaily)
        {
            var daily = new List<object>
            {
                new { type = "TextBlock", text = Bind(config.SolarCaption(labels.Length)),
                    size = "Small", weight = "Bolder", isSubtle = true, spacing = "None", wrap = true, maxLines = 2 }
            };
            for (int i = 0; i < BoardSelection.SolarDailyLabels.Length; i++)
            {
                if (!BoardSelection.IsSolarDailySensor(dailyEntities.ElementAtOrDefault(i) ?? "")) continue;
                var reading = dailyReadings.ElementAtOrDefault(i);
                daily.Add(new { type = "TextBlock", text = Bind(config.SolarCaption(labels.Length + 1 + i)),
                    size = "Small", wrap = true, maxLines = 2, spacing = "Default" });
                string value = "—";
                if (decimal.TryParse(reading?.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var total) &&
                    reading?.Attributes?.Unit is "Wh" or "kWh" or "MWh" or "GWh")
                {
                    int? places = config.DailyDecimalPlaces(i);
                    value = total.ToString(places.HasValue ? "F" + places.Value : "0.##", culture) + " " + reading.Attributes.Unit;
                    hasReading = true;
                }
                daily.Add(new { type = "TextBlock", text = Bind(value), size = large ? "Large" : "Medium",
                    weight = "Bolder", wrap = true, maxLines = 1, spacing = "None" });
            }
            if (live.Count == 0) body.AddRange(daily);
            else body.Add(new { type = "ColumnSet", columns = new object[]
            {
                new { type = "Column", width = 3, items = live },
                new { type = "Column", width = 2, spacing = "Medium", separator = true,
                    verticalContentAlignment = "Center", items = daily }
            } });
        }
        if (hasReading && !compact)
            body.Add(new { type = "TextBlock", text = Bind(UiText.Get("Stand: ", "Updated: ") +
                DateTime.Now.ToString("t", culture)), spacing = "Medium", size = "Small", isSubtle = true });
    }

    void AddHighlight(List<object> items, string label, string value, int colorIndex, bool first, bool shortRow,
        int imageWidth = SolarValueImage.Width, bool dense = false, string? valueIcon = null)
    {
        string color = HighlightColors[colorIndex];
        if (shortRow)
        {
            var columns = new List<object>();
            if (valueIcon != null) columns.Add(IconColumn(valueIcon, colorIndex));
            columns.Add(new { type = "Column", width = "stretch", verticalContentAlignment = "Center", items = new[] {
                        new { type = "TextBlock", text = Bind(label), size = "Small", maxLines = 1, wrap = true } } });
            columns.Add(new { type = "Column", width = "auto", verticalContentAlignment = "Center", items = new[] {
                        new { type = "TextBlock", text = Bind(value), weight = "Bolder", color } } });
            items.Add(new { type = "ColumnSet", spacing = "Small", columns });
            return;
        }
        string spacing = first || dense ? "Small" : large ? "Large" : "Default";
        var caption = new { type = "TextBlock", text = Bind(label), maxLines = 1, wrap = true,
            spacing, size = large ? "Default" : "Small" };
        if (valueIcon == null) items.Add(caption);
        else items.Add(new { type = "ColumnSet", spacing, columns = new object[]
        {
            new { type = "Column", width = IconSize + "px", items = Array.Empty<object>() },
            new { type = "Column", width = "stretch", items = new[] { caption } }
        } });
        List<object> valueItems = valueIcon == null ? items : [];
        string valueBinding = Bind(value);
        var text = new Dictionary<string, object>
        {
            ["type"] = "TextBlock", ["text"] = valueBinding, ["spacing"] = "None",
            ["size"] = imageWidth <= 140 ? "Large" : "ExtraLarge", ["weight"] = "Bolder", ["color"] = color,
            ["maxLines"] = imageWidth <= 140 ? 2 : 1, ["wrap"] = true
        };
        highContrast ??= new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        if (!highContrast.Value)
        {
            valueItems.Add(new Dictionary<string, object>
            {
                ["type"] = "Image", ["url"] = Bind(SolarValueImage.Render(value, HighlightRgb[colorIndex], large, imageWidth, dense)),
                ["altText"] = valueBinding, ["width"] = imageWidth + "px",
                ["height"] = SolarValueImage.HeightFor(large, dense) + "px", ["spacing"] = "None",
                ["fallback"] = new Dictionary<string, object>(text), ["$when"] = "${$host.hostTheme == 'dark'}"
            });
            text["$when"] = "${$host.hostTheme != 'dark'}";
        }
        valueItems.Add(text);
        if (valueIcon != null)
            items.Add(IconRow(valueItems, valueIcon, colorIndex, "None", alignToValueImage: !highContrast.Value));
    }

    internal void AddHighlightedEntity(string entity, EntityState? reading, int index, int count, string? caption = null,
        object? rowAction = null)
    {
        var (name, value, state, contact) = Describe(entity, reading);
        object? action = rowAction ?? EntityAction(entity, state);
        if (action != null && BoardSelection.IsButton(entity)) value = UiText.Get("Drücken", "Press");
        int color = contact && state is "on" or "off" ? (state == "on" ? 0 : 2) : index;
        var items = new List<object>();
        AddHighlight(items, string.IsNullOrWhiteSpace(caption) ? name : caption, value, color, index == 0, compact && count > 1, imageWidth: 222);
        var row = IconRow(items, BoardIcon.ForEntity(entity, reading), color, large ? "Large" : "Small");
        if (action != null)
            AddActionRow(row, action);
        else body.Add(row);
    }

    static (string Name, string Value, string? State, bool Contact) Describe(string entity, EntityState? reading)
    {
        string name = string.IsNullOrWhiteSpace(reading?.Attributes?.Name) ? entity : reading.Attributes!.Name!;
        string? state = reading?.State;
        bool contact = IsContact(entity, reading);
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

    internal static bool IsContact(string entity, EntityState? reading) =>
        entity.StartsWith("binary_sensor.", StringComparison.Ordinal) &&
        (reading?.Attributes?.DeviceClass is "door" or "garage_door" or "opening" or "window" ||
            BoardSelection.ContactIconFromId(entity).Length != 0);

    internal static bool IsGarage(string entity) =>
        BoardSelection.ContactIconFromId(entity) == "garage";

    internal void AddCompactStatus(string entity, EntityState? reading, string? caption, object? action,
        bool hideName = false, string? fontSize = null, string? customIcon = null)
    {
        var (name, value, state, contact) = Describe(entity, reading);
        int color = contact && state is "on" or "off" ? (state == "on" ? 0 : 2) : 1;
        string? preferred = string.IsNullOrWhiteSpace(customIcon) && contact &&
            (IsGarage(entity) || string.IsNullOrWhiteSpace(reading?.Attributes?.DeviceClass))
            ? BoardSelection.ContactIconFromId(entity) : customIcon;
        string icon = preferred switch
        {
            "garage" or "door" => contact && state == "on" ? preferred + "-open" : preferred,
            "window-closed" => contact && state == "on" ? "window-open" : preferred,
            "toggle-switch" => state == "on" ? "toggle-switch" : "toggle-switch-off",
            "led-strip" or "lightbulb" or "light" => preferred,
            "battery" or "solar-panel" or "none" => preferred,
            _ => BoardIcon.ForEntity(entity, reading)
        };
        string label = string.IsNullOrWhiteSpace(caption) ? name : caption;
        string size = fontSize is "Small" or "Large" ? fontSize : "Default";
        var columns = new List<object>();
        if (icon != "none") columns.Add(IconColumn(icon, color, sizeOverride: size == "Large" ? 25 : size == "Small" ? 18 : 22));
        if (!hideName) columns.Add(new { type = "Column", width = "stretch", verticalContentAlignment = "Center", items = new[] {
            new { type = "TextBlock", text = Bind(label), size, wrap = true, maxLines = 1 } } });
        columns.Add(new { type = "Column", width = hideName ? "stretch" : "auto", verticalContentAlignment = "Center", items = new[] {
            new { type = "TextBlock", text = Bind(value), size, weight = "Bolder",
                color = HighlightColors[color], wrap = true, maxLines = 1 } } });
        var row = new Dictionary<string, object>
        {
            ["type"] = "ColumnSet", ["spacing"] = "Small", ["columns"] = columns
        };
        if (action != null) AddActionRow(row, action);
        else body.Add(row);
    }

    internal void AddControl(string entity, EntityState? reading, string? label)
    {
        if (EntityAction(entity, reading?.State, label) is { } action)
            body.Add(new { type = "ActionSet", spacing = "Small", actions = new[] { action } });
    }

    internal void Add(string entity, EntityState? reading, string? caption = null, object? rowAction = null)
    {
        var (name, value, state, _) = Describe(entity, reading);
        if (!string.IsNullOrWhiteSpace(caption)) name = caption;
        object? action = rowAction ?? EntityAction(entity, state);
        if (compact && action != null && BoardSelection.IsButton(entity)) value = UiText.Get("Drücken", "Press");

        int color = state == "on" ? 2 : 1;
        var columns = new List<object>
        {
            IconColumn(BoardIcon.ForEntity(entity, reading), color),
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
        if (action != null && !compact && rowAction == null)
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
            ["type"] = "ColumnSet", ["spacing"] = large ? "Large" : compact ? "Small" : "Default",
            ["columns"] = columns
        };
        if ((compact || rowAction != null) && action != null) AddActionRow(row, action);
        else body.Add(row);
    }

    Dictionary<string, object> IconRow(List<object> items, string icon, int color, string spacing,
        bool alignToValueImage = false) => new()
    {
        ["type"] = "ColumnSet", ["spacing"] = spacing, ["columns"] = new object[]
        {
            IconColumn(icon, color, alignToValueImage),
            new { type = "Column", width = "stretch", verticalContentAlignment = "Center", items }
        }
    };

    int IconSize => compact ? 16 : large ? 28 : 22;

    object IconColumn(string icon, int color, bool alignToValueImage = false, int? sizeOverride = null)
    {
        int size = sizeOverride ?? IconSize;
        // The rendered digits sit below the center of their image. Eight pixels of top padding
        // move the visible icon down four pixels when the host centers the image column.
        int topPadding = alignToValueImage ? 8 : 0;
        object Image(int rgb, string condition, int padding = 0) => new Dictionary<string, object>
        {
            ["type"] = "Image", ["url"] = Bind(BoardIcon.Render(icon, size, rgb, padding)),
            ["width"] = size + "px", ["height"] = (size + padding) + "px", ["altText"] = "",
            ["fallback"] = "drop", ["$when"] = condition
        };
        return new
        {
            type = "Column", width = "auto", verticalContentAlignment = "Center", items = new[]
            {
                Image(HighlightRgb[color], "${$host.hostTheme == 'dark'}", topPadding),
                Image(LightIconRgb[color], "${$host.hostTheme != 'dark'}")
            }
        };
    }

    internal object? EntityAction(string entity, string? state, string? label = null)
    {
        if (BoardSelection.IsButton(entity) && state is not (null or "unavailable"))
            return new Dictionary<string, object>
            {
                ["type"] = "Action.Execute", ["verb"] = "press-button", ["data"] = new { entity },
                ["title"] = string.IsNullOrWhiteSpace(label) ? UiText.Get("Drücken", "Press") : Bind(label),
                ["$when"] = "${$host.isUserContextAuthenticated}"
            };
        if (!BoardSelection.IsSwitch(entity) || state is not ("on" or "off")) return null;
        bool on = state == "off";
        return new Dictionary<string, object>
        {
            ["type"] = "Action.Execute", ["verb"] = "set-switch", ["data"] = new { entity, on },
            ["title"] = string.IsNullOrWhiteSpace(label)
                ? on ? UiText.Get("Einschalten", "Turn on") : UiText.Get("Ausschalten", "Turn off") : Bind(label),
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

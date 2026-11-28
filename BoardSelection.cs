using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

static class BoardSelection
{
    internal const int SmallCapacity = 3;
    internal const int MediumCapacity = 6;
    internal const int Capacity = 8;
    internal const int TwoColumnCapacity = 12;
    internal const int MaxDecimalPlaces = 6;
    internal static readonly string[] SolarLabels = { "PV + BKW", UiText.Get("Hausverbrauch", "Home consumption"),
        UiText.Get("Akku", "Battery") };
    internal static readonly string[] SolarDailyLabels = { UiText.Get("Erzeugung", "Generation"),
        UiText.Get("Hausverbrauch", "Home use"), UiText.Get("Einspeisung", "Grid export"),
        UiText.Get("Netzbezug", "Grid import") };
    internal static readonly string SolarGridLabel = UiText.Get("Netz", "Grid");
    internal static int SolarGridCaptionIndex { get { return SolarLabels.Length + 1 + SolarDailyLabels.Length; } }
    internal static readonly string[] SolarCaptionDefaults = SolarLabels
        .Concat(new[] { UiText.Get("Heute", "Today") }).Concat(SolarDailyLabels).Concat(new[] { SolarGridLabel }).ToArray();
    internal const string FileName = "widget-board.json";
    internal static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ReleaseVersion.DataFolder);
    internal static readonly string FilePath = Path.Combine(Folder, FileName);

    internal static bool IsSwitch(string entity)
    {
        return Regex.IsMatch(entity, @"\A(switch|light|input_boolean|fan)\.[a-z0-9_]+\z");
    }

    internal static bool IsSupported(string entity)
    {
        return IsSwitch(entity) || IsButton(entity) || Regex.IsMatch(entity, @"\A(sensor|binary_sensor)\.[a-z0-9_]+\z");
    }

    internal static bool IsButton(string entity)
    {
        return Regex.IsMatch(entity, @"\A(button|input_button)\.[a-z0-9_]+\z");
    }

    internal static bool IsSolarDailySensor(string entity)
    {
        return !string.IsNullOrEmpty(entity) && Regex.IsMatch(entity, @"\Asensor\.[a-z0-9_]+\z");
    }

    internal static string ContactIconFromId(string entity)
    {
        if (entity.IndexOf("garagentor", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entity.IndexOf("garage_door", StringComparison.OrdinalIgnoreCase) >= 0) return "garage";
        if (!entity.StartsWith("binary_sensor.", StringComparison.OrdinalIgnoreCase)) return "";
        if (entity.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entity.IndexOf("fenster", StringComparison.OrdinalIgnoreCase) >= 0) return "window-closed";
        if (entity.IndexOf("tuer", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entity.IndexOf("_tur", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entity.IndexOf("garagentur", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entity.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entity.IndexOf("contact", StringComparison.OrdinalIgnoreCase) >= 0) return "door";
        return "";
    }
}

sealed class BoardConfiguration : SolarBoardSettings
{
    public BoardConfiguration()
    {
        Entities = new string[0];
        Profiles = new BoardProfile[0];
        SolarProfiles = new SolarBoardProfile[0];
        DefaultWidgetText = new BoardWidgetText();
        DefaultSolarName = "";
        SolarLiveEntities = new string[0];
    }
    public string[] Entities { get; set; }
    public bool HighlightDefault { get; set; }
    public BoardWidgetText DefaultWidgetText { get; set; }
    public BoardProfile[] Profiles { get; set; }
    public SolarBoardProfile[] SolarProfiles { get; set; }
    public string DefaultSolarName { get; set; }
    public string[] SolarLiveEntities { get; set; }

    internal string[] DefaultSolarSensors(string[] desktop, string export)
    {
        if (SolarLiveEntities != null && SolarLiveEntities.Length == 5) return SolarLiveEntities;
        desktop = desktop ?? new string[0];
        return new[] { desktop.ElementAtOrDefault(0) ?? "", desktop.ElementAtOrDefault(2) ?? "",
            desktop.ElementAtOrDefault(1) ?? "", desktop.ElementAtOrDefault(3) ?? "", export ?? "" };
    }

    internal string SolarDisplayName()
    {
        return string.IsNullOrWhiteSpace(DefaultSolarName) ? UiText.Get("Solaranlage", "Solar installation")
            : DefaultSolarName.Trim();
    }

    internal SolarBoardProfile[] AllSolarProfiles()
    {
        var profiles = new System.Collections.Generic.List<SolarBoardProfile>();
        foreach (var profile in SolarProfiles ?? new SolarBoardProfile[0])
        {
            Guid id;
            if (profile != null && Guid.TryParseExact(profile.Id, "N", out id) &&
                !string.IsNullOrWhiteSpace(profile.Name) && !profiles.Exists(item => item.Id == profile.Id))
                profiles.Add(profile);
        }
        return profiles.ToArray();
    }

    internal BoardProfile[] SolarChoices()
    {
        return new[] { new BoardProfile { Name = SolarDisplayName() } }
            .Concat(AllSolarProfiles()).ToArray();
    }

    internal BoardProfile[] AllProfiles()
    {
        var profiles = new System.Collections.Generic.List<BoardProfile>
        {
            new BoardProfile { Id = "", Name = "Home Assistant", Entities = Entities ?? new string[0],
                HighlightValues = HighlightDefault, WidgetText = DefaultWidgetText }
        };
        if (Profiles != null)
            foreach (var profile in Profiles)
            {
                Guid id;
                if (profile != null && Guid.TryParseExact(profile.Id, "N", out id) &&
                    !string.IsNullOrWhiteSpace(profile.Name) && !profiles.Exists(item => item.Id == profile.Id))
                    profiles.Add(profile);
            }
        return profiles.ToArray();
    }

}

class SolarBoardSettings
{
    public SolarBoardSettings()
    {
        SolarDecimals = new int?[0];
        SolarDailyDecimals = new int?[0];
        SolarDailyEntities = new string[0];
        SolarCaptions = new string[0];
    }
    public int?[] SolarDecimals { get; set; }
    public int?[] SolarDailyDecimals { get; set; }
    public string[] SolarDailyEntities { get; set; }
    public string[] SolarCaptions { get; set; }
    public bool? SolarShowIcons { get; set; }

    internal int? DecimalPlaces(int index)
    {
        int? places = SolarDecimals != null && index < SolarDecimals.Length ? SolarDecimals[index] : null;
        return places >= 0 && places <= BoardSelection.MaxDecimalPlaces ? places : null;
    }

    internal int? DailyDecimalPlaces(int index)
    {
        int? places = SolarDailyDecimals != null ? SolarDailyDecimals.ElementAtOrDefault(index) : null;
        return places >= 0 && places <= BoardSelection.MaxDecimalPlaces ? places : null;
    }

    internal string SolarCaption(int index)
    {
        var caption = (SolarCaptions ?? new string[0]).ElementAtOrDefault(index);
        return string.IsNullOrWhiteSpace(caption) ? BoardSelection.SolarCaptionDefaults[index] : caption.Trim();
    }
}

class BoardProfile
{
    public BoardProfile()
    {
        Id = Name = "";
        Entities = new string[0];
        WidgetText = new BoardWidgetText();
    }

    public string Id { get; set; }
    public string Name { get; set; }
    public string[] Entities { get; set; }
    public bool HighlightValues { get; set; }
    public BoardWidgetText WidgetText { get; set; }
    public override string ToString() { return Name; }
}

sealed class BoardWidgetText
{
    public BoardWidgetText()
    {
        Description = StatusCaption = ActionEntity = ActionCaption = CompactTitle = "";
        CompactHideNames = new bool[0];
        CompactCaptions = new string[0];
        CompactSizes = new string[0];
        CompactIcons = new string[0];
        RowActions = new string[0];
        Columns = new int[0];
        SizeHighlights = new bool[0];
        PreferredWidgetType = -1;
    }
    public string Description { get; set; }
    public bool UseCustomDescription { get; set; }
    public string StatusCaption { get; set; }
    public string ActionEntity { get; set; }
    public string[] RowActions { get; set; }
    public string ActionCaption { get; set; }
    public string CompactTitle { get; set; }
    public bool[] CompactHideNames { get; set; }
    public string[] CompactCaptions { get; set; }
    public string[] CompactSizes { get; set; }
    public string[] CompactIcons { get; set; }
    public int[] Columns { get; set; }
    public bool HideTitle { get; set; }
    public bool[] SizeHighlights { get; set; }
    public bool ShowColumnDivider { get; set; }
    public int PreferredWidgetType { get; set; }
    public int PreferredWidgetSize { get; set; }

    internal bool HighlightFor(int index, bool fallback)
    {
        return SizeHighlights != null && index >= 0 && index < SizeHighlights.Length ? SizeHighlights[index] : fallback;
    }

    internal static bool[] RightColumns(int[] slots, int[] columns)
    {
        columns = columns ?? new int[0];
        var placements = slots.Select(slot => columns.ElementAtOrDefault(slot)).ToArray();
        int automaticLeft = Math.Max(0, (slots.Length + 1) / 2 - placements.Count(column => column == 1));
        return placements.Select(column => column == 2 || (column == 0 && automaticLeft-- <= 0)).ToArray();
    }
}

sealed class SolarBoardProfile : BoardProfile
{
    public SolarBoardProfile()
    {
        Solar = new SolarBoardSettings();
        GridExportEntity = "";
    }
    public SolarBoardSettings Solar { get; set; }
    public string GridExportEntity { get; set; }
}

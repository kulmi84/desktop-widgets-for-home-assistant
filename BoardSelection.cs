using System;
using System.IO;
using System.Text.RegularExpressions;

static class BoardSelection
{
    internal const int Capacity = 3;
    internal const int MaxDecimalPlaces = 6;
    internal static readonly string[] SolarLabels = { "PV + BKW", UiText.Get("Hausverbrauch", "Home consumption"),
        UiText.Get("Akku", "Battery") };
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
}

sealed class BoardConfiguration
{
    public BoardConfiguration()
    {
        Entities = new string[0];
        SolarDecimals = new int?[0];
        Profiles = new BoardProfile[0];
    }
    public string[] Entities { get; set; }
    public bool HighlightDefault { get; set; }
    public int?[] SolarDecimals { get; set; }
    public BoardProfile[] Profiles { get; set; }

    internal BoardProfile[] AllProfiles()
    {
        var profiles = new System.Collections.Generic.List<BoardProfile>
        {
            new BoardProfile { Id = "", Name = "Home Assistant", Entities = Entities ?? new string[0],
                HighlightValues = HighlightDefault }
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

    internal int? DecimalPlaces(int index)
    {
        int? places = SolarDecimals != null && index < SolarDecimals.Length ? SolarDecimals[index] : null;
        return places >= 0 && places <= BoardSelection.MaxDecimalPlaces ? places : null;
    }
}

sealed class BoardProfile
{
    public BoardProfile()
    {
        Id = Name = "";
        Entities = new string[0];
    }

    public string Id { get; set; }
    public string Name { get; set; }
    public string[] Entities { get; set; }
    public bool HighlightValues { get; set; }
    public override string ToString() { return Name; }
}

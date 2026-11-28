using System;
using System.Globalization;
using System.IO;

static class UiText
{
    static readonly string LanguageFolder = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), ReleaseVersion.DataFolder);
    static readonly string LanguageFile = Path.Combine(LanguageFolder, "language.txt");

    internal static readonly string Preference = ReadPreference();
    internal static readonly bool English = UseEnglish();

    static string ReadPreference()
    {
        try
        {
            if (File.Exists(LanguageFile))
            {
                string value = File.ReadAllText(LanguageFile).Trim();
                if (value == "en" || value == "de") return value;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return "system";
    }

    static bool UseEnglish()
    {
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "--self-test") >= 0)
            return Array.IndexOf(args, "--ui-en") >= 0;
        return Preference == "en" || (Preference == "system" &&
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "de");
    }

    internal static void SavePreference(string value)
    {
        Directory.CreateDirectory(LanguageFolder);
        File.WriteAllText(LanguageFile, value);
    }

    internal static string Get(string german, string english) { return English ? english : german; }

    internal static string Status(string text)
    {
        switch (text)
        {
            case "Ein": case "On": return Get("Ein", "On");
            case "Aus": case "Off": return Get("Aus", "Off");
            case "Offen": case "Open": return Get("Offen", "Open");
            case "Geschlossen": case "Closed": return Get("Geschlossen", "Closed");
            default: return text;
        }
    }
}

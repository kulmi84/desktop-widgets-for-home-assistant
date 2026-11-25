using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class ProfileBackup
{
    static readonly string BackupFilter = UiText.Get("Widget-Sicherung (*.json)|*.json", "Widget backup (*.json)|*.json");
    static readonly string Folder = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), ReleaseVersion.DataFolder);
    static readonly string[] WidgetFiles = { "doors.json", "switches.json", "heating.json", "covers.json",
        BoardSelection.FileName,
        "heating-position.json", "covers-position.json", "widgets.txt", "position.txt", WidgetPlacement.FileName };

    public sealed class Backup
    {
        public int Format { get; set; }
        public string[] Entities { get; set; }
        public string GridExportEntity { get; set; }
        public Appearance Appearance { get; set; }
        public Dictionary<string, string> Widgets { get; set; }
    }

    internal static void Export(IWin32Window owner, Settings settings)
    {
        using (var dialog = new SaveFileDialog { Filter = BackupFilter,
            FileName = "DesktopWidgets-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json" })
        {
            if (dialog.ShowDialog(owner) != DialogResult.OK) return;
            try
            {
                var data = new Backup { Format = 1, Widgets = new Dictionary<string, string>() };
                foreach (string file in WidgetFiles)
                {
                    var path = Path.Combine(Folder, file);
                    if (File.Exists(path)) data.Widgets[file] = File.ReadAllText(path);
                }
                foreach (var widget in DoorWidget.SavedWidgets())
                {
                    string file = "sensor-widgets/" + widget.Key + ".json";
                    data.Widgets[file] = File.ReadAllText(Path.Combine(Folder, file));
                }
                foreach (var widget in DoorWidget.SavedWidgets(true))
                {
                    string file = "switch-widgets/" + widget.Key + ".json";
                    data.Widgets[file] = File.ReadAllText(Path.Combine(Folder, file));
                }
                if (settings != null)
                {
                    data.Entities = settings.Entities;
                    data.GridExportEntity = settings.GridExportEntity;
                    data.Appearance = settings.Appearance;
                }
                Write(dialog.FileName, new JavaScriptSerializer().Serialize(data));
                MessageBox.Show(owner, UiText.Get("Widget-Einstellungen wurden gesichert.", "Widget settings backed up."), ReleaseVersion.ProductName);
            }
            catch (Exception) { MessageBox.Show(owner, UiText.Get("Die Sicherung konnte nicht gespeichert werden.", "Could not save the backup."), ReleaseVersion.ProductName); }
        }
    }

    internal static bool Import(IWin32Window owner, Settings settings)
    {
        if (settings == null)
        {
            MessageBox.Show(owner, UiText.Get("Bitte zuerst mit Home Assistant verbinden.", "Connect to Home Assistant first."), ReleaseVersion.ProductName);
            return false;
        }
        using (var dialog = new OpenFileDialog { Filter = BackupFilter })
        {
            if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 2 * 1024 * 1024)
                    throw new InvalidDataException();
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Backup>(File.ReadAllText(dialog.FileName));
                if (data == null || data.Format != 1 || data.Widgets == null ||
                    data.Widgets.Any(pair => !Allowed(pair.Key) || pair.Value == null) ||
                    (data.Entities != null && (data.Entities.Length != 4 || data.Entities.Any(entity => !ValidEntity(entity)))) ||
                    !ValidEntity(data.GridExportEntity))
                    throw new InvalidDataException();
                foreach (var pair in data.Widgets)
                    if (pair.Key.EndsWith(".json", StringComparison.Ordinal) &&
                        !(serializer.DeserializeObject(pair.Value) is Dictionary<string, object>) &&
                        !(serializer.DeserializeObject(pair.Value) is object[]))
                        throw new InvalidDataException();

                if (MessageBox.Show(owner, UiText.Get("Gesicherte Widgets und Positionen übernehmen? Die App wird danach beendet. Öffne sie anschließend erneut.", "Restore the saved widgets and positions? The app will close. Open it again afterward."),
                    ReleaseVersion.ProductName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return false;

                var paths = data.Widgets.ToDictionary(pair => Path.Combine(Folder, pair.Key), pair => pair.Value);
                var before = paths.Keys.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllText(path) : null);
                var applied = new List<string>();
                try
                {
                    foreach (var pair in paths)
                    {
                        Write(pair.Key, pair.Value);
                        applied.Add(pair.Key);
                    }
                    if (data.Entities != null)
                    {
                        var next = new Settings { Url = settings.Url, Token = settings.Token, Entities = data.Entities,
                            GridExportEntity = data.GridExportEntity, Appearance = data.Appearance };
                        next.Save();
                    }
                }
                catch
                {
                    foreach (string path in applied)
                    {
                        if (before[path] == null) File.Delete(path);
                        else Write(path, before[path]);
                    }
                    throw;
                }
                return true;
            }
            catch (Exception)
            {
                MessageBox.Show(owner, UiText.Get("Die Sicherung konnte nicht wiederhergestellt werden.", "Could not restore the backup."), ReleaseVersion.ProductName);
                return false;
            }
        }
    }

    static bool ValidEntity(string entity)
    {
        return string.IsNullOrEmpty(entity) || System.Text.RegularExpressions.Regex.IsMatch(entity, @"^[a-z0-9_]+\.[a-z0-9_]+$");
    }

    static bool Allowed(string name)
    {
        string prefix = name.StartsWith("switch-widgets/", StringComparison.Ordinal) ? "switch-widgets/" : "sensor-widgets/";
        return WidgetFiles.Contains(name) || (name.StartsWith(prefix, StringComparison.Ordinal) &&
            name.EndsWith(".json", StringComparison.Ordinal) &&
            DoorWidget.IsSensorId(name.Substring(prefix.Length, name.Length - prefix.Length - 5)));
    }

    internal static void Write(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, contents);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { File.Delete(temp); }
    }
}

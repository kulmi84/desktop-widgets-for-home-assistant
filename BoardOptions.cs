#if STORE
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class BoardOptions : Form
{
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly TextBox[] fields = new TextBox[BoardSelection.Capacity];
    readonly ComboBox[] decimals = new ComboBox[BoardSelection.SolarLabels.Length];
    readonly Settings connection;
    readonly List<BoardProfile> profiles = new List<BoardProfile>();
    readonly ComboBox profilePicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox profileName = new TextBox { MaxLength = 40 };
    readonly ComboBox layoutPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    BoardProfile editing;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        SettingsTheme.UseRoundedWindowCorners(Handle);
    }

    internal BoardOptions(Settings connection)
    {
        this.connection = connection;
        Text = UiText.Get("Windows-Widgetboard", "Windows Widgets Board");
        Icon = WidgetIcon.Load();
        Font = new Font("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        ClientSize = new Size(520, 408);
        BackColor = SettingsTheme.Surface;
        ForeColor = Color.WhiteSmoke;

        Controls.Add(new Label
        {
            Text = UiText.Get("Bis zu drei Sensoren, Schalter oder Taster pro Kachel auswählen.",
                "Select up to three sensors, switches or buttons per widget."),
            Bounds = new Rectangle(14, 14, 492, 24)
        });

        var config = new BoardConfiguration();
        try
        {
            if (File.Exists(BoardSelection.FilePath))
            {
                string contents = File.ReadAllText(BoardSelection.FilePath).TrimStart();
                if (contents.StartsWith("[", StringComparison.Ordinal)) config.Entities = json.Deserialize<string[]>(contents);
                else config = json.Deserialize<BoardConfiguration>(contents) ?? config;
            }
        }
        catch (Exception)
        {
            Controls.Add(new Label { Text = UiText.Get("Gespeicherte Auswahl konnte nicht gelesen werden.",
                "Could not read saved selection."), AutoSize = true, Location = new Point(14, 255) });
        }

        profiles.AddRange(config.AllProfiles());
        profilePicker.Bounds = new Rectangle(14, 42, 230, 26);
        profilePicker.AccessibleName = UiText.Get("Kachel auswählen", "Choose widget");
        profilePicker.BackColor = profileName.BackColor = SettingsTheme.Input;
        profilePicker.ForeColor = profileName.ForeColor = ForeColor;
        profilePicker.FlatStyle = FlatStyle.Flat;
        profileName.Bounds = new Rectangle(14, 77, 492, 24);
        profileName.AccessibleName = UiText.Get("Kachelname", "Widget name");
        var add = new SettingsButton { Text = UiText.Get("Neue Kachel", "New widget"),
            Bounds = new Rectangle(250, 40, 123, 28) };
        var remove = new SettingsButton { Text = UiText.Get("Entfernen", "Remove"),
            Bounds = new Rectangle(380, 40, 126, 28) };
        add.Click += delegate
        {
            StoreProfile();
            var added = new BoardProfile { Id = Guid.NewGuid().ToString("N"), Name = UiText.Get("Neue Kachel", "New widget") };
            profiles.Add(added);
            ShowProfiles(profiles.Count - 1);
            profileName.SelectAll();
            profileName.Focus();
        };
        remove.Click += delegate
        {
            if (editing == null || editing.Id.Length == 0) return;
            profiles.Remove(editing);
            editing = null;
            ShowProfiles(0);
        };
        profilePicker.SelectionChangeCommitted += delegate
        {
            StoreProfile();
            int selectedIndex = profilePicker.SelectedIndex;
            ShowProfiles(selectedIndex);
        };
        profilePicker.SelectedIndexChanged += delegate { remove.Enabled = profilePicker.SelectedIndex > 0; };
        Controls.AddRange(new Control[] { profilePicker, profileName, add, remove });
        var layoutLabel = UiText.Get("Darstellung", "Layout");
        Controls.Add(new Label { Text = layoutLabel, AutoSize = true, Location = new Point(14, 112) });
        layoutPicker.Bounds = new Rectangle(124, 107, 382, 26);
        layoutPicker.AccessibleName = layoutLabel;
        layoutPicker.BackColor = SettingsTheme.Input;
        layoutPicker.ForeColor = ForeColor;
        layoutPicker.FlatStyle = FlatStyle.Flat;
        layoutPicker.Items.AddRange(new object[] { UiText.Get("Liste", "List"), UiText.Get("Große Werte", "Large values") });
        Controls.Add(layoutPicker);
        for (int i = 0; i < fields.Length; i++)
        {
            int y = 142 + 35 * i;
            var field = new TextBox
            {
                AccessibleName = UiText.Get("Entität ", "Entity ") + (i + 1),
                BackColor = SettingsTheme.Input, ForeColor = ForeColor,
                Bounds = new Rectangle(14, y + 3, 357, 24)
            };
            fields[i] = field;
            var browse = new SettingsButton { Text = UiText.Get("Auswählen…", "Select…"),
                Bounds = new Rectangle(380, y, 126, 28) };
            browse.Click += async delegate
            {
                var targetProfile = editing;
                browse.Enabled = false;
                try
                {
                    if (this.connection == null)
                    {
                        MessageBox.Show(this, UiText.Get("Bitte zuerst mit Home Assistant verbinden.",
                            "Connect to Home Assistant first."), Text);
                        return;
                    }
                    var sensors = await Options.LoadSensors(new Uri(this.connection.Url), this.connection.ReadToken());
                    if (IsDisposed || editing != targetProfile) return;
                    using (var picker = new Options.SensorPicker(sensors, "board", field.Text))
                        if (picker.ShowDialog(this) == DialogResult.OK) field.Text = picker.SelectedEntity;
                }
                catch (Exception)
                {
                    if (!IsDisposed) MessageBox.Show(this, UiText.Get("Entitäten konnten nicht geladen werden. Bitte Verbindung prüfen.",
                        "Could not load entities. Check the connection."), Text);
                }
                finally { if (!browse.IsDisposed) browse.Enabled = true; }
            };
            Controls.Add(field);
            Controls.Add(browse);
        }
        ShowProfiles(0);

        var solarGroup = new SettingsCard { Text = UiText.Get("Solaranlage · Nachkommastellen", "Solar installation · Decimal places"),
            ForeColor = ForeColor, Bounds = new Rectangle(14, 277, 492, 78) };
        for (int i = 0; i < decimals.Length; i++)
        {
            int x = 12 + i * 158;
            string label = BoardSelection.SolarLabels[i];
            solarGroup.Controls.Add(new Label { Text = label, Location = new Point(x, 23), AutoSize = true });
            var selection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = label,
                BackColor = SettingsTheme.Input, ForeColor = ForeColor, FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(x, 43, 148, 25) };
            selection.Items.Add(UiText.Get("Automatisch", "Automatic"));
            for (int places = 0; places <= BoardSelection.MaxDecimalPlaces; places++) selection.Items.Add(places);
            selection.SelectedIndex = (config.DecimalPlaces(i) ?? -1) + 1;
            decimals[i] = selection;
            solarGroup.Controls.Add(selection);
        }
        Controls.Add(solarGroup);
        var cancel = new SettingsButton { Text = UiText.Get("Abbrechen", "Cancel"),
            Bounds = new Rectangle(274, 367, 110, 28), DialogResult = DialogResult.Cancel };
        var save = new SettingsButton { Primary = true, Text = UiText.Get("Speichern", "Save"),
            Bounds = new Rectangle(396, 367, 110, 28) };
        save.Click += Save;
        Controls.Add(cancel);
        Controls.Add(save);
        CancelButton = cancel;
        AcceptButton = save;
    }

    void ShowProfiles(int selected)
    {
        profilePicker.Items.Clear();
        profilePicker.Items.AddRange(profiles.ToArray());
        profilePicker.SelectedIndex = selected;
        editing = profiles[selected];
        profileName.Text = editing.Name;
        profileName.ReadOnly = editing.Id.Length == 0;
        layoutPicker.SelectedIndex = editing.HighlightValues ? 1 : 0;
        string[] entities = editing.Entities ?? new string[0];
        for (int i = 0; i < fields.Length; i++) fields[i].Text = i < entities.Length ? entities[i] : "";
    }

    void StoreProfile()
    {
        if (editing == null) return;
        editing.Name = profileName.Text.Trim();
        editing.HighlightValues = layoutPicker.SelectedIndex == 1;
        editing.Entities = fields.Select(field => field.Text.Trim().ToLowerInvariant()).ToArray();
    }

    void Save(object sender, EventArgs args)
    {
        StoreProfile();
        int invalid = profiles.FindIndex(profile => string.IsNullOrWhiteSpace(profile.Name) ||
            (profile.Entities ?? new string[0]).Any(entity => !string.IsNullOrEmpty(entity) && !BoardSelection.IsSupported(entity)) ||
            (profile.Entities ?? new string[0]).Where(entity => !string.IsNullOrEmpty(entity))
                .GroupBy(entity => entity).Any(group => group.Count() > 1));
        if (invalid >= 0)
        {
            ShowProfiles(invalid);
            MessageBox.Show(this, UiText.Get("Bitte einen Namen und gültige, unterschiedliche Entitäts-IDs eingeben.",
                "Enter a name and valid, distinct entity IDs."), Text);
            return;
        }
        try
        {
            var config = new BoardConfiguration
            {
                Entities = profiles[0].Entities,
                HighlightDefault = profiles[0].HighlightValues,
                Profiles = profiles.Skip(1).ToArray(),
                SolarDecimals = decimals.Select(field => field.SelectedItem is int ? (int?)field.SelectedItem : null).ToArray()
            };
            ProfileBackup.Write(BoardSelection.FilePath, json.Serialize(config));
            DialogResult = DialogResult.OK;
        }
        catch (Exception)
        {
            MessageBox.Show(this, UiText.Get("Die Auswahl konnte nicht gespeichert werden.",
                "Could not save the selection."), Text);
        }
    }
}
#endif

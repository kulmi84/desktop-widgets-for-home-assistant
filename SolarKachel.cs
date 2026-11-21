using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class Appearance
{
    public int Background { get; set; }
    public int Text { get; set; }
    public int Icons { get; set; }
    public int TextSize { get; set; }
    public string Typeface { get; set; }
    public int BackgroundOpacity { get; set; }
    public int IconSize { get; set; }
    public int MetricSpacing { get; set; }
    public int FramePadding { get; set; }
    public bool Vertical { get; set; }
    public bool ValueBesideName { get; set; }
    public int OpenIconColor { get; set; }
    public HorizontalAnchor HorizontalAnchor { get; set; }
    public int ForegroundMinutes { get; set; }
    public int ForegroundBlinks { get; set; }
    public int BlinkCycleSeconds { get; set; }
    public int ShowForegroundSeconds { get; set; }
    public bool ForegroundWhileBlinking { get; set; }
    public bool BackgroundOnAnyClick { get; set; }
    public bool FixedValueWidth { get; set; }
    public int[] FieldWidths { get; set; }
    internal Action<float[], float[], float> ReportFields;

    public Appearance()
    {
        Background = Color.FromArgb(250, 250, 250).ToArgb();
        Text = Color.FromArgb(32, 32, 32).ToArgb();
        Icons = Color.FromArgb(55, 55, 55).ToArgb();
        TextSize = 10;
        Typeface = "Segoe UI";
        BackgroundOpacity = 100;
        IconSize = 44;
        MetricSpacing = 18;
        FramePadding = 14;
        OpenIconColor = Color.Firebrick.ToArgb();
        BlinkCycleSeconds = 2;
        FixedValueWidth = true;
    }
}

sealed class Settings
{
    public string Url { get; set; }
    public string Token { get; set; }
    public string[] Entities { get; set; }
    public string GridExportEntity { get; set; }
    public Appearance Appearance { get; set; }

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ReleaseVersion.DataFolder, "settings.json");

    internal static Settings Load()
    {
        if (!File.Exists(FilePath)) return null;
        var settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath));
        if (settings == null || settings.Entities == null || settings.Entities.Length != 4 ||
            settings.Url == null || settings.Token == null) return null;
        return settings;
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
        finally { File.Delete(temp); }
    }

    internal static string Protect(string token)
    {
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
    }

    internal string ReadToken()
    {
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Token), null, DataProtectionScope.CurrentUser));
    }
}

sealed class Options : Form
{
    readonly TextBox address;
    readonly TextBox token;
    readonly TextBox[] entities = new TextBox[4];
    readonly Settings previous;
    readonly TextBox gridExport;
    static readonly Color Surface = SettingsTheme.Surface;
    static readonly Color Input = SettingsTheme.Input;
    static readonly Color Border = SettingsTheme.Border;
    readonly GroupBox connection = new SettingsCard { Text = "Home Assistant" };
    readonly GroupBox design = new SettingsCard { Text = UiText.Get("Darstellung", "Appearance") };
    readonly NumericUpDown iconSize;
    readonly NumericUpDown spacing;
    readonly NumericUpDown padding;
    readonly Button background;
    readonly Button textColor;
    readonly Button iconColor;
    readonly NumericUpDown textSize;
    readonly ComboBox typeface;
    readonly ComboBox horizontalAnchor;
    readonly NumericUpDown opacity;
    readonly NumericUpDown foregroundMinutes, foregroundBlinks, blinkCycleSeconds;
    readonly CheckBox foregroundWhileBlinking;
    readonly ComboBox backgroundClick;
    readonly CheckBox fixedValueWidth;
    int[] fieldWidths;
    Action<float[], float[], float> reportFields;
    internal Settings Result { get; private set; }

    internal Appearance SelectedAppearance { get; private set; }

    internal Options(Settings previous, string[] values, Appearance doorStyle = null, Appearance solarStyle = null,
        DoorWidget.DoorEditor doorEditor = null, bool connectionOnly = false)
    {
        this.previous = previous;
        Icon = WidgetIcon.Load();
        Text = ReleaseVersion.ProductName + UiText.Get(" – Einstellungen", " – Settings");
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        ClientSize = new Size(876, 531);
        BackColor = Surface;
        ForeColor = Color.WhiteSmoke;
        var darkTitleBar = 1;
        DwmSetWindowAttribute(Handle, 20, ref darkTitleBar, sizeof(int));
        design.Bounds = new Rectangle(12, 10, 395, 379);
        connection.Bounds = new Rectangle(417, 10, 447, 318);
        design.ForeColor = connection.ForeColor = ForeColor;
        Controls.AddRange(new Control[] { design, connection });
        var appearance = doorStyle ?? (previous == null ? new Appearance() : previous.Appearance ?? new Appearance());
        fieldWidths = appearance.FieldWidths == null ? null : (int[])appearance.FieldWidths.Clone();
        background = AddColor(UiText.Get("Hintergrundfarbe", "Background color"), appearance.Background, 23);
        textColor = AddColor(UiText.Get("Schriftfarbe", "Text color"), appearance.Text, 54);
        iconColor = AddColor(UiText.Get("Icon-Farbe", "Icon color"), appearance.Icons, 85);
        opacity = AddNumber(UiText.Get("Hintergrund-Deckkraft (%)", "Background opacity (%)"), appearance.BackgroundOpacity, 0, 100, 116);
        textSize = AddNumber(UiText.Get("Schriftgröße (pt)", "Font size (pt)"), appearance.TextSize, 8, 28, 160);
        design.Controls.Add(new Label { Text = UiText.Get("0 % = transparenter Hintergrund", "0% = transparent background"), AutoSize = true,
            ForeColor = Color.Silver, Location = new Point(14, 142) });
        iconSize = AddNumber(UiText.Get("Icon-Größe (px)", "Icon size (px)"), appearance.IconSize, 20, 96, 190);
        spacing = AddNumber(UiText.Get("Abstand zwischen den Anzeigen (px)", "Space between items (px)"), appearance.MetricSpacing, 0, 100, 220);
        padding = AddNumber(UiText.Get("Rahmengröße / Innenabstand (px)", "Padding (px)"), appearance.FramePadding, 0, 48, 250);
        design.Controls.Add(new Label { Text = UiText.Get("Schriftart", "Font"), AutoSize = true, Location = new Point(14, 285) });
        typeface = new DarkFontPicker { Bounds = new Rectangle(151, 281, 231, 26),
            DropDownStyle = ComboBoxStyle.DropDownList, Sorted = true,
            BackColor = Input, ForeColor = ForeColor, FlatStyle = FlatStyle.Flat, AccessibleName = UiText.Get("Schriftart", "Font") };
        foreach (var family in FontFamily.Families)
        {
            using (family)
                if (family.IsStyleAvailable(FontStyle.Regular) || family.IsStyleAvailable(FontStyle.Bold))
                    typeface.Items.Add(family.Name);
        }
        typeface.SelectedItem = appearance.Typeface;
        if (typeface.SelectedIndex < 0) typeface.SelectedItem = "Segoe UI";
        design.Controls.Add(typeface);
        AddPickerArrow(typeface, UiText.Get("Schriftarten öffnen", "Open font list"));
        string anchorCaption = UiText.Get("Breitenänderung", "Width alignment");
        design.Controls.Add(new Label { Text = anchorCaption, AutoSize = true, Location = new Point(14, 315) });
        horizontalAnchor = new DarkFontPicker { Bounds = new Rectangle(151, 311, 231, 26),
            DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Input, ForeColor = ForeColor,
            FlatStyle = FlatStyle.Flat, AccessibleName = anchorCaption };
        horizontalAnchor.Items.AddRange(new object[] { UiText.Get("Rechts fest", "Right edge fixed"),
            UiText.Get("Links fest", "Left edge fixed"), UiText.Get("Mittig", "Centered") });
        SelectHorizontalAnchor(appearance.HorizontalAnchor);
        design.Controls.Add(horizontalAnchor);
        AddPickerArrow(horizontalAnchor, UiText.Get("Ausrichtung auswählen", "Select alignment"));
        foregroundMinutes = AddNumber(UiText.Get("Vordergrund alle (Min., 0 = aus)", "Bring forward every (min, 0 = off)"),
            appearance.ForegroundMinutes, 0, 1440, 343);
        foregroundBlinks = AddNumber(UiText.Get("Dabei blinken (Mal, 0 = aus)", "Blink count (0 = off)"),
            appearance.ForegroundBlinks, 0, 20, 373);
        blinkCycleSeconds = AddNumber(UiText.Get("Sekunden pro Blinkvorgang", "Seconds per blink cycle"),
            Math.Max(1, Math.Min(30, appearance.BlinkCycleSeconds)), 1, 30, 403);
        foregroundWhileBlinking = new CheckBox
        {
            Text = UiText.Get("Während des Blinkens im Vordergrund bleiben", "Stay on top while blinking"),
            AutoSize = true, Checked = appearance.ForegroundWhileBlinking, Location = new Point(14, 435)
        };
        design.Controls.Add(foregroundWhileBlinking);
        design.Controls.Add(new Label { Text = UiText.Get("Danach in den Hintergrund", "Return to background"),
            AutoSize = true, Location = new Point(14, 464) });
        backgroundClick = new DarkFontPicker { Bounds = new Rectangle(14, 486, 368, 26),
            DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Input, ForeColor = ForeColor,
            FlatStyle = FlatStyle.Flat, AccessibleName = UiText.Get("Danach in den Hintergrund", "Return to background") };
        backgroundClick.Items.AddRange(new object[] {
            UiText.Get("Klick auf den Programmtitel", "Click the program title"),
            UiText.Get("Klick irgendwo ins Fenster", "Click anywhere in a window") });
        backgroundClick.SelectedIndex = appearance.BackgroundOnAnyClick ? 1 : 0;
        design.Controls.Add(backgroundClick);
        AddPickerArrow(backgroundClick, backgroundClick.AccessibleName);
        fixedValueWidth = new CheckBox { Text = UiText.Get("Feste Breite für wechselnde Werte", "Fixed width for changing values"),
            AutoSize = true, Checked = appearance.FixedValueWidth, Location = new Point(14, 519) };
        design.Controls.Add(fixedValueWidth);
        var reset = new SettingsButton { Text = UiText.Get("Standard wiederherstellen", "Restore defaults"), AutoSize = true, Location = new Point(14, 489) };
        reset.Click += delegate
        {
            var defaults = new Appearance();
            background.BackColor = Color.FromArgb(defaults.Background);
            textColor.BackColor = Color.FromArgb(defaults.Text);
            iconColor.BackColor = Color.FromArgb(defaults.Icons);
            opacity.Value = defaults.BackgroundOpacity;
            textSize.Value = defaults.TextSize;
            typeface.SelectedItem = defaults.Typeface;
            iconSize.Value = defaults.IconSize;
            spacing.Value = defaults.MetricSpacing;
            padding.Value = defaults.FramePadding;
            SelectHorizontalAnchor(defaults.HorizontalAnchor);
            foregroundMinutes.Value = defaults.ForegroundMinutes;
            foregroundBlinks.Value = defaults.ForegroundBlinks;
            blinkCycleSeconds.Value = defaults.BlinkCycleSeconds;
            foregroundWhileBlinking.Checked = defaults.ForegroundWhileBlinking;
            backgroundClick.SelectedIndex = defaults.BackgroundOnAnyClick ? 1 : 0;
            fixedValueWidth.Checked = defaults.FixedValueWidth;
            fieldWidths = null;
        };
        StyleButton(reset);
        Controls.Add(reset);
        var versionLabel = new Label { Text = ReleaseVersion.Display, ForeColor = Color.Silver,
            AutoSize = true, Location = new Point(228, 495) };
        Controls.Add(versionLabel);
        address = AddField(UiText.Get("HA-Adresse", "HA address"), previous == null ? "" : previous.Url, 24);
        token = AddField(previous == null ? UiText.Get("Zugriffstoken", "Access token") : UiText.Get("Zugriffstoken (optional)", "Access token (optional)"), "", 60);
        token.UseSystemPasswordChar = true;
        if (previous != null)
            connection.Controls.Add(new Label { Text = UiText.Get("Leer lassen, um den gespeicherten Token zu behalten.", "Leave blank to keep the saved token."),
                ForeColor = Color.Silver, AutoSize = true, Location = new Point(12, 87) });
        if (previous == null)
            connection.Controls.Add(new Label { Text = UiText.Get("Solar-Sensoren sind optional.", "Solar sensors are optional."), ForeColor = Color.Silver,
                AutoSize = true, Location = new Point(12, 87) });
        string[] labels = { UiText.Get("Solarerzeugung", "Solar generation"), UiText.Get("Akkustand", "Battery level"), UiText.Get("Hausverbrauch", "Home consumption"), UiText.Get("Netzleistung / Netzbezug", "Grid power / import") };
        for (int i = 0; i < 4; i++)
            entities[i] = AddField(labels[i], previous == null ? "" : previous.Entities[i], 110 + i * 36, i == 1 ? "%" : "W");
        gridExport = AddField(UiText.Get("Netzeinspeisung (optional)", "Grid export (optional)"),
            previous == null ? "" : previous.GridExportEntity ?? "", 254, "W");
        var save = new SettingsButton { Primary = true, Text = UiText.Get("Speichern", "Save"), Bounds = new Rectangle(764, 489, 100, 30) };
        var cancel = new SettingsButton { Text = UiText.Get("Abbrechen", "Cancel"), Bounds = new Rectangle(656, 489, 100, 30), DialogResult = DialogResult.Cancel };
        StyleButton(save);
        StyleButton(cancel);
        if (doorStyle == null)
        {
            save.Click += Save;
            AddCopyMenu(design, new Point(14, 553), "Solar", null, ApplyStyle);
        }
        Controls.AddRange(new Control[] { save, cancel });
        var previewGroup = new SettingsCard { Text = UiText.Get("Live-Vorschau", "Live preview"), ForeColor = ForeColor,
            Bounds = new Rectangle(12, 395, 852, 86) };
        var preview = new PreviewPanel(ReadAppearance, values) { Bounds = new Rectangle(10, 20, 832, 56) };
        reportFields = preview.RecordFields;
        preview.ResizeField = delegate(int index, int width, int[] measured)
        {
            if (fieldWidths == null || fieldWidths.Length != measured.Length) fieldWidths = (int[])measured.Clone();
            fieldWidths[index] = width;
        };
        previewGroup.Controls.Add(preview);
        Controls.Add(previewGroup);
        foreach (var sample in new[] { background, textColor, iconColor })
            sample.BackColorChanged += delegate { preview.Invalidate(); };
        foreach (var field in new[] { opacity, textSize, iconSize, spacing, padding })
            field.ValueChanged += delegate { preview.Invalidate(); };
        typeface.SelectedIndexChanged += delegate { preview.Invalidate(); };
        if (doorStyle != null)
        {
            Text = (doorEditor.Sensors ? DoorWidget.SensorTitle : doorEditor.Cover ? HeatingWidget.CoverTitle : doorEditor.Heating ? HeatingWidget.Title : doorEditor.Switches ? DoorWidget.SwitchTitle : DoorWidget.DoorTitle) + UiText.Get(" – Einstellungen", " – Settings");
            ClientSize = new Size(1090, 581);
            design.Height = 345;
            connection.Width = 661;
            previewGroup.Bounds = new Rectangle(12, 361, 1066, 170);
            preview.Bounds = new Rectangle(10, 20, 1046, 140);
            save.Location = new Point(978, 539);
            cancel.Location = new Point(870, 539);
            reset.Top = 539;
            versionLabel.Top = 545;
            foreach (var control in connection.Controls.Cast<Control>().ToArray()) control.Dispose();
            connection.Text = doorEditor.Sensors ? DoorWidget.SensorTitle : doorEditor.Cover ? HeatingWidget.CoverTitle : doorEditor.Heating ? UiText.Get("Thermostate", "Thermostats") : doorEditor.Switches ? UiText.Get("Schalter und Sensoren", "Switches and sensors") : DoorWidget.DoorTitle;
            doorEditor.Bounds = new Rectangle(8, 22, 645, 220);
            connection.Controls.Add(doorEditor);
            var sample = AddColor(doorEditor.Switches ? UiText.Get("Icon-Farbe bei Ein", "Icon color when on") : UiText.Get("Icon-Farbe bei offener Tür", "Icon color when open"), appearance.OpenIconColor, 247, connection);
            var showOpen = new CheckBox { Text = doorEditor.Switches ? UiText.Get("Ein-Zustand in Vorschau anzeigen", "Preview the on state") : UiText.Get("Offene Kontakte in Vorschau anzeigen", "Preview open contacts"), AutoSize = true,
                Location = new Point(doorEditor.Switches ? 160 : 16, 284) };
            connection.Controls.Add(showOpen);
            CheckBox onlyWhenOpen = null;
            CheckBox onlyOpenContacts = null;
            if (!doorEditor.Switches && !doorEditor.Sensors && !doorEditor.DeviceRows)
            {
                doorEditor.ShowForegroundSeconds = appearance.ShowForegroundSeconds;
                onlyWhenOpen = new CheckBox
                {
                    Text = UiText.Get("Nur anzeigen, wenn mindestens ein Kontakt offen ist", "Show only when at least one contact is open"),
                    AutoSize = true, Checked = doorEditor.OnlyWhenOpen, Location = new Point(16, 311)
                };
                connection.Controls.Add(onlyWhenOpen);
                onlyOpenContacts = new CheckBox
                {
                    Text = UiText.Get("Nur offene Kontakte anzeigen", "Show only open contacts"),
                    AutoSize = true, Checked = doorEditor.OnlyOpenContacts, Location = new Point(16, 338)
                };
                connection.Controls.Add(onlyOpenContacts);
                var showForeground = new NumericUpDown { Minimum = 0, Maximum = 3600,
                    Value = Math.Max(0, Math.Min(3600, appearance.ShowForegroundSeconds)),
                    BackColor = Input, ForeColor = ForeColor, Bounds = new Rectangle(560, 366, 80, 25),
                    AccessibleName = UiText.Get("Beim Einblenden im Vordergrund (Sek., 0 = aus)",
                        "On reveal, stay on top (sec, 0 = off)") };
                connection.Controls.Add(new Label { Text = showForeground.AccessibleName,
                    Bounds = new Rectangle(16, 367, 535, 24), TextAlign = ContentAlignment.MiddleLeft });
                connection.Controls.Add(showForeground);
                showForeground.ValueChanged += delegate { doorEditor.ShowForegroundSeconds = (int)showForeground.Value; };
                reset.Click += delegate { showForeground.Value = new Appearance().ShowForegroundSeconds; };
                connection.Height += 81;
                design.Height += 27;
                previewGroup.Top += 27;
                save.Top += 27;
                cancel.Top += 27;
                reset.Top += 27;
                versionLabel.Top += 27;
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + 27);
            }
            if (doorEditor.DeviceRows || doorEditor.Sensors) { showOpen.Hide(); sample.Hide(); connection.Controls[connection.Controls.IndexOf(sample) - 1].Hide(); }
            var vertical = new CheckBox { Text = UiText.Get("Untereinander", "Stack vertically"), AutoSize = true,
                Location = new Point(16, 284), Checked = appearance.Vertical };
            if (doorEditor.Switches || doorEditor.Sensors) connection.Controls.Add(vertical);
            vertical.CheckedChanged += delegate { preview.Invalidate(); };
            var beside = new CheckBox { Text = UiText.Get("Wert neben Bezeichnung", "Value beside label"), AutoSize = true,
                Location = new Point(150, 284), Checked = appearance.ValueBesideName };
            if (doorEditor.Sensors) connection.Controls.Add(beside);
            beside.CheckedChanged += delegate { preview.Invalidate(); };
            reset.Click += delegate { beside.Checked = false; };
            reset.Click += delegate { vertical.Checked = false; };
            AddCopyMenu(connection, new Point(420, 278),
                doorEditor.Sensors ? DoorWidget.SensorTitle : doorEditor.Cover ? HeatingWidget.CoverTitle : doorEditor.Heating ? HeatingWidget.Title : doorEditor.Switches ? DoorWidget.SwitchTitle : DoorWidget.DoorTitle,
                solarStyle, source =>
                {
                    ApplyStyle(source);
                    sample.BackColor = Color.FromArgb(source.OpenIconColor);
                    vertical.Checked = source.Vertical;
                    beside.Checked = source.ValueBesideName;
                    if (onlyWhenOpen != null)
                    {
                        var revealDuration = connection.Controls.OfType<NumericUpDown>().FirstOrDefault();
                        if (revealDuration != null)
                            revealDuration.Value = Math.Max(revealDuration.Minimum,
                                Math.Min(revealDuration.Maximum, source.ShowForegroundSeconds));
                    }
                });
            reset.Click += delegate { sample.BackColor = Color.FromArgb(new Appearance().OpenIconColor); };
            preview.Tile = scale =>
            {
                var style = ReadAppearance();
                style.OpenIconColor = sample.BackColor.ToArgb();
                style.Vertical = vertical.Checked;
                style.ValueBesideName = doorEditor.Sensors && beside.Checked;
                var rows = doorEditor.ReadRows();
                if (doorEditor.Sensors)
                {
                    if (rows.Length == 0) rows = new[] { new Door { Name = UiText.Get("Außentemperatur", "Outdoor temperature"), IconKind = "Temperatur" } };
                    var examples = rows.Select(row => DoorWidget.SensorValue(new SolarTile.Sensor {
                        state = "21.357", attributes = new SolarTile.SensorAttributes { unit_of_measurement = row.IconKind == "Temperatur" ? "°C" : row.IconKind == "Luftfeuchtigkeit" ? "%" : "" }
                    }, row.Decimals, row.Unit)).ToArray();
                    return DoorWidget.PaintSensors(style, rows, rows.Select(row => row.Name).ToArray(), examples, scale, 0);
                }
                if (doorEditor.DeviceRows) return HeatingWidget.Preview(style, rows, scale, doorEditor.Cover);
                if (rows.Length == 0) rows = new[] { doorEditor.Switches
                    ? new Door { Name = UiText.Get("Lampe", "Light"), Open = "Ein", Closed = "Aus", IconKind = "Schalter" }
                    : new Door { Name = UiText.Get("Haustür", "Front door"), Open = "Offen", Closed = "Geschlossen" } };
                return DoorWidget.PaintDoors(style, rows.Select(door => door.Name).ToArray(),
                    rows.Select(door => UiText.Status(showOpen.Checked ? door.Open : door.Closed)).ToArray(),
                    Enumerable.Repeat(showOpen.Checked, rows.Length).ToArray(), scale, 0, rows);
            };
            doorEditor.RowsChanged += delegate { preview.Invalidate(); };
            sample.BackColorChanged += delegate { preview.Invalidate(); };
            showOpen.CheckedChanged += delegate { preview.Invalidate(); };
            save.Click += delegate
            {
                if (!doorEditor.SaveRows()) return;
                if (onlyWhenOpen != null) doorEditor.OnlyWhenOpen = onlyWhenOpen.Checked;
                if (onlyOpenContacts != null) doorEditor.OnlyOpenContacts = onlyOpenContacts.Checked;
                SelectedAppearance = ReadAppearance();
                SelectedAppearance.OpenIconColor = sample.BackColor.ToArgb();
                SelectedAppearance.Vertical = vertical.Checked;
                SelectedAppearance.ValueBesideName = doorEditor.Sensors && beside.Checked;
                if (onlyWhenOpen != null) SelectedAppearance.ShowForegroundSeconds = doorEditor.ShowForegroundSeconds;
                DialogResult = DialogResult.OK;
            };
        }
        design.Height = Math.Max(design.Height + 210, doorStyle == null ? 591 : 557);
        previewGroup.Top += 210;
        save.Top += 210;
        cancel.Top += 210;
        reset.Top += 210;
        versionLabel.Top += 210;
        ClientSize = new Size(ClientSize.Width, ClientSize.Height + 210);
        fixedValueWidth.CheckedChanged += delegate { preview.Invalidate(); };
        if (connectionOnly)
        {
            Text = UiText.Get("Home Assistant – Verbindung", "Home Assistant connection");
            design.Hide();
            previewGroup.Hide();
            reset.Hide();
            versionLabel.Hide();
            foreach (Control control in connection.Controls)
                if (control.Top > 100) control.Hide();
            connection.Bounds = new Rectangle(12, 10, 447, 115);
            ClientSize = new Size(471, 176);
            cancel.Location = new Point(251, 135);
            save.Location = new Point(359, 135);
        }
        AcceptButton = save;
        CancelButton = cancel;
    }

    void ApplyStyle(Appearance source)
    {
        background.BackColor = Color.FromArgb(source.Background);
        textColor.BackColor = Color.FromArgb(source.Text);
        iconColor.BackColor = Color.FromArgb(source.Icons);
        opacity.Value = source.BackgroundOpacity;
        textSize.Value = source.TextSize;
        typeface.SelectedItem = source.Typeface;
        if (typeface.SelectedIndex < 0) typeface.SelectedItem = "Segoe UI";
        iconSize.Value = source.IconSize;
        spacing.Value = source.MetricSpacing;
        padding.Value = source.FramePadding;
        SelectHorizontalAnchor(source.HorizontalAnchor);
        foregroundMinutes.Value = Math.Max(foregroundMinutes.Minimum, Math.Min(foregroundMinutes.Maximum, source.ForegroundMinutes));
        foregroundBlinks.Value = Math.Max(foregroundBlinks.Minimum, Math.Min(foregroundBlinks.Maximum, source.ForegroundBlinks));
        blinkCycleSeconds.Value = Math.Max(1, Math.Min(30, source.BlinkCycleSeconds));
        foregroundWhileBlinking.Checked = source.ForegroundWhileBlinking;
        backgroundClick.SelectedIndex = source.BackgroundOnAnyClick ? 1 : 0;
        fixedValueWidth.Checked = source.FixedValueWidth;
        fieldWidths = source.FieldWidths == null ? null : (int[])source.FieldWidths.Clone();
    }

    void SelectHorizontalAnchor(HorizontalAnchor anchor)
    {
        int index = (int)anchor;
        horizontalAnchor.SelectedIndex = index >= 0 && index < horizontalAnchor.Items.Count ? index : 0;
    }

    void AddPickerArrow(ComboBox picker, string caption)
    {
        var arrow = DarkArrow("", caption);
        arrow.Bounds = new Rectangle(picker.Right - 27, picker.Top + 1, 26, 23);
        arrow.Paint += delegate(object sender, PaintEventArgs args)
        {
            int x = arrow.Width / 2, y = arrow.Height / 2;
            using (var pen = new Pen(Color.Silver))
            {
                args.Graphics.DrawLine(pen, 0, 2, 0, arrow.Height - 3);
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                args.Graphics.DrawLines(pen, new[] { new Point(x - 4, y - 2),
                    new Point(x, y + 2), new Point(x + 4, y - 2) });
            }
        };
        arrow.Click += delegate { picker.Focus(); picker.DroppedDown = true; };
        design.Controls.Add(arrow);
        arrow.BringToFront();
    }

    void AddCopyMenu(Control parent, Point location, string current, Appearance solar, Action<Appearance> apply)
    {
        var button = new SettingsButton { Text = UiText.Get("Ansicht von Widget übernehmen", "Copy appearance from widget"), AutoSize = true, Location = location };
        StyleButton(button);
        parent.Controls.Add(button);
        var menu = new WidgetMenu();
        button.Disposed += delegate { menu.Dispose(); };
        foreach (string name in new[] { "Solar", DoorWidget.DoorTitle, DoorWidget.SwitchTitle, HeatingWidget.Title, HeatingWidget.CoverTitle })
        {
            if (name == current) continue;
            string source = name;
            menu.Items.Add(source, null, delegate
            {
                Appearance saved = source == "Solar" ? solar : (source == HeatingWidget.Title || source == HeatingWidget.CoverTitle)
                    ? HeatingWidget.SavedStyle(source == HeatingWidget.CoverTitle) : DoorWidget.SavedStyle(source == DoorWidget.SwitchTitle);
                apply(saved ?? (source == HeatingWidget.Title
                    ? new Appearance { IconSize = 28, MetricSpacing = 0, FramePadding = 10 }
                    : new Appearance()));
            });
        }
        foreach (var widget in DoorWidget.SavedWidgets())
        {
            var id = widget.Key;
            menu.Items.Add(UiText.Get("Sensoren · ", "Sensors · ") + widget.Value, null, delegate
            {
                var saved = DoorWidget.SavedInstanceStyle(id);
                if (saved != null) apply(saved);
            });
        }
        foreach (var widget in DoorWidget.SavedWidgets(true))
        {
            var id = widget.Key;
            menu.Items.Add(UiText.Get("Schalter · ", "Switches · ") + widget.Value, null, delegate
            {
                var saved = DoorWidget.SavedInstanceStyle(id, true);
                if (saved != null) apply(saved);
            });
        }
        button.Click += delegate { menu.Show(button, 0, button.Height); };
    }

    TextBox AddField(string caption, string value, int top, string unit = null)
    {
        connection.Controls.Add(new Label { Text = caption, TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(12, top + 2, 184, 21) });
        var field = new TextBox { Text = value, BackColor = Input, ForeColor = ForeColor,
            BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, AccessibleName = caption };
        AddInputFrame(connection, field, new Rectangle(201, top + 2, unit == null ? 233 : 201, 21));
        if (unit != null)
        {
            var browse = DarkArrow("", UiText.Get("Sensor auswählen: ", "Select sensor: ") + caption);
            browse.Paint += delegate(object sender, PaintEventArgs args)
            {
                var graphics = args.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float centerX = browse.ClientSize.Width / 2f;
                float centerY = browse.ClientSize.Height / 2f;
                float radius = Math.Max(3, browse.ClientSize.Height * 0.19f);
                using (var pen = new Pen(browse.Enabled ? Color.WhiteSmoke : Color.Gray, 1.4f))
                {
                    graphics.DrawEllipse(pen, centerX - radius - 1, centerY - radius - 1, 2 * radius, 2 * radius);
                    graphics.DrawLine(pen, centerX + radius * 0.5f, centerY + radius * 0.5f,
                        centerX + radius * 1.35f, centerY + radius * 1.35f);
                }
            };
            browse.Bounds = new Rectangle(406, top + 2, 28, 21);
            browse.FlatAppearance.BorderSize = 1;
            browse.TabStop = true;
            browse.Click += async delegate { await SelectSensor(field, unit, browse); };
            connection.Controls.Add(browse);
        }
        return field;
    }

    Button AddColor(string caption, int color, int y, Control parent = null)
    {
        parent = parent ?? design;
        parent.Controls.Add(new Label { Text = caption, AutoSize = true, Location = new Point(14, y + 4) });
        var sample = new Button { BackColor = Color.FromArgb(color), UseVisualStyleBackColor = false,
            AccessibleName = caption, Bounds = new Rectangle(304, y + 2, 78, 23) };
        StyleButton(sample);
        sample.BackColor = Color.FromArgb(color);
        sample.Click += delegate
        {
            using (var picker = new ColorDialog { FullOpen = true, Color = sample.BackColor })
                if (picker.ShowDialog(this) == DialogResult.OK) sample.BackColor = picker.Color;
        };
        parent.Controls.Add(sample);
        return sample;
    }

    NumericUpDown AddNumber(string caption, int value, int min, int max, int y)
    {
        design.Controls.Add(new Label { Text = caption, AutoSize = true, Location = new Point(14, y + 4) });
        var field = new NumericUpDown { Minimum = min, Maximum = max,
            Value = Math.Max(min, Math.Min(max, value)), AccessibleName = caption,
            BackColor = Input, ForeColor = ForeColor, BorderStyle = BorderStyle.None };
        AddNumberFrame(field, new Rectangle(304, y + 2, 78, 23));
        return field;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    static Button DarkArrow(string text, string caption)
    {
        var arrow = new Button { Text = text, AccessibleName = caption, Margin = Padding.Empty,
            Padding = Padding.Empty, TabStop = false };
        StyleButton(arrow);
        arrow.FlatAppearance.BorderSize = 0;
        return arrow;
    }

    sealed class DarkFontPicker : ComboBox
    {
        internal DarkFontPicker() { DrawMode = DrawMode.OwnerDrawFixed; }

        protected override void OnDrawItem(DrawItemEventArgs args)
        {
            bool selected = (args.State & DrawItemState.Selected) != 0;
            using (var fill = new SolidBrush(selected ? Border : Input))
                args.Graphics.FillRectangle(fill, args.Bounds);
            if (args.Index >= 0)
                TextRenderer.DrawText(args.Graphics, GetItemText(Items[args.Index]), Font, args.Bounds,
                    ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            args.DrawFocusRectangle();
        }
    }

    static Panel CreateInputFrame(Rectangle bounds)
    {
        var frame = new Panel { Bounds = bounds, BackColor = Input, TabStop = false };
        frame.Paint += delegate(object sender, PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var edge = SettingsTheme.Rounded(new RectangleF(0.5f, 0.5f,
                frame.ClientSize.Width - 1, frame.ClientSize.Height - 1), 4))
            using (var pen = new Pen(Border))
                args.Graphics.DrawPath(pen, edge);
        };
        return frame;
    }

    void AddNumberFrame(NumericUpDown field, Rectangle bounds)
    {
        var frame = CreateInputFrame(bounds);
        var minus = DarkArrow("", UiText.Get("Verringern", "Decrease"));
        var plus = DarkArrow("", UiText.Get("Erhöhen", "Increase"));
        minus.Paint += delegate(object sender, PaintEventArgs args) { DrawStepArrow(minus, args.Graphics, false); };
        plus.Paint += delegate(object sender, PaintEventArgs args) { DrawStepArrow(plus, args.Graphics, true); };
        minus.Click += delegate { field.DownButton(); };
        plus.Click += delegate { field.UpButton(); };

        // Clip the native spinner outside the visible text area.
        var editor = new Panel { BackColor = Input, TabStop = false };
        editor.Controls.Add(field);
        frame.Controls.AddRange(new Control[] { editor, minus, plus });
        Action layout = delegate
        {
            int width = frame.ClientSize.Width;
            int height = frame.ClientSize.Height;
            int button = Math.Max(12, (height - 2) * 3 / 4);
            int topHeight = (height - 2) / 2;
            editor.Bounds = new Rectangle(1, 1, width - button - 2, height - 2);
            field.Bounds = new Rectangle(3, Math.Max(0, (editor.Height - field.PreferredHeight) / 2),
                editor.Width + 2 * height, field.PreferredHeight);
            plus.Bounds = new Rectangle(width - button - 1, 1, button, topHeight);
            minus.Bounds = new Rectangle(width - button - 1, 1 + topHeight, button, height - 2 - topHeight);
        };
        frame.Resize += delegate { layout(); };
        layout();
        design.Controls.Add(frame);
    }

    static void DrawStepArrow(Button button, Graphics graphics, bool up)
    {
        int x = button.ClientSize.Width / 2;
        int y = button.ClientSize.Height / 2;
        int half = Math.Max(2, button.ClientSize.Width / 5);
        int direction = up ? -1 : 1;
        using (var border = new Pen(Border))
        using (var ink = new SolidBrush(Color.WhiteSmoke))
        {
            graphics.DrawLine(border, 0, 0, 0, button.Height - 1);
            if (!up) graphics.DrawLine(border, 1, 0, button.Width - 1, 0);
            graphics.FillPolygon(ink, new[] { new Point(x - half, y - direction * 2),
                new Point(x + half, y - direction * 2), new Point(x, y + direction * 2) });
        }
    }

    static void AddInputFrame(Control parent, Control input, Rectangle bounds)
    {
        var frame = CreateInputFrame(bounds);
        var editor = new Panel { Dock = DockStyle.Fill, BackColor = Input, TabStop = false };
        frame.Padding = new Padding(1);
        editor.Controls.Add(input);
        Action layout = delegate
        {
            // The native single-line edit adds an internal top inset.
            int textInset = Math.Max(1, (int)Math.Round(input.Font.Height / 7.0));
            input.Bounds = new Rectangle(4, (editor.Height - input.PreferredSize.Height) / 2 - textInset,
                Math.Max(1, editor.Width - 8), input.PreferredSize.Height);
        };
        editor.Resize += delegate { layout(); };
        frame.Controls.Add(editor);
        layout();
        editor.Click += delegate { input.Focus(); };
        frame.Click += delegate { input.Focus(); };
        parent.Controls.Add(frame);
    }

    static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = Input;
        button.ForeColor = Color.WhiteSmoke;
    }

    Appearance ReadAppearance()
    {
        return new Appearance
        {
            Background = background.BackColor.ToArgb(),
            Text = textColor.BackColor.ToArgb(),
            Icons = iconColor.BackColor.ToArgb(),
            BackgroundOpacity = (int)opacity.Value,
            TextSize = (int)textSize.Value,
            Typeface = typeface.SelectedItem as string ?? "Segoe UI",
            IconSize = (int)iconSize.Value,
            MetricSpacing = (int)spacing.Value,
            FramePadding = (int)padding.Value,
            HorizontalAnchor = (HorizontalAnchor)horizontalAnchor.SelectedIndex,
            ForegroundMinutes = (int)foregroundMinutes.Value,
            ForegroundBlinks = (int)foregroundBlinks.Value,
            BlinkCycleSeconds = (int)blinkCycleSeconds.Value,
            ForegroundWhileBlinking = foregroundWhileBlinking.Checked,
            BackgroundOnAnyClick = backgroundClick.SelectedIndex == 1,
            FixedValueWidth = fixedValueWidth.Checked,
            FieldWidths = fieldWidths == null ? null : (int[])fieldWidths.Clone(),
            ReportFields = reportFields,
            ShowForegroundSeconds = previous != null && previous.Appearance != null ? previous.Appearance.ShowForegroundSeconds : 0
        };
    }

    Uri ReadAddress()
    {
        Uri uri;
        if (!Uri.TryCreate(address.Text.Trim(), UriKind.Absolute, out uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException(UiText.Get("Bitte eine gültige HTTP- oder HTTPS-Adresse eingeben.", "Enter a valid HTTP or HTTPS address."));
        return uri;
    }

    bool CanReuseToken(Uri uri)
    {
        return previous != null && new Uri(previous.Url).GetLeftPart(UriPartial.Authority)
            == uri.GetLeftPart(UriPartial.Authority);
    }

    async Task SelectSensor(TextBox target, string unit, Button browse)
    {
        browse.Enabled = false;
        browse.Invalidate();
        try
        {
            Uri uri = ReadAddress();
            string accessToken = token.Text.Trim();
            if (accessToken.Length == 0 && CanReuseToken(uri)) accessToken = previous.ReadToken();
            if (accessToken.Length == 0) throw new ArgumentException(UiText.Get("Bitte zuerst einen Zugriffstoken eingeben.", "Enter an access token first."));

            var sensors = await LoadSensors(uri, accessToken);
            if (IsDisposed) return;
            using (var picker = new SensorPicker(sensors, unit, target.Text))
                if (picker.ShowDialog(this) == DialogResult.OK) target.Text = picker.SelectedEntity;
        }
        catch (ArgumentException ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, Text); }
        catch (Exception) { if (!IsDisposed) MessageBox.Show(this, UiText.Get("Sensorliste konnte nicht geladen werden. Bitte Verbindung und Zugriffstoken prüfen.", "Could not load sensors. Check the connection and access token."), Text); }
        finally
        {
            if (!browse.IsDisposed) { browse.Enabled = true; browse.Invalidate(); }
        }
    }

    internal static async Task<SolarTile.Sensor[]> LoadSensors(Uri uri, string accessToken)
    {
        using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using (var response = await client.GetAsync(uri.AbsoluteUri.TrimEnd('/') + "/api/states"))
            {
                if (!response.IsSuccessStatusCode)
                    throw new ArgumentException(UiText.Get("Home Assistant meldet HTTP ", "Home Assistant returned HTTP ") + (int)response.StatusCode + ".");
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                return serializer.Deserialize<SolarTile.Sensor[]>(await response.Content.ReadAsStringAsync());
            }
        }
    }

    internal sealed class SensorPicker : Form
    {
        readonly ListBox list;
        internal SolarTile.Sensor Selection { get { return (SolarTile.Sensor)list.SelectedItem; } }
        internal string SelectedEntity { get { return Selection.entity_id; } }

        internal SensorPicker(SolarTile.Sensor[] sensors, string unit, string current)
        {
            Text = UiText.Get("Sensor auswählen", "Select sensor");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(660, 410);
            BackColor = Surface;
            ForeColor = Color.WhiteSmoke;
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));

            var matches = (sensors ?? new SolarTile.Sensor[0]).Where(sensor => sensor != null &&
                sensor.entity_id != null && sensor.attributes != null && (unit == "any_sensor" ? DoorWidget.IsSensor(sensor.entity_id) : unit == "window_cover"
                    ? HeatingWidget.IsCover(sensor.entity_id) && !new[] { "garage", "door", "gate" }.Contains(sensor.attributes.device_class)
                    : unit == "climate" ? HeatingWidget.IsClimate(sensor.entity_id) : unit == "switch" ? DoorWidget.IsSwitchEntity(sensor.entity_id) : unit == null
                    ? (sensor.entity_id.StartsWith("binary_sensor.", StringComparison.Ordinal) &&
                        new[] { "door", "garage_door", "opening", "window" }.Contains(sensor.attributes.device_class)) ||
                        sensor.entity_id.StartsWith("cover.", StringComparison.Ordinal)
                    : sensor.entity_id.StartsWith("sensor.", StringComparison.Ordinal) && (unit == "%"
                        ? sensor.attributes.unit_of_measurement == "%"
                        : new[] { "W", "kW", "MW" }.Contains(sensor.attributes.unit_of_measurement))))
                .OrderBy(sensor => sensor.Display).ToArray();
            var label = new Label { Text = unit == "%" ? UiText.Get("Sensoren mit % (Akkustand)", "Battery sensors in %") : UiText.Get("Leistungssensoren mit W, kW oder MW", "Power sensors in W, kW or MW"),
                AutoSize = true, Location = new Point(12, 12) };
            var search = new TextBox { Bounds = new Rectangle(12, 38, 636, 24), BackColor = Input,
                ForeColor = ForeColor, AccessibleName = UiText.Get("Sensoren suchen", "Search sensors") };
            list = new ListBox { Bounds = new Rectangle(12, 70, 636, 288), BackColor = Input,
                ForeColor = ForeColor, DisplayMember = "Display", HorizontalScrollbar = true };
            var accept = new SettingsButton { Primary = true, Text = UiText.Get("Übernehmen", "Apply"), Bounds = new Rectangle(538, 369, 110, 28) };
            var cancel = new SettingsButton { Text = UiText.Get("Abbrechen", "Cancel"), Bounds = new Rectangle(420, 369, 110, 28), DialogResult = DialogResult.Cancel };
            StyleButton(accept);
            StyleButton(cancel);
            accept.Click += delegate { if (list.SelectedItem != null) DialogResult = DialogResult.OK; };
            list.DoubleClick += delegate { if (list.SelectedItem != null) DialogResult = DialogResult.OK; };
            list.SelectedIndexChanged += delegate { accept.Enabled = list.SelectedIndex >= 0; };
            Action filter = delegate
            {
                list.BeginUpdate();
                list.Items.Clear();
                list.Items.AddRange(matches.Where(sensor =>
                    sensor.Display.IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0).ToArray());
                for (int i = 0; i < list.Items.Count; i++)
                    if (((SolarTile.Sensor)list.Items[i]).entity_id == current) { list.SelectedIndex = i; break; }
                accept.Enabled = list.SelectedIndex >= 0;
                list.EndUpdate();
                label.Text = list.Items.Count == 0 ? UiText.Get("Keine passenden Sensoren gefunden.", "No matching sensors found.") : list.Items.Count + UiText.Get(" passende Sensoren", " matching sensors");
            };
            search.TextChanged += delegate { filter(); };
            Controls.AddRange(new Control[] { label, search, list, cancel, accept });
            AcceptButton = accept;
            CancelButton = cancel;
            filter();
        }
    }

    void Save(object sender, EventArgs args)
    {
        try
        {
            Uri uri = ReadAddress();
            var next = new Settings { Url = uri.AbsoluteUri, Entities = new string[4] };
            for (int i = 0; i < entities.Length; i++)
            {
                string entity = entities[i].Text.Trim().ToLowerInvariant();
                if (entity.Length != 0 && !Regex.IsMatch(entity, @"^[a-z0-9_]+\.[a-z0-9_]+$"))
                    throw new ArgumentException(UiText.Get("Bitte gültige Entitäts-IDs eingeben oder ungenutzte Felder leer lassen.", "Enter valid entity IDs or leave unused fields blank."));
                next.Entities[i] = entity;
            }

            next.GridExportEntity = gridExport.Text.Trim().ToLowerInvariant();
            if (next.GridExportEntity.Length != 0 &&
                !Regex.IsMatch(next.GridExportEntity, @"^[a-z0-9_]+\.[a-z0-9_]+$"))
                throw new ArgumentException(UiText.Get("Bitte eine gültige Entitäts-ID für die Netzeinspeisung eingeben.", "Enter a valid entity ID for grid export."));

            if (token.Text.Trim().Length != 0) next.Token = Settings.Protect(token.Text.Trim());
            else if (CanReuseToken(uri))
                next.Token = previous.Token;
            else throw new ArgumentException(UiText.Get("Bitte einen Zugriffstoken für diese Adresse eingeben.", "Enter an access token for this address."));

            next.Appearance = ReadAppearance();
            next.Save();
            Result = next;
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, Text); }
        catch (Exception) { MessageBox.Show(this, UiText.Get("Die Einstellungen konnten nicht gespeichert werden.", "Could not save settings."), Text); }
    }
}

sealed class SolarTile : Form
{
    static readonly string[] Captions = { "Solar", UiText.Get("Akku", "Battery"), UiText.Get("Haus", "Home"), UiText.Get("Netz", "Grid") };
    readonly string[] values = { "—", "—", "—", "—" };
    readonly string[] metricHints = new string[4];
    Appearance appearance = new Appearance();
    int hovered = -1;
    readonly float[] metricStarts = new float[4];
    readonly ToolTip hints = new ToolTip();
    readonly Timer timer = new Timer { Interval = 15000 };
    readonly HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };
    readonly ContextMenuStrip menu = new WidgetMenu();
    Settings settings;
    bool updating;
    Control dragSource;
    Point pointerStart;
    Point windowStart;
    readonly bool demo;
    readonly NotifyIcon tray;
    DoorWidget doors;
    DoorWidget switches;
    HeatingWidget heating;
    HeatingWidget covers;
    readonly System.Collections.Generic.Dictionary<string, DoorWidget> widgetInstances = new System.Collections.Generic.Dictionary<string, DoorWidget>();
    ToolStripMenuItem sensorMenu, switchMenu;
    HomeAssistantEvents liveEvents;
    readonly object liveLock = new object();
    readonly System.Collections.Generic.HashSet<string> liveChanges = new System.Collections.Generic.HashSet<string>();
    readonly Timer liveTimer = new Timer { Interval = 120 };
    bool exiting;
    readonly ContextMenuStrip trayMenu = new WidgetMenu();
    static readonly string WidgetsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ReleaseVersion.DataFolder, "widgets.txt");
    WidgetPlacement placement;
    static readonly string PositionFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ReleaseVersion.DataFolder, "position.txt");

    internal SolarTile(bool demo)
    {
        this.demo = demo;
        Icon = WidgetIcon.Load(solar: true);
        Text = ReleaseVersion.SolarName;
        Font = new Font("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        ClientSize = new Size(460, 66);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ConnectDrag(this);
        if (demo)
        {
            double[] examples = { 7720, 100, 520, -7200 };
            for (int i = 0; i < values.Length; i++) values[i] = DisplayReading(i, examples[i]);
        }
        if (!demo)
        {
            menu.Items.Add(UiText.Get("Einstellungen", "Settings"), null, delegate { Configure(); });
            menu.Items.Add(UiText.Get("Aktualisieren", "Refresh"), null, async delegate { await RefreshValues(); });
            WidgetPlacement.AddLockMenuItem(menu, this, () => placement);
            try { settings = Settings.Load(); }
            catch (Exception) { ShowEmpty(UiText.Get("Einstellungen konnten nicht gelesen werden.", "Could not read settings.")); }
            Shown += async delegate
            {
                ApplyAppearance();
                placement = new WidgetPlacement(this, "solar", LegacyPosition(),
                    horizontalAnchor: () => appearance.HorizontalAnchor);
                new WidgetAttention(this, () => appearance, RenderTile);
                if (settings != null) await RefreshValues();
            };
            timer.Tick += async delegate { await RefreshValues(); };
        }
        var pin = new ToolStripMenuItem(UiText.Get("Immer im Vordergrund", "Always on top")) { CheckOnClick = true };
        pin.CheckedChanged += delegate { TopMost = pin.Checked; if (doors != null) doors.TopMost = TopMost; if (switches != null) switches.TopMost = TopMost; if (heating != null) heating.TopMost = TopMost; if (covers != null) covers.TopMost = TopMost; foreach (var widget in widgetInstances.Values) widget.TopMost = TopMost; };
        menu.Items.Add(pin);
        menu.Items.Add(UiText.Get("Schließen", "Close"), null, delegate
        {
            if (demo) Close();
            else { Hide(); RememberWidgets(); }
        });
        VisibleChanged += delegate
        {
            if (demo) return;
            if (Visible) timer.Start();
            else timer.Stop();
        };
        ContextMenuStrip = menu;
        ShowInTaskbar = false;
        if (!demo)
        {
            var addWidget = new ToolStripMenuItem(UiText.Get("Widget hinzufügen", "Add widget"));
            addWidget.DropDownItems.Add("Solar", null, delegate { OpenSolar(); });
            var contactsMenu = new ToolStripMenuItem(DoorWidget.DoorTitle);
            contactsMenu.DropDownItems.Add(UiText.Get("Öffnen", "Open"), null, delegate { ShowDoors(); });
            contactsMenu.DropDownItems.Add(UiText.Get("Einstellungen…", "Settings…"), null, delegate { ShowDoors(editContacts: true); });
            var closeContacts = contactsMenu.DropDownItems.Add(UiText.Get("Schließen", "Close"), null,
                delegate { if (doors != null) doors.DisableContacts(); });
            contactsMenu.DropDownOpening += delegate { closeContacts.Enabled = doors != null && doors.ContactsEnabled; };
            addWidget.DropDownItems.Add(contactsMenu);
            switchMenu = new ToolStripMenuItem(DoorWidget.SwitchTitle);
            addWidget.DropDownItems.Add(switchMenu);
            switchMenu.DropDownOpening += delegate { PopulateInstanceMenu(switchMenu, true); };
            PopulateInstanceMenu(switchMenu, true);
            addWidget.DropDownItems.Add(HeatingWidget.Title, null, delegate { ShowHeating(); });
            addWidget.DropDownItems.Add(HeatingWidget.CoverTitle, null, delegate { ShowHeating(cover: true); });
            sensorMenu = new ToolStripMenuItem(DoorWidget.SensorTitle);
            addWidget.DropDownItems.Add(sensorMenu);
            sensorMenu.DropDownOpening += delegate { PopulateInstanceMenu(sensorMenu, false); };
            PopulateInstanceMenu(sensorMenu, false);
            trayMenu.Items.Add(addWidget);
            trayMenu.Items.Add(UiText.Get("Einstellungen", "Settings"), null, delegate { OpenProgramOptions(); });
            trayMenu.Items.Add(UiText.Get("Programm beenden", "Exit application"), null, delegate { ExitWidgets(); });
            tray = new NotifyIcon { Icon = WidgetIcon.Load(), Text = ReleaseVersion.ProductName, ContextMenuStrip = trayMenu };
            tray.DoubleClick += delegate { OpenProgramOptions(); };
        }
        if (demo) Shown += delegate { ApplyAppearance(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00080000; // WS_EX_LAYERED
            return parameters;
        }
    }

    void StartWidgets()
    {
        tray.Visible = true;
        liveTimer.Tick += async delegate { await RefreshLive(); };
        liveTimer.Start();
        StartLive();
        if (settings == null)
        {
#if STORE
            BeginInvoke(new Action(delegate { Configure(connectionOnly: true); }));
#endif
            return;
        }
        string[] active = new string[0];
        try { if (File.Exists(WidgetsFile)) active = File.ReadAllLines(WidgetsFile); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (active.Contains("solar")) Show();
        if (active.Contains("doors")) ShowDoors(remember: false);
        if (active.Contains("switches")) ShowDoors(remember: false, switchWidget: true);
        if (active.Contains("heating")) ShowHeating(false);
        if (active.Contains("covers")) ShowHeating(false, cover: true);
        foreach (var id in active.Where(line => line.StartsWith("sensor:", StringComparison.Ordinal)).Select(line => line.Substring(7)).Distinct())
            if (DoorWidget.IsSensorId(id) && DoorWidget.SavedWidgets().ContainsKey(id)) OpenInstance(id, false, false);
        foreach (var id in active.Where(line => line.StartsWith("switch:", StringComparison.Ordinal)).Select(line => line.Substring(7)).Distinct())
            if (DoorWidget.IsSensorId(id) && DoorWidget.SavedWidgets(true).ContainsKey(id)) OpenInstance(id, true, false);
    }

    void StartLive()
    {
        lock (liveLock)
        {
            if (liveEvents != null) liveEvents.Dispose();
            liveEvents = null;
            liveChanges.Clear();
            var current = settings;
            if (current == null) return;
            liveEvents = new HomeAssistantEvents(current, entity =>
            {
                lock (liveLock)
                    if (settings == current) liveChanges.Add(entity);
            });
        }
    }

    async Task RefreshLive()
    {
        string[] changes;
        lock (liveLock)
        {
            if (liveChanges.Count == 0) return;
            changes = liveChanges.ToArray();
            liveChanges.Clear();
        }

        liveTimer.Stop();
        try
        {
            bool all = changes.Contains(null);
            var tasks = new System.Collections.Generic.List<Task>();
            if (Visible && settings != null && (all || changes.Any(entity =>
                settings.Entities.Contains(entity) || entity == settings.GridExportEntity)))
                tasks.Add(RefreshValues(true));
            if (doors != null) tasks.Add(doors.RefreshLive(changes));
            if (switches != null) tasks.Add(switches.RefreshLive(changes));
            if (heating != null) tasks.Add(heating.RefreshLive(changes));
            if (covers != null) tasks.Add(covers.RefreshLive(changes));
            foreach (var widget in widgetInstances.Values) tasks.Add(widget.RefreshLive(changes));
            await Task.WhenAll(tasks);
        }
        finally { if (!exiting && !IsDisposed) liveTimer.Start(); }
    }

    void RememberWidgets()
    {
        if (exiting) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WidgetsFile));
            string temp = WidgetsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temp, new[] { Visible ? "solar" : "", doors != null && doors.ContactsEnabled ? "doors" : "", switches != null && switches.Visible ? "switches" : "", heating != null && heating.Visible ? "heating" : "", covers != null && covers.Visible ? "covers" : "" }
                    .Concat(widgetInstances.Where(pair => pair.Value.Visible).Select(pair => pair.Key)));
                if (File.Exists(WidgetsFile)) File.Replace(temp, WidgetsFile, null);
                else File.Move(temp, WidgetsFile);
            }
            finally { File.Delete(temp); }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void PopulateInstanceMenu(ToolStripMenuItem menu, bool switchWidget)
    {
        foreach (ToolStripItem item in menu.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
        menu.DropDownItems.Add(switchWidget ? UiText.Get("Neues Schalter-Widget…", "New switch widget…") : UiText.Get("Neue Sensor-Kachel…", "New sensor widget…"), null,
            delegate { OpenInstance(Guid.NewGuid().ToString("N"), switchWidget); });
        if (switchWidget && (switches != null || File.Exists(DoorWidget.ConfigPath(true))))
            menu.DropDownItems.Add(UiText.Get("Schalter (bisheriges Widget)", "Switches (original widget)"), null, delegate { ShowDoors(switchWidget: true); });
        foreach (var widget in DoorWidget.SavedWidgets(switchWidget).OrderBy(pair => pair.Value))
        {
            string id = widget.Key;
            menu.DropDownItems.Add(widget.Value, null, delegate { OpenInstance(id, switchWidget); });
        }
    }

    void OpenInstance(string id, bool switchWidget, bool remember = true)
    {
        if (settings == null && !Configure(connectionOnly: true)) return;
        string key = (switchWidget ? "switch:" : "sensor:") + id;
        DoorWidget widget;
        if (!widgetInstances.TryGetValue(key, out widget))
        {
            widget = new DoorWidget(() => settings, TopMost, switches: switchWidget, instanceId: id);
            widgetInstances.Add(key, widget);
            widget.FormClosed += delegate { widgetInstances.Remove(key); RememberWidgets(); };
            widget.VisibleChanged += delegate { if (!widget.Visible) RememberWidgets(); };
        }
        widget.Show();
        if (widget.IsDisposed) return;
        widget.Activate();
        if (remember) RememberWidgets();
    }

    void OpenSolar()
    {
        if (settings == null && !Configure(connectionOnly: true)) return;
        bool reopening = IsHandleCreated;
        Show();
        Activate();
        if (reopening) RefreshVisibleSolar();
        RememberWidgets();
    }

    async void RefreshVisibleSolar() { await RefreshValues(); }

    void ShowDoors(bool remember = true, bool switchWidget = false, bool editContacts = false)
    {
        if (settings == null && !Configure(connectionOnly: true)) return;
        var existing = switchWidget ? switches : doors;
        if (existing != null)
        {
            if (switchWidget) { existing.Show(); existing.Activate(); }
            else existing.OpenContacts(editContacts);
            if (remember) RememberWidgets();
            return;
        }
        var widget = new DoorWidget(() => settings, TopMost, switchWidget);
        if (switchWidget) switches = widget;
        else doors = widget;
        widget.FormClosed += delegate
        {
            if (switchWidget) switches = null;
            else doors = null;
            RememberWidgets();
        };
        if (switchWidget)
        {
            widget.Show();
            widget.VisibleChanged += delegate { if (!widget.Visible) RememberWidgets(); };
        }
        else
        {
            widget.ContactsDisabled += RememberWidgets;
            widget.OpenContacts(editContacts);
        }
        if (remember) RememberWidgets();
    }

    void ShowHeating(bool remember = true, bool cover = false)
    {
        if (settings == null && !Configure(connectionOnly: true)) return;
        var widget = cover ? covers : heating;
        if (widget == null)
        {
            widget = new HeatingWidget(() => settings, cover: cover) { TopMost = TopMost };
            if (cover) covers = widget;
            else heating = widget;
            widget.FormClosed += delegate
            {
                if (cover) covers = null;
                else heating = null;
                RememberWidgets();
            };
            widget.VisibleChanged += delegate { if (!widget.Visible) RememberWidgets(); };
        }
        widget.Show();
        widget.Activate();
        if (remember) RememberWidgets();
    }

    void ExitWidgets()
    {
        exiting = true;
        if (doors != null) doors.Close();
        if (switches != null) switches.Close();
        if (heating != null) heating.Close();
        if (covers != null) covers.Close();
        foreach (var widget in widgetInstances.Values.ToArray()) widget.Close();
        Close();
        Application.ExitThread();
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        exiting = true;
        if (doors != null) doors.Close();
        if (switches != null) switches.Close();
        if (heating != null) heating.Close();
        if (covers != null) covers.Close();
        foreach (var widget in widgetInstances.Values.ToArray()) widget.Close();
        if (!demo) Application.ExitThread();
        base.OnFormClosing(args);
    }

    void ApplyAppearance()
    {
        appearance = settings == null ? new Appearance() : settings.Appearance ?? new Appearance();
        RenderTile();
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        int metric = 0;
        while (metric + 1 < metricStarts.Length && args.X >= metricStarts[metric + 1]) metric++;
        if (metric == hovered) return;
        hovered = metric;
        hints.SetToolTip(this, metricHints[metric]);
    }

    protected override void OnMouseLeave(EventArgs args)
    {
        base.OnMouseLeave(args);
        hovered = -1;
        hints.SetToolTip(this, null);
    }

    void RenderTile()
    {
        if (!IsHandleCreated || IsDisposed) return;
        float scale = LayeredTile.ScaleFor(this);
        // One alpha level keeps the transparent desktop tile reachable by the mouse.
        using (var bitmap = PaintTile(appearance, values, scale, 1, metricStarts))
        {
            if (placement != null) placement.Resize(bitmap.Size);
            else if (ClientSize != bitmap.Size) ClientSize = bitmap.Size;
            LayeredTile.Present(Handle, bitmap);
        }
        AccessibleName = Text = ReleaseVersion.SolarName + ": " + string.Join(", ", Captions[0] + " " + values[0], Captions[1] + " " + values[1],
            Captions[2] + " " + values[2], Captions[3] + " " + values[3]);
    }

    internal static Font CreateTileFont(string typeface, float size, FontStyle style)
    {
        using (var font = new Font(typeface ?? "Segoe UI", size, style, GraphicsUnit.Point))
        using (var family = font.FontFamily)
        {
            if (family.IsStyleAvailable(style)) return (Font)font.Clone();
            var available = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
            return family.IsStyleAvailable(available)
                ? new Font(family, size, available, GraphicsUnit.Point)
                : new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }
    }

    internal static Bitmap PaintTile(Appearance appearance, string[] values, float scale,
        int minimumAlpha, float[] metricStarts, string[] captions = null, Action<Graphics, int> drawIcon = null,
        bool namesOnly = false, RectangleF[] iconBounds = null, bool valueBesideName = false)
    {
        captions = captions ?? Captions;
        var size = Math.Max(8, Math.Min(28, appearance.TextSize));
        var opacity = Math.Max(0, Math.Min(100, appearance.BackgroundOpacity));
        float icon = Math.Max(20, Math.Min(96, appearance.IconSize)) * scale;
        float gap = Math.Max(0, Math.Min(100, appearance.MetricSpacing)) * scale;
        float inset = Math.Max(0, Math.Min(48, appearance.FramePadding)) * scale;
        float iconTextGap = 4 * scale;
        float right = inset;
        float widest = 0;
        float textHeight;
        var fieldWidths = new float[values.Length];
        using (var measureBitmap = new Bitmap(1, 1))
        using (var measure = Graphics.FromImage(measureBitmap))
        using (var labelFont = CreateTileFont(appearance.Typeface, Math.Max(7, size - 1) * scale, FontStyle.Regular))
        using (var valueFont = CreateTileFont(appearance.Typeface, size * scale, FontStyle.Bold))
        {
            textHeight = namesOnly || valueBesideName ? valueFont.GetHeight(measure) : labelFont.GetHeight(measure) + valueFont.GetHeight(measure) + 2 * scale;
            measure.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            for (int i = 0; i < values.Length; i++)
            {
                metricStarts[i] = right;
                float captionWidth = measure.MeasureString(captions[i], namesOnly ? valueFont : labelFont, int.MaxValue, StringFormat.GenericTypographic).Width;
                float valueWidth = measure.MeasureString(appearance.FixedValueWidth ? "-999,99 kW" : values[i],
                    valueFont, int.MaxValue, StringFormat.GenericTypographic).Width;
                float textWidth = valueBesideName ? captionWidth + 8 * scale + valueWidth
                    : Math.Max(captionWidth, namesOnly ? 0 : valueWidth);
                float itemWidth = icon + iconTextGap + (float)Math.Ceiling(textWidth) + 2 * scale;
                if (appearance.FieldWidths != null && i < appearance.FieldWidths.Length && appearance.FieldWidths[i] > 0)
                    itemWidth = Math.Max(icon + iconTextGap + 12 * scale, appearance.FieldWidths[i] * scale);
                widest = Math.Max(widest, itemWidth);
                fieldWidths[i] = itemWidth;
                right += itemWidth + gap;
            }
        }
        float row = Math.Max(icon, textHeight + 2 * scale);
        int width = (int)Math.Ceiling(appearance.Vertical ? widest + inset * 2 : right - gap + inset);
        int height = (int)Math.Ceiling(inset * 2 + (appearance.Vertical ? row * values.Length + gap * (values.Length - 1) : row));
        if (appearance.ReportFields != null && !appearance.Vertical)
            appearance.ReportFields(metricStarts, fieldWidths, scale);
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            using (var g = Graphics.FromImage(bitmap))
            using (var fill = new SolidBrush(Color.FromArgb(Math.Max(minimumAlpha, opacity * 255 / 100),
                Color.FromArgb(appearance.Background))))
            using (var text = new SolidBrush(Color.FromArgb(appearance.Text)))
            using (var labelFont = CreateTileFont(appearance.Typeface, Math.Max(7, size - 1) * scale, FontStyle.Regular))
            using (var valueFont = CreateTileFont(appearance.Typeface, size * scale, FontStyle.Bold))
            using (var shape = new GraphicsPath())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float d = Math.Min(27 * scale, Math.Max(1, inset * 2));
                shape.AddArc(0, 0, d, d, 180, 90);
                shape.AddArc(width - d, 0, d, d, 270, 90);
                shape.AddArc(width - d, height - d, d, d, 0, 90);
                shape.AddArc(0, height - d, d, d, 90, 90);
                shape.CloseFigure();
                g.FillPath(fill, shape);
                var labelHeight = labelFont.GetHeight(g);
                var valueHeight = valueFont.GetHeight(g);
                float block = namesOnly || valueBesideName ? valueHeight : labelHeight + valueHeight + 2 * scale;
                for (int i = 0; i < values.Length; i++)
                {
                    float left = appearance.Vertical ? inset : metricStarts[i];
                    float center = appearance.Vertical ? inset + i * (row + gap) + row / 2 : height / 2f;
                    float top = center - block / 2;
                    if (iconBounds != null) iconBounds[i] = new RectangleF(left, center - icon / 2, icon, icon);
                    float textLeft = (float)Math.Round(left + icon + iconTextGap);
                    var transform = g.Save();
                    g.TranslateTransform((float)Math.Round(left), (float)Math.Round(center - icon / 2));
                    float iconPixels = (float)Math.Round(icon);
                    g.ScaleTransform(iconPixels / 32f, iconPixels / 32f);
                    g.PixelOffsetMode = PixelOffsetMode.None;
                    if (drawIcon == null) SolarIcon.Draw(g, i, Color.FromArgb(appearance.Icons));
                    else drawIcon(g, i);
                    g.Restore(transform);
                    g.PixelOffsetMode = PixelOffsetMode.None;
                    g.TextRenderingHint = opacity == 100
                        ? System.Drawing.Text.TextRenderingHint.ClearTypeGridFit
                        : opacity == 0
                            ? System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit
                            : System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    var textState = g.Save();
                    float textRight = appearance.Vertical || i + 1 == values.Length ? width - inset :
                        metricStarts[i + 1] - gap;
                    g.SetClip(new RectangleF(textLeft, 0, Math.Max(1, textRight - textLeft), height),
                        CombineMode.Intersect);
                    g.DrawString(captions[i], namesOnly ? valueFont : labelFont, text, textLeft, (float)Math.Round(top), StringFormat.GenericTypographic);
                    if (valueBesideName)
                    {
                        float captionWidth = g.MeasureString(captions[i], labelFont, int.MaxValue, StringFormat.GenericTypographic).Width;
                        g.DrawString(values[i], valueFont, text, (float)Math.Round(textLeft + captionWidth + 8 * scale),
                            (float)Math.Round(top), StringFormat.GenericTypographic);
                    }
                    else if (!namesOnly) g.DrawString(values[i], valueFont, text, textLeft, (float)Math.Round(top + labelHeight + 2 * scale),
                        StringFormat.GenericTypographic);
                    g.Restore(textState);
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                }
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    void ConnectDrag(Control control)
    {
        control.MouseDown += delegate(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left || (placement != null && placement.Locked)) return;
            dragSource = control;
            pointerStart = Cursor.Position;
            windowStart = Location;
            control.Capture = true;
        };
        control.MouseMove += delegate
        {
            if (dragSource != control || !control.Capture || (placement != null && placement.Locked)) return;
            Point point = Cursor.Position;
            var requested = new Point(windowStart.X + point.X - pointerStart.X, windowStart.Y + point.Y - pointerStart.Y);
            Location = placement == null ? requested : placement.DragLocation(requested);
        };
        control.MouseUp += delegate
        {
            if (dragSource != control) return;
            dragSource = null;
            control.Capture = false;
            if (!demo && Location != windowStart) SavePosition();
        };
    }

    Point LegacyPosition()
    {
        try
        {
            if (!File.Exists(PositionFile)) return Location;
            string[] coordinates = File.ReadAllText(PositionFile).Split(',');
            int x, y;
            if (coordinates.Length != 2 ||
                !int.TryParse(coordinates[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ||
                !int.TryParse(coordinates[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return Location;

            return new Point(x, y);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Location;
    }

    void SavePosition()
    {
        try
        {
            if (placement != null) placement.Save();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    ProgramOptions programOptions;

    void OpenProgramOptions()
    {
        if (programOptions != null && !programOptions.IsDisposed)
        {
            programOptions.Activate();
            return;
        }
        programOptions = new ProgramOptions(() => settings, () => Configure(connectionOnly: true), ExitWidgets,
            reveal => { if (doors != null) doors.RevealForSettings(reveal); });
        programOptions.FormClosed += delegate { programOptions = null; };
        programOptions.Show();
    }

    bool Configure(bool connectionOnly = false)
    {
        using (var dialog = new Options(settings, values, connectionOnly: connectionOnly))
        {
            if ((Visible ? dialog.ShowDialog(this) : dialog.ShowDialog()) != DialogResult.OK) return false;
            settings = dialog.Result;
            StartLive();
            if (Visible) ApplyAppearance();
        }
        if (Visible)
        {
            ShowEmpty(UiText.Get("Messwerte werden geladen", "Loading readings"));
            RefreshVisibleSolar();
        }
        return true;
    }

    void ShowEmpty(string hint)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = "—";
            metricHints[i] = hint;
        }
        RenderTile();
    }

    bool solarRefreshQueued;

    async Task RefreshValues(bool live = false)
    {
        if (settings == null || IsDisposed || !Visible) return;
        if (updating) { solarRefreshQueued |= live; return; }
        updating = true;
        Settings current = settings;
        try
        {
            string token;
            try { token = current.ReadToken(); }
            catch (Exception) { ShowEmpty(UiText.Get("Zugriffstoken kann nicht gelesen werden. Bitte neu eingeben.", "Could not read the access token. Enter it again.")); return; }
            var requests = new Task<Tuple<string, string>>[4];
            for (int i = 0; i < requests.Length; i++) requests[i] = ReadMetric(current, i, token);
            var readings = await Task.WhenAll(requests);
            if (IsDisposed || current != settings) return;
            for (int i = 0; i < readings.Length; i++)
            {
                values[i] = readings[i].Item1;
                metricHints[i] = readings[i].Item2;
            }
            RenderTile();
        }
        finally
        {
            updating = false;
            if (!IsDisposed && (current != settings || solarRefreshQueued))
            {
                solarRefreshQueued = false;
                BeginInvoke(new Action(async delegate { await RefreshValues(); }));
            }
        }
    }

    async Task<Tuple<string, string>> ReadMetric(Settings current, int index, string token)
    {
        string entity = current.Entities[index];
        var primary = Read(current, entity, index, token);
        var exported = index == 3 && !string.IsNullOrEmpty(current.GridExportEntity)
            ? Read(current, current.GridExportEntity, index, token) : null;
        var reading = await primary;
        var export = exported == null ? null : await exported;
        if (!reading.Item1.HasValue) return Tuple.Create("—", reading.Item2);
        if (export != null && !export.Item1.HasValue) return Tuple.Create("—", export.Item2);
        double net = NetReading(reading.Item1.Value, export == null ? 0 : export.Item1.Value);
        string hint = export == null ? entity : entity + " − " + current.GridExportEntity;
        return Tuple.Create(DisplayReading(index, net), hint + " · " + DateTime.Now.ToString("HH:mm:ss"));
    }

    internal static double NetReading(double imported, double exported)
    {
        return imported - exported;
    }

    async Task<Tuple<double?, string>> Read(Settings current, string entity, int index, string token)
    {
        if (string.IsNullOrEmpty(entity)) return Tuple.Create<double?, string>(null, UiText.Get("Sensor in den Einstellungen auswählen.", "Select a sensor in Settings."));
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, current.Url.TrimEnd('/') + "/api/states/" + Uri.EscapeDataString(entity)))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await client.SendAsync(request))
                {
                    int code = (int)response.StatusCode;
                    if (code == 401 || code == 403) return Tuple.Create<double?, string>(null, UiText.Get("Home Assistant hat den Zugriff abgelehnt", "Home Assistant denied access"));
                    if (code == 404) return Tuple.Create<double?, string>(null, UiText.Get("Entität nicht gefunden: ", "Entity not found: ") + entity);
                    if (!response.IsSuccessStatusCode) return Tuple.Create<double?, string>(null, "HTTP " + code);
                    var state = new JavaScriptSerializer().Deserialize<Sensor>(await response.Content.ReadAsStringAsync());
                    double? reading = state == null || state.attributes == null
                        ? null : ParseReading(index, state.state, state.attributes.unit_of_measurement);
                    return Tuple.Create(reading, UiText.Get("Sensorwert oder Einheit nicht verfügbar: ", "Sensor value or unit unavailable: ") + entity);
                }
            }
        }
        catch (Exception) { return Tuple.Create<double?, string>(null, UiText.Get("Messwert konnte nicht geladen werden: ", "Could not load sensor reading: ") + entity); }
    }

    static string DisplayReading(int index, double number)
    {
        var culture = CultureInfo.GetCultureInfo(UiText.Get("de-DE", "en-US"));
        if (index == 1) return number.ToString("N0", culture) + " %";
        if (Math.Abs(number) > 1000) return (number / 1000).ToString("0.##", culture) + " kW";
        return number.ToString("N0", culture) + " W";
    }

    internal static string Format(int index, string state, string unit)
    {
        double? reading = ParseReading(index, state, unit);
        return reading.HasValue ? DisplayReading(index, reading.Value) : null;
    }

    static double? ParseReading(int index, string state, string unit)
    {
        double number;
        if (!double.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return null;
        if (index == 1)
        {
            if (unit != "%") return null;
        }
        else
        {
            switch (unit)
            {
                case "W": break;
                case "kW": number *= 1000; break;
                case "MW": number *= 1000000; break;
                default: return null;
            }
        }
        if (double.IsNaN(number) || double.IsInfinity(number)) return null;
        return number;
    }

    protected override void OnFormClosed(FormClosedEventArgs args)
    {
        liveTimer.Dispose();
        if (liveEvents != null) liveEvents.Dispose();
        if (tray != null)
        {
            Icon appIcon = tray.Icon;
            tray.Dispose();
            appIcon.Dispose();
        }
        timer.Dispose();
        client.Dispose();
        hints.Dispose();
        menu.Dispose();
        trayMenu.Dispose();
        base.OnFormClosed(args);
    }

    public sealed class Sensor
    {
        public string state { get; set; }
        public string entity_id { get; set; }
        public string Display
        {
            get { return (attributes.friendly_name ?? entity_id) + "  [" + attributes.unit_of_measurement + "]  " + entity_id; }
        }
        public SensorAttributes attributes { get; set; }
    }

    public sealed class SensorAttributes
    {
        public string unit_of_measurement { get; set; }
        public string friendly_name { get; set; }
        public string device_class { get; set; }
        public int supported_features { get; set; }
        public string[] supported_color_modes { get; set; }
        public int? brightness { get; set; }
        public int[] rgb_color { get; set; }
    }

    [DllImport("user32.dll")]
    static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [STAThread]
    static int Main(string[] args)
    {
        // The Framework compiler targets legacy defaults unless TLS policy is set explicitly.
        AppContext.SetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", false);
        AppContext.SetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", false);
        System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)0;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } // Per-monitor DPI awareness v2
        catch (EntryPointNotFoundException) { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (Array.IndexOf(args, "--self-test") >= 0)
        {
            using (var tile = new SolarTile(true))
            using (var dialog = new Options(null, tile.values))
            {
                if (new JavaScriptSerializer().Deserialize<Appearance>("{}").IconSize != 44) return 1;
                if (WidgetPlacement.KeepOnScreen(new Point(-800, 90), new Size(400, 66),
                    new Rectangle(-1920, 0, 1920, 1080)) != new Point(-800, 90) ||
                    WidgetPlacement.KeepOnScreen(new Point(990, 990), new Size(400, 66),
                    new Rectangle(0, 0, 1000, 1000)) != new Point(600, 934)) return 1;
                var starts = new float[4];
                using (var preview = PaintTile(new Appearance { BackgroundOpacity = 0, MetricSpacing = 0, FixedValueWidth = false }, tile.values, 1, 0, starts))
                    if (preview.GetPixel(preview.Width / 2, 2).A != 0 || preview.Width >= 430) return 1;
                using (var smallReading = PaintTile(new Appearance(), new[] { "1 W", "6 %", "40 W", "0 W" }, 1, 0, new float[4]))
                using (var largeReading = PaintTile(new Appearance(), new[] { "9,99 kW", "100 %", "1.000 W", "-999 W" }, 1, 0, new float[4]))
                    if (smallReading.Size != largeReading.Size) return 1;
                var customFields = new Appearance { FieldWidths = new[] { 80, 60, 90, 70 } };
                var beforeFields = new float[4];
                var afterFields = new float[4];
                using (var before = PaintTile(customFields, tile.values, 1, 0, beforeFields))
                {
                    customFields.FieldWidths[1] += 25;
                    using (var after = PaintTile(customFields, tile.values, 1, 0, afterFields))
                        if (after.Width != before.Width + 25 || afterFields[1] != beforeFields[1] ||
                            afterFields[2] != beforeFields[2] + 25 || after.Height != before.Height) return 1;
                }
                var restoredFields = new JavaScriptSerializer().Deserialize<Appearance>(new JavaScriptSerializer().Serialize(customFields));
                if (!restoredFields.FieldWidths.SequenceEqual(customFields.FieldWidths)) return 1;
                var door = new Door { Open = "Auf", Closed = "Zu" };
                if (door.Describe("on") != "Auf" || door.Describe("closed") != "Zu" ||
                    door.Describe("unavailable") != UiText.Get("Nicht verfügbar", "Unavailable")) return 1;
                using (var doorWidget = new DoorWidget(() => null, false)) { }
                if (!DoorWidget.OpenContactIndices(new[] { "off", "on", "closed", "open", "unknown", "unavailable" })
                    .SequenceEqual(new[] { 1, 3 }) || DoorWidget.OpenContactIndices(null).Length != 0 ||
                    DoorWidget.OpenContactIndices(new[] { "closed", "off" }).Length != 0) return 1;
                if (WidgetPlacement.LeftAfterResize(100, 200, 250, HorizontalAnchor.Left) != 100 ||
                    WidgetPlacement.LeftAfterResize(100, 200, 250, HorizontalAnchor.Right) != 50 ||
                    WidgetPlacement.LeftAfterResize(100, 200, 251, HorizontalAnchor.Center) != 75 ||
                    WidgetPlacement.LeftAfterResize(75, 251, 200, HorizontalAnchor.Center) != 100 ||
                    new JavaScriptSerializer().Deserialize<Appearance>("{}").HorizontalAnchor != HorizontalAnchor.Right) return 1;
                if (DoorWidget.AnyContactOpen(null) || DoorWidget.AnyContactOpen(new[] { "off", "closed", "unknown", "unavailable" }) ||
                    !DoorWidget.AnyContactOpen(new[] { "off", "on" }) || !DoorWidget.AnyContactOpen(new[] { "open" }) ||
                    !DoorWidget.AnyContactOpen(new[] { "opening" }) || !DoorWidget.AnyContactOpen(new[] { "closing" })) return 1;
                var sampleLight = new JavaScriptSerializer().Deserialize<Sensor>(
                    "{\"state\":\"on\",\"attributes\":{\"brightness\":125,\"supported_color_modes\":[\"xy\"],\"rgb_color\":[255,137,0]}}");
                var lightStyle = new Appearance { BackgroundOpacity = 0, Background = Color.Navy.ToArgb() };
                using (var light = new LightControls("Testlampe", lightStyle, () => sampleLight,
                    (service, data) => Task.FromResult(0), Point.Empty))
                {
                    if (light.Controls.OfType<Label>().Any(label => label.Text.Length != 0) ||
                        !light.Controls.Cast<Control>().Any(control => control.AccessibleDescription == UiText.Get("49 Prozent", "49 percent")) ||
                        light.Controls.OfType<Button>().Count() != 8 ||
                        light.BackColor.ToArgb() != lightStyle.Background || light.Opacity != 1) return 1;
                }
                using (var doorOptions = new Options(null, new[] { "Zu" }, new Appearance(), new Appearance(),
                    new DoorWidget.DoorEditor(new[] { new Door { Name = "Testtür", Open = "Auf", Closed = "Zu" } }, () => null))) { }
                string heatPosition = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    var expected = Screen.PrimaryScreen.WorkingArea.Location + new Size(32, 32);
                    using (var thermostat = new HeatingWidget(() => null, heatPosition))
                    {
                        thermostat.Location = expected;
                        thermostat.SavePosition();
                    }
                    using (var thermostat = new HeatingWidget(() => null, heatPosition))
                    {
                        thermostat.RestorePosition();
                        if (thermostat.Location != expected) return 1;
                    }
                    using (var lockTestForm = new Form())
                    using (var menu = new WidgetMenu())
                    {
                        var placement = new WidgetPlacement(lockTestForm, "test-lock", expected, heatPosition);
                        WidgetPlacement.AddLockMenuItem(menu, lockTestForm, () => placement);
                        menu.Items[0].PerformClick();
                        if (!placement.Locked || !new WidgetPlacement(lockTestForm, "test-lock", expected, heatPosition).Locked ||
                            new WidgetPlacement(lockTestForm, "test-other-widget", expected, heatPosition).Locked) return 1;
                        menu.Items[0].PerformClick();
                        if (new WidgetPlacement(lockTestForm, "test-lock", expected, heatPosition).Locked) return 1;
                        lockTestForm.Location = expected;
                        menu.Items[1].PerformClick();
                        var area = Screen.FromRectangle(lockTestForm.Bounds).WorkingArea;
                        if (lockTestForm.Top != expected.Y ||
                            lockTestForm.Left != WidgetPlacement.CenteredLeft(area, lockTestForm.Width)) return 1;
                        placement.Resize(new Size(lockTestForm.ClientSize.Width + 21, lockTestForm.ClientSize.Height));
                        if (lockTestForm.Left != WidgetPlacement.CenteredLeft(area, lockTestForm.Width) ||
                            placement.DragLocation(new Point(0, expected.Y + 10)).Y != expected.Y + 10 ||
                            placement.DragLocation(Point.Empty).X != lockTestForm.Left) return 1;
                        var restoredPlacement = new WidgetPlacement(lockTestForm, "test-lock", Point.Empty, heatPosition);
                        if (restoredPlacement.DragLocation(Point.Empty).X != lockTestForm.Left) return 1;
                        menu.Items[1].PerformClick();
                        if (placement.DragLocation(new Point(10, 20)) != new Point(10, 20)) return 1;
                    }
                }
                finally { File.Delete(heatPosition); }
                if (HeatingWidget.InvertCoverPosition(20) != 80 || HeatingWidget.InvertCoverPosition(80) != 20) return 1;
                var sensorReading = new Sensor { state = "21.357", attributes = new SolarTile.SensorAttributes { unit_of_measurement = "°C" } };
                if (!DoorWidget.IsSensor("sensor.temperatur") || DoorWidget.IsSensor("switch.test") ||
                    DoorWidget.SensorValue(sensorReading, 1) != 21.4m.ToString("F1", CultureInfo.CurrentCulture) + " °C") return 1;
                var heatStyle = new Appearance();
                using (var sensorOptions = new Options(null, new string[0], heatStyle, heatStyle,
                    new DoorWidget.DoorEditor(new Door[0], () => null, sensors: true))) { }
                using (var sensorPreview = DoorWidget.PaintSensors(new Appearance { Vertical = true }, new Door[0],
                    new[] { "Temperatur", "Luftfeuchte" }, new[] { "21 °C", "50 %" }, 1, 0))
                    if (sensorPreview.Height < 88) return 1;
                using (var coverOptions = new Options(null, new string[0], heatStyle, heatStyle,
                    new DoorWidget.DoorEditor(new Door[0], () => null, cover: true))) { }
                using (var coverPreview = HeatingWidget.Preview(heatStyle, new Door[0], 1, true))
                    if (coverPreview.Width < 100 || !HeatingWidget.IsCover("cover.rollladen")) return 1;
                using (var heatOptions = new Options(null, new string[0], heatStyle, heatStyle,
                    new DoorWidget.DoorEditor(new Door[0], () => null, heating: true))) { }
                using (var heatPreview = HeatingWidget.Preview(heatStyle, new Door[0], 1))
                    if (heatPreview.Width < 100 || heatPreview.GetPixel(0, 0).A != 0) return 1;
                if (!HeatingWidget.IsClimate("climate.wohnzimmer") || HeatingWidget.IsClimate("sensor.temperatur")) return 1;
                var lamp = new Door { Name = UiText.Get("Lampe", "Light"), IconKind = "Schalter", Open = "Ein", Closed = "Aus" };
                using (var options = new Options(null, new string[0], new Appearance(), new Appearance(),
                    new DoorWidget.DoorEditor(new[] { lamp }, () => null, true))) { }
                using (var on = DoorWidget.PaintDoors(new Appearance(), new[] { lamp.Name }, new[] { lamp.Describe("on") },
                    new[] { true }, 1, 0, new[] { lamp }))
                using (var off = DoorWidget.PaintDoors(new Appearance(), new[] { lamp.Name }, new[] { lamp.Describe("off") },
                    new[] { false }, 1, 0, new[] { lamp }))
                    if (on.GetPixel(30, 36) == off.GetPixel(30, 36)) return 1;
                var names = new[] { "Testtür" };
                using (var open = DoorWidget.PaintDoors(new Appearance(), names, new[] { "Status" }, new[] { true }, 1, 0))
                using (var closed = DoorWidget.PaintDoors(new Appearance(), names, new[] { "Status" }, new[] { false }, 1, 0))
                    if (open.GetPixel(25, 33) == closed.GetPixel(25, 33)) return 1;
                tile.Show();
                tile.appearance = new Appearance { BackgroundOpacity = 0, TextSize = 28 };
                tile.RenderTile();
                int oldWidth = tile.Width;
                int oldHeight = tile.Height;
                tile.appearance.Typeface = "Courier New";
                tile.appearance.MetricSpacing += 8;
                tile.appearance.FramePadding += 4;
                tile.appearance.IconSize = 96;
                tile.RenderTile();
                bool resizes = tile.Width > oldWidth && tile.Height > oldHeight &&
                    tile.AccessibleName.Contains(UiText.Get("-7,2 kW", "-7.2 kW"));
                tile.Close();
                return resizes && tile.values[3] == UiText.Get("-7,2 kW", "-7.2 kW") && Format(3, "-7.2", "kW") == UiText.Get("-7,2 kW", "-7.2 kW") &&
                    DisplayReading(3, NetReading(ParseReading(3, "0.1", "kW").Value,
                        ParseReading(3, "300", "W").Value)) == "-200 W" && Format(0, "1000", "W") == UiText.Get("1.000 W", "1,000 W") &&
                    Format(2, "1349", "W") == UiText.Get("1,35 kW", "1.35 kW") && Format(1, "100", "%") == "100 %" && Format(0, "unavailable", "W") == null &&
                    new Settings { Token = Settings.Protect("test-token") }.ReadToken() == "test-token" ? 0 : 1;
            }
        }
        bool demo = Array.IndexOf(args, "--demo") >= 0;
        using (var tile = new SolarTile(demo))
        {
            if (demo) Application.Run(tile);
            else
            {
                // Create the hidden host even when no widget is selected.
                var handle = tile.Handle;
                tile.StartWidgets();
                Application.Run(new ApplicationContext());
            }
        }
        return 0;
    }
}

sealed class PreviewPanel : Control
{
    internal Action<int, int, int[]> ResizeField;
    float[] fieldStarts = new float[0], fieldSizes = new float[0];
    float fieldScale = 1, dragScale = 1;
    Point imageOrigin;
    Size imageSize;
    int draggingField = -1, dragX, initialWidth;
    int[] measuredWidths = new int[0];

    internal void RecordFields(float[] starts, float[] sizes, float scale)
    {
        fieldStarts = (float[])starts.Clone();
        fieldSizes = (float[])sizes.Clone();
        fieldScale = scale;
    }

    int FieldAt(Point point)
    {
        if (point.Y < imageOrigin.Y || point.Y > imageOrigin.Y + imageSize.Height) return -1;
        for (int i = 0; i < fieldStarts.Length; ++i)
            if (Math.Abs(point.X - imageOrigin.X - fieldStarts[i] - fieldSizes[i]) <= 6) return i;
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || ResizeField == null) return;
        draggingField = FieldAt(e.Location);
        if (draggingField < 0) return;
        measuredWidths = fieldSizes.Select(width => (int)Math.Round(width / fieldScale)).ToArray();
        initialWidth = measuredWidths[draggingField];
        dragX = e.X;
        dragScale = fieldScale;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = draggingField >= 0 || FieldAt(e.Location) >= 0 ? Cursors.SizeWE : Cursors.Default;
        if (draggingField < 0 || !Capture) return;
        ResizeField(draggingField, Math.Max(36, Math.Min(800,
            initialWidth + (int)Math.Round(2 * (e.X - dragX) / dragScale))), measuredWidths);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        draggingField = -1;
        Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) draggingField = -1;
    }

    readonly Func<Appearance> readAppearance;
    internal Func<float, Bitmap> Tile { get; set; }
    readonly string[] values;

    internal PreviewPanel(Func<Appearance> readAppearance, string[] values)
    {
        this.readAppearance = readAppearance;
        this.values = (string[])values.Clone();
        DoubleBuffered = true;
        ResizeRedraw = true;
        AccessibleName = UiText.Get("Live-Vorschau für ", "Live preview for ") + ReleaseVersion.SolarName;
        AccessibleDescription = UiText.Get("Feldbreiten durch Ziehen der blauen Trennlinien ändern.", "Drag the blue dividers to resize individual fields.");
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        var g = args.Graphics;
        g.Clear(Color.FromArgb(189, 193, 200));
        int cell = Math.Max(6, (int)(12 * g.DpiX / 96f));
        using (var light = new SolidBrush(Color.FromArgb(224, 226, 230)))
            for (int y = 0; y < Height; y += cell)
                for (int x = y / cell % 2 * cell; x < Width; x += cell * 2)
                    g.FillRectangle(light, x, y, cell, cell);
        float scale = g.DpiX / 96f;
        fieldStarts = fieldSizes = new float[0];
        using (var bitmap = Tile == null ? SolarTile.PaintTile(readAppearance(), values, scale, 0, new float[4]) : Tile(scale))
        {
            float fit = Math.Min(1f, Math.Min((Width - 16f) / bitmap.Width, (Height - 12f) / bitmap.Height));
            if (fit <= 0) return;
            if (fit == 1f)
            {
                imageSize = bitmap.Size;
                imageOrigin = new Point((Width - bitmap.Width) / 2, (Height - bitmap.Height) / 2);
                g.DrawImageUnscaled(bitmap, (Width - bitmap.Width) / 2, (Height - bitmap.Height) / 2);
            }
            else
            {
                using (var fitted = Tile == null ? SolarTile.PaintTile(readAppearance(), values, scale * fit, 0, new float[4]) : Tile(scale * fit))
                {
                    imageSize = fitted.Size;
                    imageOrigin = new Point((Width - fitted.Width) / 2, (Height - fitted.Height) / 2);
                    g.DrawImageUnscaled(fitted, (Width - fitted.Width) / 2, (Height - fitted.Height) / 2);
                }
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var line = new Pen(Color.FromArgb(140, 62, 122, 160), 1))
            using (var grip = new Pen(Color.FromArgb(225, 62, 122, 160), 2))
                for (int i = 0; i < fieldStarts.Length; ++i)
                {
                    float x = imageOrigin.X + fieldStarts[i] + fieldSizes[i];
                    g.DrawLine(line, x, imageOrigin.Y + 3, x, imageOrigin.Y + imageSize.Height - 3);
                    float center = imageOrigin.Y + imageSize.Height / 2f;
                    g.DrawLine(grip, x, center - 5, x, center + 5);
                }
        }
    }
}

static class SolarIcon
{
    internal static void Draw(Graphics g, int metric, Color color)
    {
        using (var ink = new SolidBrush(color))
        using (var line = new Pen(ink, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            switch (metric)
            {
                case 0:
                    g.FillEllipse(ink, 14, 6, 4, 4);
                    g.DrawLine(line, 10, 9, 11, 10);
                    g.DrawLine(line, 21, 10, 22, 9);
                    g.DrawPolygon(line, new[] { new Point(10, 13), new Point(22, 13), new Point(25, 23), new Point(7, 23) });
                    g.DrawLine(line, 9, 18, 23, 18);
                    g.DrawLine(line, 14, 13, 13, 23);
                    g.DrawLine(line, 18, 13, 19, 23);
                    g.DrawLine(line, 13, 26, 19, 26);
                    break;
                case 1:
                    g.FillRectangle(ink, 14, 6, 4, 2);
                    using (var battery = new GraphicsPath())
                    {
                        battery.AddArc(11, 8, 4, 4, 180, 90);
                        battery.AddArc(17, 8, 4, 4, 270, 90);
                        battery.AddArc(17, 21, 4, 4, 0, 90);
                        battery.AddArc(11, 21, 4, 4, 90, 90);
                        battery.CloseFigure();
                        g.DrawPath(line, battery);
                    }
                    g.FillRectangle(ink, 13, 17, 6, 5);
                    break;
                case 2:
                    g.DrawLines(line, new[] { new Point(6, 15), new Point(16, 7), new Point(26, 15) });
                    g.DrawLines(line, new[] { new Point(9, 13), new Point(9, 24), new Point(14, 24),
                        new Point(14, 18), new Point(18, 18), new Point(18, 24), new Point(23, 24), new Point(23, 13) });
                    break;
                case 3:
                    g.DrawLines(line, new[] { new Point(10, 25), new Point(14, 17), new Point(15, 7),
                        new Point(17, 7), new Point(18, 17), new Point(22, 25) });
                    g.DrawLines(line, new[] { new Point(9, 13), new Point(12, 10), new Point(20, 10), new Point(23, 13) });
                    g.DrawLines(line, new[] { new Point(8, 19), new Point(12, 16), new Point(20, 16), new Point(24, 19) });
                    g.DrawLine(line, 12, 21, 20, 21);
                    break;
            }
        }
    }
}
static class LayeredTile
{
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);

    internal static float ScaleFor(Form window)
    {
        try
        {
            uint dpi = GetDpiForWindow(window.Handle);
            if (dpi != 0) return dpi / 96f;
        }
        catch (EntryPointNotFoundException) { }
        using (var graphics = window.CreateGraphics()) return graphics.DpiX / 96f;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Blend
    {
        internal byte Operation, Flags, Alpha, Format;
    }

    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr source);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, IntPtr position,
        ref Size size, IntPtr source, ref Point offset, int colorKey, ref Blend blend, int flags);

    internal static void Present(IntPtr window, Bitmap bitmap, byte opacity = 255)
    {
        var dc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            var image = bitmap.GetHbitmap(Color.FromArgb(0));
            try
            {
                var previous = SelectObject(dc, image);
                try
                {
                    var size = bitmap.Size;
                    var offset = Point.Empty;
                    var blend = new Blend { Alpha = (byte)(WidgetAttention.Alpha(window) * opacity / 255), Format = 1 };
                    if (!UpdateLayeredWindow(window, IntPtr.Zero, IntPtr.Zero, ref size, dc,
                        ref offset, 0, ref blend, 2))
                        throw new System.ComponentModel.Win32Exception();
                }
                finally { SelectObject(dc, previous); }
            }
            finally { DeleteObject(image); }
        }
        finally { DeleteDC(dc); }
    }
}
static class WidgetIcon
{
    // Read the current artwork directly from the executable, independent of the shell icon cache.
    internal static Icon Load(bool solar = false)
    {
        using (var source = typeof(WidgetIcon).Assembly.GetManifestResourceStream(solar ? "Solaranlage.ico" : "HomeAssistantWidget.ico"))
        using (var icon = new Icon(source, SystemInformation.IconSize))
            return (Icon)icon.Clone();
    }
}
sealed class WidgetMenu : ContextMenuStrip
{
    static readonly Color Surface = Color.FromArgb(31, 31, 31);
    static readonly Color Highlight = Color.FromArgb(57, 57, 57);
    static readonly Color TextColor = Color.FromArgb(245, 245, 245);
    static readonly ToolStripRenderer MenuRenderer = new MenuStyle();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripDropDownMenu, object>
        StyledMenus = new System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripDropDownMenu, object>();

    internal WidgetMenu()
    {
        StyleMenu(this);
    }

    static int Scale(ToolStrip menu, int value)
    {
        return menu.LogicalToDeviceUnits(value);
    }

    static void StyleMenu(ToolStripDropDownMenu menu)
    {
        object styled;
        if (StyledMenus.TryGetValue(menu, out styled)) return;
        StyledMenus.Add(menu, new object());
        menu.Renderer = MenuRenderer;
        menu.BackColor = Surface;
        menu.ForeColor = TextColor;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        ApplyInsets(menu);
        menu.HandleCreated += delegate { ApplyCorners(menu); };
        menu.ItemAdded += delegate(object sender, ToolStripItemEventArgs args) { StyleItem(menu, args.Item); };
        menu.Opening += delegate
        {
            menu.ShowCheckMargin = menu.Items.OfType<ToolStripMenuItem>().Any(item => item.CheckOnClick || item.Checked);
            ApplyInsets(menu);
            foreach (ToolStripItem item in menu.Items) StyleItem(menu, item);
            ApplyCorners(menu);
        };
        foreach (ToolStripItem item in menu.Items) StyleItem(menu, item);
    }

    static void ApplyInsets(ToolStripDropDownMenu menu)
    {
        int inset = Scale(menu, 8);
        menu.Padding = new Padding(inset);
    }

    static void StyleItem(ToolStripDropDownMenu menu, ToolStripItem item)
    {
        var entry = item as ToolStripMenuItem;
        if (entry == null) return;
        entry.ForeColor = TextColor;
        entry.Padding = new Padding(Scale(menu, 8), Scale(menu, 5), Scale(menu, 8), Scale(menu, 5));
        if (!entry.HasDropDownItems) return;
        var submenu = entry.DropDown as ToolStripDropDownMenu;
        if (submenu != null) StyleMenu(submenu);
    }

    static void ApplyCorners(ToolStripDropDownMenu menu)
    {
        if (!menu.IsHandleCreated) return;
        SettingsTheme.UseRoundedWindowCorners(menu.Handle);
    }

    sealed class MenuStyle : ToolStripSystemRenderer
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs args) { }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs args) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs args)
        {
            using (var brush = new SolidBrush(Surface))
                args.Graphics.FillRectangle(brush, args.ToolStrip.ClientRectangle);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs args)
        {
            if (!args.Item.Selected && !args.Item.Pressed) return;
            var bounds = new Rectangle(Point.Empty, args.Item.Size);
            bounds.Inflate(-Scale(args.ToolStrip, 3), -1);
            if (bounds.Width < 2 || bounds.Height < 2) return;
            var state = args.Graphics.Save();
            try
            {
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shape = SettingsTheme.Rounded(bounds, Scale(args.ToolStrip, 5)))
                using (var brush = new SolidBrush(Highlight))
                    args.Graphics.FillPath(brush, shape);
            }
            finally { args.Graphics.Restore(state); }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs args)
        {
            args.TextColor = args.Item.Enabled ? TextColor : Color.FromArgb(148, 148, 148);
            var text = args.TextRectangle;
            args.TextRectangle = new Rectangle(text.X, 0, text.Width, args.Item.Height);
            args.TextFormat = (args.TextFormat & ~TextFormatFlags.Bottom) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            base.OnRenderItemText(args);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs args)
        {
            args.ArrowColor = args.Item.Enabled ? TextColor : Color.FromArgb(148, 148, 148);
            base.OnRenderArrow(args);
        }
    }
}

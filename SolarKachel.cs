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
    public int OpenIconColor { get; set; }

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
    }
}

sealed class Settings
{
    public string Url { get; set; }
    public string Token { get; set; }
    public string[] Entities { get; set; }
    public string GridExportEntity { get; set; }
    public Appearance Appearance { get; set; }

    internal static readonly string[] Defaults = {
        "sensor.pv_gesamt", "sensor.battery_level",
        "sensor.stromverbrauch_haus", "sensor.import_power"
    };
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SolarKachel", "settings.json");

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
    static readonly Color Surface = Color.FromArgb(24, 24, 26);
    static readonly Color Input = Color.FromArgb(43, 43, 47);
    static readonly Color Border = Color.FromArgb(80, 80, 87);
    readonly GroupBox connection = new GroupBox { Text = "Home Assistant" };
    readonly GroupBox design = new GroupBox { Text = "Darstellung" };
    readonly NumericUpDown iconSize;
    readonly NumericUpDown spacing;
    readonly NumericUpDown padding;
    readonly Button background;
    readonly Button textColor;
    readonly Button iconColor;
    readonly NumericUpDown textSize;
    readonly ComboBox typeface;
    readonly NumericUpDown opacity;
    internal Settings Result { get; private set; }

    internal Appearance SelectedAppearance { get; private set; }

    internal Options(Settings previous, string[] values, Appearance doorStyle = null, Appearance solarStyle = null,
        DoorWidget.DoorEditor doorEditor = null)
    {
        this.previous = previous;
        Icon = WidgetIcon.Load();
        Text = ReleaseVersion.ProductName + " – Einstellungen";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        ClientSize = new Size(876, 498);
        BackColor = Surface;
        ForeColor = Color.WhiteSmoke;
        var darkTitleBar = 1;
        DwmSetWindowAttribute(Handle, 20, ref darkTitleBar, sizeof(int));
        design.Bounds = new Rectangle(12, 10, 395, 346);
        connection.Bounds = new Rectangle(417, 10, 447, 318);
        design.ForeColor = connection.ForeColor = ForeColor;
        Controls.AddRange(new Control[] { design, connection });
        var appearance = doorStyle ?? (previous == null ? new Appearance() : previous.Appearance ?? new Appearance());
        background = AddColor("Hintergrundfarbe", appearance.Background, 23);
        textColor = AddColor("Schriftfarbe", appearance.Text, 54);
        iconColor = AddColor("Icon-Farbe", appearance.Icons, 85);
        opacity = AddNumber("Hintergrund-Deckkraft (%)", appearance.BackgroundOpacity, 0, 100, 116);
        textSize = AddNumber("Schriftgröße (pt)", appearance.TextSize, 8, 28, 160);
        design.Controls.Add(new Label { Text = "0 % = transparenter Hintergrund", AutoSize = true,
            ForeColor = Color.Silver, Location = new Point(14, 142) });
        iconSize = AddNumber("Icon-Größe (px)", appearance.IconSize, 20, 96, 190);
        spacing = AddNumber("Abstand zwischen den Anzeigen (px)", appearance.MetricSpacing, 0, 100, 220);
        padding = AddNumber("Rahmengröße / Innenabstand (px)", appearance.FramePadding, 0, 48, 250);
        design.Controls.Add(new Label { Text = "Schriftart", AutoSize = true, Location = new Point(14, 285) });
        typeface = new DarkFontPicker { Bounds = new Rectangle(151, 281, 231, 26),
            DropDownStyle = ComboBoxStyle.DropDownList, Sorted = true,
            BackColor = Input, ForeColor = ForeColor, FlatStyle = FlatStyle.Flat, AccessibleName = "Schriftart" };
        foreach (var family in FontFamily.Families)
        {
            using (family)
                if (family.IsStyleAvailable(FontStyle.Regular) || family.IsStyleAvailable(FontStyle.Bold))
                    typeface.Items.Add(family.Name);
        }
        typeface.SelectedItem = appearance.Typeface;
        if (typeface.SelectedIndex < 0) typeface.SelectedItem = "Segoe UI";
        design.Controls.Add(typeface);
        var fontArrow = DarkArrow("", "Schriftarten öffnen");
        fontArrow.Paint += delegate(object sender, PaintEventArgs args)
        {
            int x = fontArrow.Width / 2, y = fontArrow.Height / 2;
            using (var pen = new Pen(Color.Silver))
            {
                args.Graphics.DrawLine(pen, 0, 2, 0, fontArrow.Height - 3);
                args.Graphics.DrawLines(pen, new[] { new Point(x - 4, y - 2),
                    new Point(x, y + 2), new Point(x + 4, y - 2) });
            }
        };
        fontArrow.Bounds = new Rectangle(355, 282, 26, 23);
        fontArrow.Click += delegate { typeface.Focus(); typeface.DroppedDown = true; };
        design.Controls.Add(fontArrow);
        fontArrow.BringToFront();
        var reset = new Button { Text = "Standard wiederherstellen", AutoSize = true, Location = new Point(14, 456) };
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
        };
        StyleButton(reset);
        Controls.Add(reset);
        var versionLabel = new Label { Text = ReleaseVersion.Display, ForeColor = Color.Silver,
            AutoSize = true, Location = new Point(228, 462) };
        Controls.Add(versionLabel);
        address = AddField("HA-Adresse", previous == null ? "http://homeassistant.local:8123" : previous.Url, 24);
        token = AddField(previous == null ? "Zugriffstoken" : "Zugriffstoken (optional)", "", 60);
        token.UseSystemPasswordChar = true;
        if (previous != null)
            connection.Controls.Add(new Label { Text = "Leer lassen, um den gespeicherten Token zu behalten.",
                ForeColor = Color.Silver, AutoSize = true, Location = new Point(12, 87) });
        string[] labels = { "Solarerzeugung", "Akkustand", "Hausverbrauch", "Netzleistung / Netzbezug" };
        for (int i = 0; i < 4; i++)
            entities[i] = AddField(labels[i], (previous == null ? Settings.Defaults : previous.Entities)[i], 110 + i * 36, i == 1 ? "%" : "W");
        gridExport = AddField("Netzeinspeisung (optional)",
            previous == null ? "sensor.export_power" : previous.GridExportEntity ?? "", 254, "W");
        var save = new Button { Text = "Speichern", Bounds = new Rectangle(764, 456, 100, 30) };
        var cancel = new Button { Text = "Abbrechen", Bounds = new Rectangle(656, 456, 100, 30), DialogResult = DialogResult.Cancel };
        StyleButton(save);
        StyleButton(cancel);
        if (doorStyle == null)
        {
            save.Click += Save;
            AddCopyMenu(design, new Point(14, 310), "Solar", null, ApplyStyle);
        }
        Controls.AddRange(new Control[] { save, cancel });
        var previewGroup = new GroupBox { Text = "Live-Vorschau", ForeColor = ForeColor,
            Bounds = new Rectangle(12, 362, 852, 86) };
        var preview = new PreviewPanel(ReadAppearance, values) { Bounds = new Rectangle(10, 20, 832, 56) };
        previewGroup.Controls.Add(preview);
        Controls.Add(previewGroup);
        foreach (var sample in new[] { background, textColor, iconColor })
            sample.BackColorChanged += delegate { preview.Invalidate(); };
        foreach (var field in new[] { opacity, textSize, iconSize, spacing, padding })
            field.ValueChanged += delegate { preview.Invalidate(); };
        typeface.SelectedIndexChanged += delegate { preview.Invalidate(); };
        if (doorStyle != null)
        {
            Text = (doorEditor.Cover ? HeatingWidget.CoverTitle : doorEditor.Heating ? HeatingWidget.Title : doorEditor.Switches ? DoorWidget.SwitchTitle : DoorWidget.DoorTitle) + " – Einstellungen";
            ClientSize = new Size(1090, 554);
            design.Height = 318;
            connection.Width = 661;
            previewGroup.Bounds = new Rectangle(12, 334, 1066, 170);
            preview.Bounds = new Rectangle(10, 20, 1046, 140);
            save.Location = new Point(978, 512);
            cancel.Location = new Point(870, 512);
            reset.Top = 512;
            versionLabel.Top = 518;
            foreach (var control in connection.Controls.Cast<Control>().ToArray()) control.Dispose();
            connection.Text = doorEditor.Cover ? HeatingWidget.CoverTitle : doorEditor.Heating ? "Thermostate" : doorEditor.Switches ? "Schalter und Sensoren" : DoorWidget.DoorTitle;
            doorEditor.Bounds = new Rectangle(8, 22, 645, 220);
            connection.Controls.Add(doorEditor);
            var sample = AddColor(doorEditor.Switches ? "Icon-Farbe bei Ein" : "Icon-Farbe bei offener Tür", appearance.OpenIconColor, 247, connection);
            var showOpen = new CheckBox { Text = doorEditor.Switches ? "Ein-Zustand in Vorschau anzeigen" : "Offene Kontakte in Vorschau anzeigen", AutoSize = true,
                Location = new Point(doorEditor.Switches ? 160 : 16, 284) };
            connection.Controls.Add(showOpen);
            if (doorEditor.DeviceRows) { showOpen.Hide(); sample.Hide(); connection.Controls[connection.Controls.IndexOf(sample) - 1].Hide(); }
            var vertical = new CheckBox { Text = "Untereinander", AutoSize = true,
                Location = new Point(16, 284), Checked = appearance.Vertical };
            if (doorEditor.Switches) connection.Controls.Add(vertical);
            vertical.CheckedChanged += delegate { preview.Invalidate(); };
            reset.Click += delegate { vertical.Checked = false; };
            AddCopyMenu(connection, new Point(420, 278),
                doorEditor.Cover ? HeatingWidget.CoverTitle : doorEditor.Heating ? HeatingWidget.Title : doorEditor.Switches ? DoorWidget.SwitchTitle : DoorWidget.DoorTitle,
                solarStyle, source =>
                {
                    ApplyStyle(source);
                    sample.BackColor = Color.FromArgb(source.OpenIconColor);
                    vertical.Checked = source.Vertical;
                });
            reset.Click += delegate { sample.BackColor = Color.FromArgb(new Appearance().OpenIconColor); };
            preview.Tile = scale =>
            {
                var style = ReadAppearance();
                style.OpenIconColor = sample.BackColor.ToArgb();
                style.Vertical = vertical.Checked;
                var rows = doorEditor.ReadRows();
                if (doorEditor.DeviceRows) return HeatingWidget.Preview(style, rows, scale, doorEditor.Cover);
                if (rows.Length == 0) rows = new[] { doorEditor.Switches
                    ? new Door { Name = "Lampe", Open = "Ein", Closed = "Aus", IconKind = "Schalter" }
                    : new Door { Name = "Haustür", Open = "Offen", Closed = "Geschlossen" } };
                return DoorWidget.PaintDoors(style, rows.Select(door => door.Name).ToArray(),
                    rows.Select(door => showOpen.Checked ? door.Open : door.Closed).ToArray(),
                    Enumerable.Repeat(showOpen.Checked, rows.Length).ToArray(), scale, 0, rows);
            };
            doorEditor.RowsChanged += delegate { preview.Invalidate(); };
            sample.BackColorChanged += delegate { preview.Invalidate(); };
            showOpen.CheckedChanged += delegate { preview.Invalidate(); };
            save.Click += delegate
            {
                if (!doorEditor.SaveRows()) return;
                SelectedAppearance = ReadAppearance();
                SelectedAppearance.OpenIconColor = sample.BackColor.ToArgb();
                SelectedAppearance.Vertical = vertical.Checked;
                DialogResult = DialogResult.OK;
            };
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
    }

    void AddCopyMenu(Control parent, Point location, string current, Appearance solar, Action<Appearance> apply)
    {
        var button = new Button { Text = "Ansicht von Widget übernehmen", AutoSize = true, Location = location };
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
            var browse = DarkArrow("", "Sensor auswählen: " + caption);
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
            using (var pen = new Pen(Color.FromArgb(103, 103, 109)))
                args.Graphics.DrawRectangle(pen, 0, 0, frame.ClientSize.Width - 1, frame.ClientSize.Height - 1);
        };
        return frame;
    }

    void AddNumberFrame(NumericUpDown field, Rectangle bounds)
    {
        var frame = CreateInputFrame(bounds);
        var minus = DarkArrow("", "Verringern");
        var plus = DarkArrow("", "Erhöhen");
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
            FramePadding = (int)padding.Value
        };
    }

    Uri ReadAddress()
    {
        Uri uri;
        if (!Uri.TryCreate(address.Text.Trim(), UriKind.Absolute, out uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Bitte eine gültige HTTP- oder HTTPS-Adresse eingeben.");
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
            if (accessToken.Length == 0) throw new ArgumentException("Bitte zuerst einen Zugriffstoken eingeben.");

            var sensors = await LoadSensors(uri, accessToken);
            if (IsDisposed) return;
            using (var picker = new SensorPicker(sensors, unit, target.Text))
                if (picker.ShowDialog(this) == DialogResult.OK) target.Text = picker.SelectedEntity;
        }
        catch (ArgumentException ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, Text); }
        catch (Exception) { if (!IsDisposed) MessageBox.Show(this, "Sensorliste konnte nicht geladen werden. Bitte Verbindung und Zugriffstoken prüfen.", Text); }
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
                    throw new ArgumentException("Home Assistant meldet HTTP " + (int)response.StatusCode + ".");
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
            Text = "Sensor auswählen";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(660, 410);
            BackColor = Surface;
            ForeColor = Color.WhiteSmoke;
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));

            var matches = (sensors ?? new SolarTile.Sensor[0]).Where(sensor => sensor != null &&
                sensor.entity_id != null && sensor.attributes != null && (unit == "window_cover"
                    ? HeatingWidget.IsCover(sensor.entity_id) && !new[] { "garage", "door", "gate" }.Contains(sensor.attributes.device_class)
                    : unit == "climate" ? HeatingWidget.IsClimate(sensor.entity_id) : unit == "switch" ? DoorWidget.IsSwitchEntity(sensor.entity_id) : unit == null
                    ? (sensor.entity_id.StartsWith("binary_sensor.", StringComparison.Ordinal) &&
                        new[] { "door", "garage_door", "opening", "window" }.Contains(sensor.attributes.device_class)) ||
                        sensor.entity_id.StartsWith("cover.", StringComparison.Ordinal)
                    : sensor.entity_id.StartsWith("sensor.", StringComparison.Ordinal) && (unit == "%"
                        ? sensor.attributes.unit_of_measurement == "%"
                        : new[] { "W", "kW", "MW" }.Contains(sensor.attributes.unit_of_measurement))))
                .OrderBy(sensor => sensor.Display).ToArray();
            var label = new Label { Text = unit == "%" ? "Sensoren mit % (Akkustand)" : "Leistungssensoren mit W, kW oder MW",
                AutoSize = true, Location = new Point(12, 12) };
            var search = new TextBox { Bounds = new Rectangle(12, 38, 636, 24), BackColor = Input,
                ForeColor = ForeColor, AccessibleName = "Sensoren suchen" };
            list = new ListBox { Bounds = new Rectangle(12, 70, 636, 288), BackColor = Input,
                ForeColor = ForeColor, DisplayMember = "Display", HorizontalScrollbar = true };
            var accept = new Button { Text = "Übernehmen", Bounds = new Rectangle(538, 369, 110, 28) };
            var cancel = new Button { Text = "Abbrechen", Bounds = new Rectangle(420, 369, 110, 28), DialogResult = DialogResult.Cancel };
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
                label.Text = list.Items.Count == 0 ? "Keine passenden Sensoren gefunden." : list.Items.Count + " passende Sensoren";
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
                if (!Regex.IsMatch(entity, @"^[a-z0-9_]+\.[a-z0-9_]+$"))
                    throw new ArgumentException("Bitte vier gültige Entitäts-IDs eingeben.");
                next.Entities[i] = entity;
            }

            next.GridExportEntity = gridExport.Text.Trim().ToLowerInvariant();
            if (next.GridExportEntity.Length != 0 &&
                !Regex.IsMatch(next.GridExportEntity, @"^[a-z0-9_]+\.[a-z0-9_]+$"))
                throw new ArgumentException("Bitte eine gültige Entitäts-ID für die Netzeinspeisung eingeben.");

            if (token.Text.Trim().Length != 0) next.Token = Settings.Protect(token.Text.Trim());
            else if (CanReuseToken(uri))
                next.Token = previous.Token;
            else throw new ArgumentException("Bitte einen Zugriffstoken für diese Adresse eingeben.");

            next.Appearance = ReadAppearance();
            next.Save();
            Result = next;
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, Text); }
        catch (Exception) { MessageBox.Show(this, "Die Einstellungen konnten nicht gespeichert werden.", Text); }
    }
}

sealed class SolarTile : Form
{
    static readonly string[] Captions = { "Solar", "Akku", "Haus", "Netz" };
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
    HomeAssistantEvents liveEvents;
    readonly object liveLock = new object();
    readonly System.Collections.Generic.HashSet<string> liveChanges = new System.Collections.Generic.HashSet<string>();
    readonly Timer liveTimer = new Timer { Interval = 120 };
    bool exiting;
    readonly ContextMenuStrip trayMenu = new WidgetMenu();
    static readonly string WidgetsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SolarKachel", "widgets.txt");
    static readonly string PositionFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SolarKachel", "position.txt");

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
            menu.Items.Add("Einstellungen", null, delegate { Configure(); });
            menu.Items.Add("Aktualisieren", null, async delegate { await RefreshValues(); });
            try { settings = Settings.Load(); }
            catch (Exception) { ShowEmpty("Einstellungen konnten nicht gelesen werden."); }
            Shown += async delegate
            {
                ApplyAppearance();
                RestorePosition();
                if (settings != null) await RefreshValues();
            };
            timer.Tick += async delegate { await RefreshValues(); };
        }
        var pin = new ToolStripMenuItem("Immer im Vordergrund") { CheckOnClick = true };
        pin.CheckedChanged += delegate { TopMost = pin.Checked; if (doors != null) doors.TopMost = TopMost; if (switches != null) switches.TopMost = TopMost; if (heating != null) heating.TopMost = TopMost; if (covers != null) covers.TopMost = TopMost; };
        menu.Items.Add(pin);
        menu.Items.Add("Schließen", null, delegate
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
            var addWidget = new ToolStripMenuItem("Widget hinzufügen");
            addWidget.DropDownItems.Add("Solar", null, delegate { OpenSolar(); });
            addWidget.DropDownItems.Add(DoorWidget.DoorTitle, null, delegate { ShowDoors(); });
            addWidget.DropDownItems.Add(DoorWidget.SwitchTitle, null, delegate { ShowDoors(switchWidget: true); });
            addWidget.DropDownItems.Add(HeatingWidget.Title, null, delegate { ShowHeating(); });
            addWidget.DropDownItems.Add(HeatingWidget.CoverTitle, null, delegate { ShowHeating(cover: true); });
            trayMenu.Items.Add(addWidget);
            trayMenu.Items.Add("Einstellungen", null, delegate { Configure(); });
            trayMenu.Items.Add("Programm beenden", null, delegate { ExitWidgets(); });
            tray = new NotifyIcon { Icon = WidgetIcon.Load(), Text = ReleaseVersion.ProductName, ContextMenuStrip = trayMenu };
            tray.DoubleClick += delegate { Configure(); };
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
        if (settings == null) return;
        string[] active = DoorWidget.HasSavedDoors() ? new[] { "solar", "doors" } : new[] { "solar" };
        try { if (File.Exists(WidgetsFile)) active = File.ReadAllLines(WidgetsFile); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (active.Contains("solar")) Show();
        if (active.Contains("doors")) ShowDoors(remember: false);
        if (active.Contains("switches")) ShowDoors(remember: false, switchWidget: true);
        if (active.Contains("heating")) ShowHeating(false);
        if (active.Contains("covers")) ShowHeating(false, cover: true);
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
                File.WriteAllLines(temp, new[] { Visible ? "solar" : "", doors != null && doors.Visible ? "doors" : "", switches != null && switches.Visible ? "switches" : "", heating != null && heating.Visible ? "heating" : "", covers != null && covers.Visible ? "covers" : "" });
                if (File.Exists(WidgetsFile)) File.Replace(temp, WidgetsFile, null);
                else File.Move(temp, WidgetsFile);
            }
            finally { File.Delete(temp); }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void OpenSolar()
    {
        if (settings == null && !Configure()) return;
        bool reopening = IsHandleCreated;
        Show();
        Activate();
        if (reopening) RefreshVisibleSolar();
        RememberWidgets();
    }

    async void RefreshVisibleSolar() { await RefreshValues(); }

    void ShowDoors(bool remember = true, bool switchWidget = false)
    {
        if (settings == null && !Configure()) return;
        var existing = switchWidget ? switches : doors;
        if (existing != null)
        {
            existing.Show();
            existing.Activate();
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
        widget.Show();
        widget.VisibleChanged += delegate { if (!widget.Visible) RememberWidgets(); };
        if (remember) RememberWidgets();
    }

    void ShowHeating(bool remember = true, bool cover = false)
    {
        if (settings == null && !Configure()) return;
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
            if (ClientSize != bitmap.Size) ClientSize = bitmap.Size;
            LayeredTile.Present(Handle, bitmap);
        }
        AccessibleName = Text = ReleaseVersion.SolarName + ": " + string.Join(", ", Captions[0] + " " + values[0], Captions[1] + " " + values[1],
            Captions[2] + " " + values[2], Captions[3] + " " + values[3]);
    }

    static Font CreateTileFont(string typeface, float size, FontStyle style)
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
        bool namesOnly = false, RectangleF[] iconBounds = null)
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
        using (var measureBitmap = new Bitmap(1, 1))
        using (var measure = Graphics.FromImage(measureBitmap))
        using (var labelFont = CreateTileFont(appearance.Typeface, Math.Max(7, size - 1) * scale, FontStyle.Regular))
        using (var valueFont = CreateTileFont(appearance.Typeface, size * scale, FontStyle.Bold))
        {
            textHeight = namesOnly ? valueFont.GetHeight(measure) : labelFont.GetHeight(measure) + valueFont.GetHeight(measure) + 2 * scale;
            measure.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            for (int i = 0; i < values.Length; i++)
            {
                metricStarts[i] = right;
                float captionWidth = measure.MeasureString(captions[i], namesOnly ? valueFont : labelFont, int.MaxValue, StringFormat.GenericTypographic).Width;
                float valueWidth = measure.MeasureString(values[i], valueFont, int.MaxValue, StringFormat.GenericTypographic).Width;
                float itemWidth = icon + iconTextGap + (float)Math.Ceiling(Math.Max(captionWidth, namesOnly ? 0 : valueWidth)) + 2 * scale;
                widest = Math.Max(widest, itemWidth);
                right += itemWidth + gap;
            }
        }
        float row = Math.Max(icon, textHeight + 2 * scale);
        int width = (int)Math.Ceiling(appearance.Vertical ? widest + inset * 2 : right - gap + inset);
        int height = (int)Math.Ceiling(inset * 2 + (appearance.Vertical ? row * values.Length + gap * (values.Length - 1) : row));
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
                float block = namesOnly ? valueHeight : labelHeight + valueHeight + 2 * scale;
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
                    g.DrawString(captions[i], namesOnly ? valueFont : labelFont, text, textLeft, (float)Math.Round(top), StringFormat.GenericTypographic);
                    if (!namesOnly) g.DrawString(values[i], valueFont, text, textLeft, (float)Math.Round(top + labelHeight + 2 * scale),
                        StringFormat.GenericTypographic);
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
            if (args.Button != MouseButtons.Left) return;
            dragSource = control;
            pointerStart = Cursor.Position;
            windowStart = Location;
            control.Capture = true;
        };
        control.MouseMove += delegate
        {
            if (dragSource != control || !control.Capture) return;
            Point point = Cursor.Position;
            Location = new Point(windowStart.X + point.X - pointerStart.X, windowStart.Y + point.Y - pointerStart.Y);
        };
        control.MouseUp += delegate
        {
            if (dragSource != control) return;
            dragSource = null;
            control.Capture = false;
            if (!demo && Location != windowStart) SavePosition();
        };
    }

    static Point KeepOnScreen(Point saved, Size size, Rectangle area)
    {
        return new Point(
            Math.Max(area.Left, Math.Min(saved.X, area.Right - size.Width)),
            Math.Max(area.Top, Math.Min(saved.Y, area.Bottom - size.Height)));
    }

    void RestorePosition()
    {
        try
        {
            if (!File.Exists(PositionFile)) return;
            string[] coordinates = File.ReadAllText(PositionFile).Split(',');
            int x, y;
            if (coordinates.Length != 2 ||
                !int.TryParse(coordinates[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ||
                !int.TryParse(coordinates[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return;

            var point = new Point(x, y);
            Location = KeepOnScreen(point, Size, Screen.FromPoint(point).WorkingArea);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void SavePosition()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PositionFile));
            File.WriteAllText(PositionFile, string.Format(CultureInfo.InvariantCulture, "{0},{1}", Left, Top));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    bool Configure()
    {
        using (var dialog = new Options(settings, values))
        {
            if ((Visible ? dialog.ShowDialog(this) : dialog.ShowDialog()) != DialogResult.OK) return false;
            settings = dialog.Result;
            StartLive();
            if (Visible) ApplyAppearance();
        }
        if (Visible)
        {
            ShowEmpty("Messwerte werden geladen");
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
            catch (Exception) { ShowEmpty("Zugriffstoken kann nicht gelesen werden. Bitte neu eingeben."); return; }
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
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, current.Url.TrimEnd('/') + "/api/states/" + Uri.EscapeDataString(entity)))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await client.SendAsync(request))
                {
                    int code = (int)response.StatusCode;
                    if (code == 401 || code == 403) return Tuple.Create<double?, string>(null, "Home Assistant hat den Zugriff abgelehnt");
                    if (code == 404) return Tuple.Create<double?, string>(null, "Entität nicht gefunden: " + entity);
                    if (!response.IsSuccessStatusCode) return Tuple.Create<double?, string>(null, "HTTP " + code);
                    var state = new JavaScriptSerializer().Deserialize<Sensor>(await response.Content.ReadAsStringAsync());
                    double? reading = state == null || state.attributes == null
                        ? null : ParseReading(index, state.state, state.attributes.unit_of_measurement);
                    return Tuple.Create(reading, "Sensorwert oder Einheit nicht verfügbar: " + entity);
                }
            }
        }
        catch (Exception) { return Tuple.Create<double?, string>(null, "Messwert konnte nicht geladen werden: " + entity); }
    }

    static string DisplayReading(int index, double number)
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
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
                if (KeepOnScreen(new Point(-800, 90), new Size(400, 66),
                    new Rectangle(-1920, 0, 1920, 1080)) != new Point(-800, 90) ||
                    KeepOnScreen(new Point(990, 990), new Size(400, 66),
                    new Rectangle(0, 0, 1000, 1000)) != new Point(600, 934)) return 1;
                var starts = new float[4];
                using (var preview = PaintTile(new Appearance { BackgroundOpacity = 0, MetricSpacing = 0 }, tile.values, 1, 0, starts))
                    if (preview.GetPixel(preview.Width / 2, 2).A != 0 || preview.Width >= 430) return 1;
                var door = new Door { Open = "Auf", Closed = "Zu" };
                if (door.Describe("on") != "Auf" || door.Describe("closed") != "Zu" ||
                    door.Describe("unavailable") != "Nicht verfügbar") return 1;
                using (var doorWidget = new DoorWidget(() => null, false)) { }
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
                }
                finally { File.Delete(heatPosition); }
                if (HeatingWidget.InvertCoverPosition(20) != 80 || HeatingWidget.InvertCoverPosition(80) != 20) return 1;
                var heatStyle = new Appearance();
                using (var coverOptions = new Options(null, new string[0], heatStyle, heatStyle,
                    new DoorWidget.DoorEditor(new Door[0], () => null, cover: true))) { }
                using (var coverPreview = HeatingWidget.Preview(heatStyle, new Door[0], 1, true))
                    if (coverPreview.Width < 100 || !HeatingWidget.IsCover("cover.rollladen")) return 1;
                using (var heatOptions = new Options(null, new string[0], heatStyle, heatStyle,
                    new DoorWidget.DoorEditor(new Door[0], () => null, heating: true))) { }
                using (var heatPreview = HeatingWidget.Preview(heatStyle, new Door[0], 1))
                    if (heatPreview.Width < 100 || heatPreview.GetPixel(0, 0).A != 0) return 1;
                if (!HeatingWidget.IsClimate("climate.wohnzimmer") || HeatingWidget.IsClimate("sensor.temperatur")) return 1;
                var lamp = new Door { Name = "Lampe", IconKind = "Schalter", Open = "Ein", Closed = "Aus" };
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
                    tile.AccessibleName.Contains("-7,2 kW");
                tile.Close();
                return resizes && tile.values[3] == "-7,2 kW" && Format(3, "-7.2", "kW") == "-7,2 kW" &&
                    DisplayReading(3, NetReading(ParseReading(3, "0.1", "kW").Value,
                        ParseReading(3, "300", "W").Value)) == "-200 W" && Format(0, "1000", "W") == "1.000 W" &&
                    Format(2, "1349", "W") == "1,35 kW" && Format(1, "100", "%") == "100 %" && Format(0, "unavailable", "W") == null &&
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
    readonly Func<Appearance> readAppearance;
    internal Func<float, Bitmap> Tile { get; set; }
    readonly string[] values;

    internal PreviewPanel(Func<Appearance> readAppearance, string[] values)
    {
        this.readAppearance = readAppearance;
        this.values = (string[])values.Clone();
        DoubleBuffered = true;
        ResizeRedraw = true;
        AccessibleName = "Live-Vorschau für " + ReleaseVersion.SolarName;
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
        using (var bitmap = Tile == null ? SolarTile.PaintTile(readAppearance(), values, scale, 0, new float[4]) : Tile(scale))
        {
            float fit = Math.Min(1f, Math.Min((Width - 16f) / bitmap.Width, (Height - 12f) / bitmap.Height));
            if (fit <= 0) return;
            if (fit == 1f)
                g.DrawImageUnscaled(bitmap, (Width - bitmap.Width) / 2, (Height - bitmap.Height) / 2);
            else
            {
                using (var fitted = Tile == null ? SolarTile.PaintTile(readAppearance(), values, scale * fit, 0, new float[4]) : Tile(scale * fit))
                    g.DrawImageUnscaled(fitted, (Width - fitted.Width) / 2, (Height - fitted.Height) / 2);
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

    internal static void Present(IntPtr window, Bitmap bitmap)
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
                    var blend = new Blend { Alpha = 255, Format = 1 };
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
    internal WidgetMenu()
    {
        Renderer = new ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false };
        ForeColor = Color.WhiteSmoke;
        ShowImageMargin = false;
        ShowCheckMargin = true;
        ItemAdded += delegate(object sender, ToolStripItemEventArgs args)
        {
            args.Item.ForeColor = ForeColor;
            var item = args.Item as ToolStripMenuItem;
            if (item != null && item.HasDropDownItems)
            {
                item.DropDown.ForeColor = ForeColor;
                foreach (ToolStripItem child in item.DropDownItems) child.ForeColor = ForeColor;
            }
        };
    }

    sealed class DarkColors : ProfessionalColorTable
    {
        static readonly Color Surface = Color.FromArgb(30, 30, 33);
        static readonly Color Highlight = Color.FromArgb(54, 54, 59);
        static readonly Color Edge = Color.FromArgb(84, 84, 90);
        public override Color ToolStripDropDownBackground { get { return Surface; } }
        public override Color ImageMarginGradientBegin { get { return Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Surface; } }
        public override Color ImageMarginGradientEnd { get { return Surface; } }
        public override Color MenuBorder { get { return Edge; } }
        public override Color MenuItemBorder { get { return Edge; } }
        public override Color MenuItemSelected { get { return Highlight; } }
        public override Color MenuItemSelectedGradientBegin { get { return Highlight; } }
        public override Color MenuItemSelectedGradientEnd { get { return Highlight; } }
        public override Color MenuItemPressedGradientBegin { get { return Highlight; } }
        public override Color MenuItemPressedGradientMiddle { get { return Highlight; } }
        public override Color MenuItemPressedGradientEnd { get { return Highlight; } }
        public override Color CheckBackground { get { return Highlight; } }
        public override Color CheckSelectedBackground { get { return Highlight; } }
        public override Color CheckPressedBackground { get { return Highlight; } }
    }
}
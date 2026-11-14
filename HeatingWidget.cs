using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class HeatingWidget : Form
{
    internal const string Title = "Heizung";
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "SolarKachel", "heating.json");
    readonly Func<Settings> connection;
    readonly FlowLayoutPanel rows = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown,
        WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 8) };
    readonly Label status = new Label { Width = 292, Height = 28, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    Point dragOrigin, windowOrigin;
    bool dragArmed;
    ThermostatRow pressedRow;
    int pressedDirection;
    readonly Timer timer = new Timer { Interval = 15000 };
    readonly HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };
    string[] entities = new string[0];
    bool busy;
    Appearance style = new Appearance { IconSize = 28, MetricSpacing = 0, FramePadding = 10 };
    System.Collections.Generic.Dictionary<string, string> names = new System.Collections.Generic.Dictionary<string, string>();
    ThermostatRow[] thermostats { get { return rows.Controls.OfType<ThermostatRow>().ToArray(); } }
    Rectangle[] hits = new Rectangle[0];


    internal HeatingWidget(Func<Settings> connection)
    {
        this.connection = connection;
        Text = Title;
        Icon = WidgetIcon.Load();
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(250, 250, 250);
        ForeColor = Color.FromArgb(32, 32, 32);
        DoubleBuffered = true;
        ClientSize = new Size(316, 54);
        // Row controls keep the thermostat state; the shared layered surface draws the visible tile.
        rows.Controls.Add(status);
        MouseDown += BeginPointerAction;
        MouseMove += MoveTile;
        MouseCaptureChanged += delegate { dragArmed = false; pressedRow = null; };
        var menu = new WidgetMenu();
        menu.Items.Add("Einstellungen", null, async delegate { await SelectDevices(); });
        menu.Items.Add("Aktualisieren", null, async delegate { await RefreshDevice(); });
        menu.Items.Add("Schließen", null, delegate { Hide(); });
        ContextMenuStrip = menu;
        timer.Tick += async delegate { await RefreshDevice(); };
        VisibleChanged += async delegate
        {
            if (Visible) { timer.Start(); await RefreshDevice(); }
            else timer.Stop();
        };
        Shown += async delegate
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var serializer = new JavaScriptSerializer();
                    if (json.TrimStart().StartsWith("[")) entities = serializer.Deserialize<string[]>(json) ?? entities;
                    else
                    {
                        var saved = serializer.Deserialize<HeatingSettings>(json);
                        entities = saved.Entities ?? entities;
                        style = saved.Style ?? style;
                        names = saved.Names ?? names;

                    }
                }
            }
            catch (Exception) { status.Text = "Gespeicherte Thermostate konnten nicht gelesen werden."; return; }
            UpdateDevices(entities.Where(IsClimate).ToArray());
            if (entities.Length == 0) await SelectDevices();
            else await RefreshDevice();
        };
        FormClosed += delegate { rows.Dispose(); timer.Dispose(); client.Dispose(); menu.Dispose(); };
    }

    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x80000; return value; }
    }

    internal static Bitmap Preview(Appearance appearance, Door[] doors, float scale)
    {
        var labels = doors.Length == 0 ? new[] { "Heizung Wohnzimmer" } : doors.Select(door => door.Name).ToArray();
        return DrawTile(appearance, labels, labels.Select(_ => "20,5°").ToArray(),
            labels.Select(_ => "21°").ToArray(), labels.Select(_ => true).ToArray(),
            labels.Select(_ => true).ToArray(), scale, 0, new Rectangle[labels.Length * 2]);
    }

    void Render()
    {
        if (!Visible || !IsHandleCreated || IsDisposed) return;
        var list = thermostats;
        var captions = list.Select(row => row.NameLabel.Text).ToArray();
        if (captions.Length == 0) captions = new[] { "Thermostate auswählen" };
        if (status.Text.Length != 0) captions = captions.Concat(new[] { status.Text }).ToArray();
        hits = new Rectangle[captions.Length * 2];
        using (var image = DrawTile(style, captions, list.Select(row => row.Actual.Text).ToArray(),
            list.Select(row => row.Target.Text).ToArray(), list.Select(row => row.Minus.Enabled).ToArray(),
            list.Select(row => row.Plus.Enabled).ToArray(), LayeredTile.ScaleFor(this), 1, hits))
        {
            if (ClientSize != image.Size) ClientSize = image.Size;
            LayeredTile.Present(Handle, image);
        }
    }

    static Bitmap DrawTile(Appearance style, string[] captions, string[] actual, string[] target,
        bool[] minus, bool[] plus, float scale, int minimumAlpha, Rectangle[] hits)
    {
        int inset = (int)Math.Round(style.FramePadding * scale);
        int icon = Math.Max(1, (int)Math.Round(style.IconSize * scale));
        int gap = (int)Math.Round(style.MetricSpacing * scale);
        using (var font = new Font(style.Typeface ?? "Segoe UI", style.TextSize * scale))
        using (var bold = new Font(font, FontStyle.Bold))
        {
            int row = Math.Max(icon, font.Height + (int)Math.Round(5 * scale));
            int actualWidth = actual.Concat(new[] { "20,5°" })
                .Max(value => TextRenderer.MeasureText(value, font).Width);
            int targetWidth = target.Concat(new[] { "20,5°" })
                .Max(value => TextRenderer.MeasureText(value, bold, Size.Empty, TextFormatFlags.NoPadding).Width)
                + Math.Max(2, (int)Math.Round(2 * scale));
            int button = Math.Max(1, (int)Math.Round(13 * scale));
            int name = captions.Max(value => TextRenderer.MeasureText(value, font).Width);
            int width = 2 * inset + icon + actualWidth + targetWidth + name + button * 2 + 8;
            int height = 2 * inset + row * captions.Length + gap * (captions.Length - 1);
            var image = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(image))
            using (var path = new GraphicsPath())
            using (var background = new SolidBrush(Color.FromArgb(Math.Max(minimumAlpha, style.BackgroundOpacity * 255 / 100), Color.FromArgb(style.Background))))
            using (var text = new SolidBrush(Color.FromArgb(style.Text)))
            using (var muted = new SolidBrush(Color.FromArgb(100, Color.FromArgb(style.Text))))
            {
                int d = Math.Min(Math.Min(width, height), Math.Min((int)(27 * scale), Math.Max(1, inset * 2)));
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(width - d, 0, d, d, 270, 90);
                path.AddArc(width - d, height - d, d, d, 0, 90);
                path.AddArc(0, height - d, d, d, 90, 90);
                path.CloseFigure();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(background, path);
                g.TextRenderingHint = style.BackgroundOpacity == 100
                    ? System.Drawing.Text.TextRenderingHint.ClearTypeGridFit
                    : style.BackgroundOpacity == 0
                        ? System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit
                        : System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var left = new StringFormat { LineAlignment = StringAlignment.Center })
                for (int i = 0; i < captions.Length; i++)
                {
                    int y = inset + i * (row + gap);
                    int x = inset + icon + 4;
                    if (i < actual.Length)
                    {
                        var state = g.Save();
                        g.TranslateTransform(inset, y + (row - icon) / 2);
                        g.ScaleTransform(icon / 32f, icon / 32f);
                        DoorIcons.DrawHeatWave(g, Color.FromArgb(style.Icons));
                        g.Restore(state);
                        g.DrawString(actual[i], font, text, new Rectangle(x, y, actualWidth, row), left);
                        g.DrawString(captions[i], font, text, new Rectangle(x + actualWidth, y, name, row), left);
                        x += actualWidth + name;
                        hits[i * 2] = new Rectangle(x, y, button, row);
                        hits[i * 2 + 1] = new Rectangle(x + button + targetWidth, y, button, row);
                        g.DrawString("−", font, minus[i] ? text : muted, hits[i * 2], center);
                        g.DrawString(target[i], bold, text, new Rectangle(x + button, y, targetWidth, row), center);
                        g.DrawString("+", font, plus[i] ? text : muted, hits[i * 2 + 1], center);
                    }
                    else g.DrawString(captions[i], font, text, new Rectangle(inset, y, width - inset * 2, row), left);
                }
            }
            return image;
        }
    }

    void BeginPointerAction(object sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        int hit = Array.FindIndex(hits, bounds => bounds.Contains(args.Location));
        var currentRows = thermostats;
        pressedRow = hit >= 0 && hit / 2 < currentRows.Length ? currentRows[hit / 2] : null;
        pressedDirection = hit % 2 == 0 ? -1 : 1;
        dragArmed = pressedRow == null;
        dragOrigin = Cursor.Position;
        windowOrigin = Location;
    }

    void MoveTile(object sender, MouseEventArgs args)
    {
        if (!dragArmed || args.Button != MouseButtons.Left) return;
        Point pointer = Cursor.Position;
        Size threshold = SystemInformation.DragSize;
        if (Location == windowOrigin && Math.Abs(pointer.X - dragOrigin.X) < Math.Max(1, threshold.Width / 2) &&
            Math.Abs(pointer.Y - dragOrigin.Y) < Math.Max(1, threshold.Height / 2)) return;
        Location = new Point(windowOrigin.X + pointer.X - dragOrigin.X, windowOrigin.Y + pointer.Y - dragOrigin.Y);
    }

    protected override void WndProc(ref Message message)
    {
        // WinForms can release mouse capture before raising MouseUp.
        if (message.Msg == 0x0202) // WM_LBUTTONUP
        {
            var row = pressedRow;
            int direction = pressedDirection;
            dragArmed = false;
            pressedRow = null;
            base.WndProc(ref message);
            if (row != null && !row.IsDisposed)
                CommitClick(row, direction);
            return;
        }
        base.WndProc(ref message);
    }

    async void CommitClick(ThermostatRow row, int direction)
    {
        int index = Array.IndexOf(thermostats, row) * 2 + (direction > 0 ? 1 : 0);
        if (index >= 0 && index < hits.Length && hits[index].Contains(PointToClient(Cursor.Position)))
            await Step(row, direction);
    }

    sealed class Radiator : Control
    {
        protected override void OnPaint(PaintEventArgs args)
        {
            var g = args.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Width / 32f, Height / 32f);
            using (var pen = new Pen(Color.FromArgb(55, 55, 55), 1.8f) { LineJoin = LineJoin.Round })
            {
                g.DrawRectangle(pen, 4, 7, 23, 17);
                for (int x = 9; x <= 24; x += 5) g.DrawLine(pen, x, 10, x, 21);
                g.DrawLine(pen, 27, 12, 30, 12);
                g.DrawLine(pen, 8, 24, 8, 28);
                g.DrawLine(pen, 23, 24, 23, 28);
            }
        }
    }

    internal static Appearance SavedStyle()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            return json.TrimStart().StartsWith("[") ? null :
                new JavaScriptSerializer().Deserialize<HeatingSettings>(json).Style;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    internal static bool IsClimate(string entity)
    {
        return entity != null && System.Text.RegularExpressions.Regex.IsMatch(entity, @"^climate\.[a-z0-9_]+$");
    }

    void UpdateDevices(string[] selected)
    {
        entities = selected.Distinct().ToArray();
        foreach (Control old in rows.Controls.Cast<Control>().Where(control => control != status).ToArray())
            old.Dispose();
        foreach (string entity in entities)
        {
            var row = new ThermostatRow(entity, BackColor, ForeColor);
            if (names.ContainsKey(entity)) row.NameLabel.Text = names[entity];
            row.Minus.Click += async delegate { await Step(row, -1); };
            row.Plus.Click += async delegate { await Step(row, 1); };
            rows.Controls.Add(row);
        }
        SetStatus(entities.Length == 0 ? "Rechtsklick → Einstellungen → Thermostate auswählen" : "");
    }

    void SetStatus(string message)
    {
        status.Text = message;
        status.Visible = message.Length != 0;
        Render();
    }

    sealed class ThermostatRow : Panel
    {
        internal readonly string Entity;
        internal readonly Control Icon = new Radiator { Bounds = new Rectangle(0, 3, 21, 21) };
        internal readonly Label Actual = new Label { Text = "—", Bounds = new Rectangle(25, 0, 42, 26),
            TextAlign = ContentAlignment.MiddleLeft };
        internal readonly Label NameLabel = new Label { Bounds = new Rectangle(70, 0, 133, 26),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        internal readonly Label Target = new Label { Text = "—", Bounds = new Rectangle(227, 0, 42, 26),
            TextAlign = ContentAlignment.MiddleCenter };
        internal readonly Button Minus = new Button { Text = "−", Bounds = new Rectangle(204, 1, 23, 24) };
        internal readonly Button Plus = new Button { Text = "+", Bounds = new Rectangle(269, 1, 23, 24) };
        internal ClimateState Current;
        internal bool Pending, Reading;
        internal decimal? DesiredTemperature { get; set; }
        internal int ChangeVersion;
        internal DateTime LastClick;
        internal DateTime ReadAfter;

        internal ThermostatRow(string entity, Color background, Color foreground)
        {
            Entity = entity;
            Size = new Size(292, 26);
            Margin = new Padding(0, 0, 0, 1);
            BackColor = background;
            ForeColor = foreground;
            NameLabel.Text = entity;
            var emphasis = new Font("Segoe UI", 10, FontStyle.Bold);
            NameLabel.Font = Target.Font = emphasis;
            Disposed += delegate { emphasis.Dispose(); };
            foreach (var button in new[] { Minus, Plus })
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = background;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 232, 232);
                button.Cursor = Cursors.Hand;
                button.Enabled = false;
            }
            Minus.AccessibleName = "Solltemperatur senken";
            Plus.AccessibleName = "Solltemperatur erhöhen";
            Controls.AddRange(new Control[] { Icon, Actual, NameLabel, Minus, Target, Plus });
        }

        internal void Display()
        {
            var data = Current == null ? null : Current.attributes;
            Actual.Text = Format(data == null ? null : data.current_temperature);
            decimal? target = DesiredTemperature ?? (data == null ? null : data.temperature);
            Target.Text = Format(target);
            if (data != null && NameLabel.Text == Entity) NameLabel.Text = data.friendly_name ?? Entity;
            bool available = data != null && Current.state != "unavailable" &&
                Current.state != "unknown" && (data.supported_features & 1) != 0 &&
                data.temperature.HasValue && data.min_temp.HasValue && data.max_temp.HasValue;
            Minus.Enabled = available && target > data.min_temp;
            Plus.Enabled = available && target < data.max_temp;
        }
    }

    async Task SelectDevices()
    {
        var settings = connection();
        if (settings == null || busy) return;
        busy = true;
        try
        {
            var editor = new DoorWidget.DoorEditor(thermostats.Select(row => new Door
            {
                Entity = row.Entity, Name = row.NameLabel.Text, Open = "Ein", Closed = "Aus"
            }).ToArray(), connection, heating: true);
            using (var dialog = new Options(settings, new string[0], style, settings.Appearance, editor))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var selected = editor.Doors.GroupBy(door => door.Entity).Select(group => group.First()).ToArray();
                var saved = new HeatingSettings
                {
                    Entities = selected.Select(door => door.Entity).ToArray(),
                    Names = selected.ToDictionary(door => door.Entity, door => door.Name),
                    Style = dialog.SelectedAppearance
                };
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, new JavaScriptSerializer().Serialize(saved));
                    if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                    else File.Move(temp, FilePath);
                }
                finally { File.Delete(temp); }
                style = saved.Style;
                names = saved.Names;
                UpdateDevices(saved.Entities);
            }
        }
        catch (Exception) { if (!IsDisposed) SetStatus("Einstellungen konnten nicht gespeichert werden."); return; }
        finally { busy = false; }
        await RefreshDevice();
    }

    async Task<ClimateState> ReadState(Settings settings, string entity)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get,
            settings.Url.TrimEnd('/') + "/api/states/" + Uri.EscapeDataString(entity)))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadToken());
            using (var response = await client.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                return new JavaScriptSerializer().Deserialize<ClimateState>(await response.Content.ReadAsStringAsync());
            }
        }
    }

    async Task RefreshDevice()
    {
        var settings = connection();
        if (busy || settings == null || !Visible || IsDisposed) return;
        await Task.WhenAll(rows.Controls.OfType<ThermostatRow>().Select(row => RefreshRow(row, settings)).ToArray());
    }

    async Task RefreshRow(ThermostatRow row, Settings settings)
    {
        if (row.Pending || row.Reading || row.IsDisposed || DateTime.UtcNow < row.ReadAfter) return;
        int version = row.ChangeVersion;
        row.Reading = true;
        try
        {
            var state = await ReadState(settings, row.Entity);
            if (!row.IsDisposed && !row.Pending && version == row.ChangeVersion) row.Current = state;
        }
        catch (Exception) { if (!row.IsDisposed && !row.Pending && version == row.ChangeVersion) row.Current = null; }
        finally
        {
            row.Reading = false;
            if (!row.IsDisposed) { row.Display(); Render(); }
        }
    }

    async Task Step(ThermostatRow row, int direction)
    {
        var settings = connection();
        if (busy || settings == null || !(direction < 0 ? row.Minus.Enabled : row.Plus.Enabled)) return;
        var data = row.Current.attributes;
        decimal step = data.target_temp_step.GetValueOrDefault() > 0 ? data.target_temp_step.Value : 0.5m;
        decimal start = row.DesiredTemperature ?? data.temperature.Value;
        row.DesiredTemperature = Math.Min(data.max_temp.Value, Math.Max(data.min_temp.Value, start + direction * step));
        row.LastClick = DateTime.UtcNow;
        row.ChangeVersion++;
        row.Display();
        Render();
        if (row.Pending) return;

        row.Pending = true;
        bool failed = false;
        try
        {
            while (!row.IsDisposed && !IsDisposed)
            {
                var delay = TimeSpan.FromMilliseconds(250) - (DateTime.UtcNow - row.LastClick);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay);
                    continue;
                }

                int version = row.ChangeVersion;
                decimal temperature = row.DesiredTemperature.Value;
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post,
                        settings.Url.TrimEnd('/') + "/api/services/climate/set_temperature"))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadToken());
                        request.Content = new StringContent(new JavaScriptSerializer().Serialize(new { entity_id = row.Entity, temperature }),
                            System.Text.Encoding.UTF8, "application/json");
                        using (var response = await client.SendAsync(request)) response.EnsureSuccessStatusCode();
                    }
                    data.temperature = temperature;
                    row.ReadAfter = DateTime.UtcNow.AddSeconds(2);
                    failed = false;
                }
                catch (Exception) { failed = true; }
                if (version == row.ChangeVersion) break;
            }
        }
        finally
        {
            row.Pending = false;
            row.DesiredTemperature = null;
        }
        if (IsDisposed || row.IsDisposed) return;
        row.Display();
        SetStatus(failed ? "Solltemperatur konnte nicht geändert werden." : "");
        if (failed) await RefreshRow(row, settings);
    }

    public sealed class HeatingSettings
    {
        public string[] Entities { get; set; }
        public Appearance Style { get; set; }
        public System.Collections.Generic.Dictionary<string, string> Names { get; set; }
    }

    static string Format(decimal? temperature)
    {
        return temperature.HasValue ? temperature.Value.ToString("0.##", CultureInfo.CurrentCulture) + "°" : "—";
    }

    public sealed class ClimateState
    {
        public string state { get; set; }
        public ClimateAttributes attributes { get; set; }
    }

    public sealed class ClimateAttributes
    {
        public string friendly_name { get; set; }
        public decimal? current_temperature { get; set; }
        public decimal? temperature { get; set; }
        public decimal? min_temp { get; set; }
        public decimal? max_temp { get; set; }
        public decimal? target_temp_step { get; set; }
        public int supported_features { get; set; }
    }
}

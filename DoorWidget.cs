using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class Door
{
    public string Entity { get; set; }
    public string IconKind { get; set; }
    public int? Decimals { get; set; }
    public string Unit { get; set; }

    internal string ResolvedIcon
    {
        get
        {
            if (IconKind == "Schalter" || IconKind == "Tür" || IconKind == "Garagentor" || IconKind == "Fenster") return IconKind;
            string hint = (Name ?? "") + " " + (Entity ?? "");
            if (hint.IndexOf("garagentor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                hint.IndexOf("garage_door", StringComparison.OrdinalIgnoreCase) >= 0) return "Garagentor";
            if (hint.IndexOf("fenster", StringComparison.OrdinalIgnoreCase) >= 0 ||
                hint.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0) return "Fenster";
            return "Tür";
        }
    }
    public string Name { get; set; }
    public string Open { get; set; }
    public string Closed { get; set; }

    internal string Describe(string state)
    {
        switch (state)
        {
            case "on":
            case "open": return Open;
            case "off":
            case "closed": return Closed;
            case "playing": return "Wiedergabe";
            case "paused": return "Pause";
            case "idle": return "Bereit";
            case "standby": return "Standby";
            case "opening": return "Öffnet";
            case "closing": return "Schließt";
            default: return "Nicht verfügbar";
        }
    }
}

sealed class DoorPosition
{
    public int X { get; set; }
    public int Y { get; set; }
}

sealed class DoorSettings
{
    public string Name { get; set; }
    public Door[] Doors { get; set; }
    public DoorPosition Position { get; set; }
    public Appearance Appearance { get; set; }
}

sealed class DoorWidget : Form
{
    static readonly Color Surface = Color.FromArgb(24, 24, 26);
    static readonly Color Input = Color.FromArgb(43, 43, 47);
    internal const string DoorTitle = "Fenster-Tür-Sensoren";
    internal const string SwitchTitle = "Schalter";
    readonly string configPath;
    readonly bool switches;
    readonly string sensorId;
    bool IsSensorWidget { get { return sensorId != null; } }
    internal const string SensorTitle = "Sensoren";
    internal string WidgetName { get { return config.Name ?? SensorTitle; } }

    static string SensorFolder
    {
        get { return Path.Combine(Path.GetDirectoryName(ConfigPath(false)), "sensor-widgets"); }
    }

    internal static bool IsSensorId(string id)
    {
        Guid value;
        return Guid.TryParseExact(id, "N", out value);
    }

    internal static System.Collections.Generic.Dictionary<string, string> SensorWidgets()
    {
        var result = new System.Collections.Generic.Dictionary<string, string>();
        if (!Directory.Exists(SensorFolder)) return result;
        foreach (string file in Directory.GetFiles(SensorFolder, "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            if (!IsSensorId(id)) continue;
            try
            {
                var saved = new JavaScriptSerializer().Deserialize<DoorSettings>(File.ReadAllText(file));
                if (saved != null) result[id] = saved.Name ?? SensorTitle;
            }
            catch (Exception) { }
        }
        return result;
    }

    internal static Appearance SensorStyle(string id)
    {
        if (!IsSensorId(id)) return null;
        try
        {
            return new JavaScriptSerializer().Deserialize<DoorSettings>(
                File.ReadAllText(Path.Combine(SensorFolder, id + ".json"))).Appearance;
        }
        catch (Exception) { return null; }
    }
    internal static string ConfigPath(bool switches)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ReleaseVersion.DataFolder, switches ? "switches.json" : "doors.json");
    }

    internal static Appearance SavedStyle(bool switches)
    {
        try
        {
            var path = ConfigPath(switches);
            return File.Exists(path)
                ? new JavaScriptSerializer().Deserialize<DoorSettings>(File.ReadAllText(path)).Appearance : null;
        }
        catch (Exception) { return null; }
    }

    internal static bool IsSwitchEntity(string entity)
    {
        return Regex.IsMatch(entity, @"^(switch|light|input_boolean|fan|binary_sensor|media_player|scene|script)\.[a-z0-9_]+$");
    }
    readonly Func<Settings> connection;
    Point? dragStart;
    Point windowStart;
    readonly Timer timer = new Timer { Interval = 15000 };
    readonly HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };
    DoorSettings config = new DoorSettings { Doors = new Door[0] };
    bool loading;
    bool switching;
    RectangleF[] switchBounds = new RectangleF[0];
    int pressedSwitch = -1;
    string[] lastStates;
    SolarTile.Sensor[] deviceStates;

    static bool HasDoors(DoorSettings saved)
    {
        return saved != null && saved.Doors != null && saved.Doors.Length > 0;
    }

    internal static bool HasSavedDoors()
    {
        try
        {
            return File.Exists(ConfigPath(false)) &&
                HasDoors(new JavaScriptSerializer().Deserialize<DoorSettings>(File.ReadAllText(ConfigPath(false))));
        }
        catch (Exception) { return false; }
    }

    internal DoorWidget(Func<Settings> connection, bool topMost, bool switches = false, string sensorId = null)
    {
        this.connection = connection;
        this.switches = switches;
        this.sensorId = sensorId;
        configPath = IsSensorWidget ? Path.Combine(SensorFolder, sensorId + ".json") : ConfigPath(switches);
        Text = switches ? SwitchTitle : DoorTitle;
        Icon = WidgetIcon.Load();
        Font = new Font("Segoe UI", 10);
        BackColor = Surface;
        ForeColor = Color.WhiteSmoke;
        TopMost = topMost;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        var menu = new WidgetMenu();
        menu.Items.Add(switches ? "Schalter bearbeiten…" : "Sensoren bearbeiten…", null, delegate { EditDoors(); });
        menu.Items.Add("Aktualisieren", null, async delegate { await RefreshDoors(); });
        menu.Items.Add("Schließen", null, delegate { Hide(); });
        ContextMenuStrip = menu;
        if (switches) menu.Opening += delegate { PopulateDeviceMenu(menu); };
        try
        {
            if (File.Exists(configPath))
                config = new JavaScriptSerializer().Deserialize<DoorSettings>(File.ReadAllText(configPath)) ?? config;
        }
        catch (Exception) { }
        if (config.Doors == null) config.Doors = new Door[0];
        VisibleChanged += async delegate
        {
            if (!Visible) { timer.Stop(); return; }
            timer.Start();
            await RefreshDoors();
        };
        if (config.Position != null)
        {
            Point point = new Point(config.Position.X, config.Position.Y);
            Rectangle area = Screen.FromPoint(point).WorkingArea;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Math.Max(area.Left, Math.Min(point.X, area.Right - Width)),
                Math.Max(area.Top, Math.Min(point.Y, area.Bottom - Height)));
        }
        else StartPosition = FormStartPosition.CenterParent;
        Shown += async delegate
        {
            Render(config.Doors, null);
            if (config.Position != null)
            {
                Point point = new Point(config.Position.X, config.Position.Y);
                Rectangle area = Screen.FromPoint(point).WorkingArea;
                Location = new Point(Math.Max(area.Left, Math.Min(point.X, area.Right - Width)),
                    Math.Max(area.Top, Math.Min(point.Y, area.Bottom - Height)));
            }
            else if (Owner != null) Location = new Point(Owner.Left, Owner.Bottom + 10);
            if (config.Doors.Length == 0) EditDoors();
            else await RefreshDoors();
        };
        timer.Tick += async delegate { await RefreshDoors(); };
        FormClosed += delegate
        {
            timer.Dispose();
            client.Dispose();
            menu.Dispose();
        };
        MouseDown += delegate(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
            pressedSwitch = HitSwitch(args.Location);
            dragStart = Cursor.Position;
            windowStart = Location;
            Capture = true;
        };
        MouseMove += delegate
        {
            if (!dragStart.HasValue || !Capture || pressedSwitch >= 0) return;
            var start = dragStart.Value;
            Location = new Point(windowStart.X + Cursor.Position.X - start.X, windowStart.Y + Cursor.Position.Y - start.Y);
        };
        MouseUp += async delegate(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left || !dragStart.HasValue) return;
            dragStart = null;
            Capture = false;
            int index = pressedSwitch;
            pressedSwitch = -1;
            if (index >= 0)
            {
                if (HitSwitch(args.Location) == index) await ToggleSwitch(index);
                return;
            }
            if (Location == windowStart) return;
            try { Save(config.Doors, config.Appearance); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }

    int HitSwitch(Point point)
    {
        if (!switches) return -1;
        return Array.FindIndex(switchBounds, bounds => bounds.Contains(point));
    }

    int DeviceAt(Point point)
    {
        var style = config.Appearance ?? new Appearance();
        for (int i = Math.Min(config.Doors.Length, switchBounds.Length) - 1; i >= 0; i--)
        {
            var bounds = switchBounds[i];
            if (style.Vertical ? point.Y >= bounds.Top : point.X >= bounds.Left) return i;
        }
        return -1;
    }

    void PopulateDeviceMenu(ContextMenuStrip menu)
    {
        const string key = "DeviceActions";
        if (menu.Items.ContainsKey(key)) menu.Items[key].Dispose();
        int index = DeviceAt(PointToClient(Cursor.Position));
        if (index < 0) return;
        var device = config.Doors[index];
        var options = DeviceActions(index);
        if (options.Count == 0) return;
        var actions = new ToolStripMenuItem(device.Name) { Name = key, ForeColor = Color.WhiteSmoke };
        actions.DropDownItems.AddRange(options.ToArray());
        menu.Items.Insert(0, actions);
    }

    System.Collections.Generic.List<ToolStripItem> DeviceActions(int index)
    {
        var items = new System.Collections.Generic.List<ToolStripItem>();
        if (switching || index < 0 || index >= config.Doors.Length) return items;
        var door = config.Doors[index];
        string entity = door.Entity;
        string domain = entity.Split('.')[0];
        var sensor = deviceStates != null && index < deviceStates.Length ? deviceStates[index] : null;
        if (sensor == null || sensor.state == "unavailable" || sensor.state == "unknown") return items;
        int features = sensor.attributes == null ? 0 : sensor.attributes.supported_features;
        Action<ToolStripItemCollection, string, string, object> add = (list, label, action, payload) =>
        {
            var item = new ToolStripMenuItem(label) { ForeColor = Color.WhiteSmoke };
            item.Click += async delegate { await SendDeviceAction(entity, action, payload); };
            list.Add(item);
        };
        using (var group = new ToolStripMenuItem())
        {
            if (domain == "scene" || domain == "script")
                add(group.DropDownItems, domain == "scene" ? "Szene aktivieren" : "Skript starten", "turn_on", new { entity_id = entity });
            if (domain == "light" || domain == "fan" || domain == "switch" || domain == "input_boolean")
            {
                string action = sensor.state == "on" ? "turn_off" : "turn_on";
                add(group.DropDownItems, sensor.state == "on" ? "Ausschalten" : "Einschalten", action, new { entity_id = entity });
            }
            var modes = sensor.attributes == null ? null : sensor.attributes.supported_color_modes;
            if (domain == "light" && modes != null && modes.Any(mode => mode != "onoff" && mode != "unknown"))
                group.DropDownItems.Add(LevelMenu("Helligkeit", value => SendDeviceAction(entity, "turn_on", new { entity_id = entity, brightness_pct = value })));
            if (domain == "fan" && (features & 1) != 0)
                group.DropDownItems.Add(LevelMenu("Lüfterstufe", value => SendDeviceAction(entity, "set_percentage", new { entity_id = entity, percentage = value })));
            if (domain == "media_player")
            {
                if ((features & 16384) != 0) add(group.DropDownItems, "Wiedergabe", "media_play", new { entity_id = entity });
                if ((features & 1) != 0) add(group.DropDownItems, "Pause", "media_pause", new { entity_id = entity });
                if ((features & 4096) != 0) add(group.DropDownItems, "Stopp", "media_stop", new { entity_id = entity });
                if ((features & 16) != 0) add(group.DropDownItems, "Vorheriger Titel", "media_previous_track", new { entity_id = entity });
                if ((features & 32) != 0) add(group.DropDownItems, "Nächster Titel", "media_next_track", new { entity_id = entity });
                if ((features & 128) != 0) add(group.DropDownItems, "Einschalten", "turn_on", new { entity_id = entity });
                if ((features & 256) != 0) add(group.DropDownItems, "Ausschalten", "turn_off", new { entity_id = entity });
                if ((features & 4) != 0)
                    group.DropDownItems.Add(LevelMenu("Lautstärke", value => SendDeviceAction(entity, "volume_set", new { entity_id = entity, volume_level = value / 100m })));
            }
            items.AddRange(group.DropDownItems.Cast<ToolStripItem>());
            group.DropDownItems.Clear();
        }
        return items;
    }

    static ToolStripMenuItem LevelMenu(string label, Func<int, Task> apply)
    {
        var menu = new ToolStripMenuItem(label) { ForeColor = Color.WhiteSmoke };
        var level = new ToolStripTextBox { AccessibleName = label + " in Prozent", MaxLength = 3, Width = 65,
            BackColor = Input, ForeColor = Color.WhiteSmoke, ToolTipText = "0–100 eingeben, mit Enter bestätigen" };
        Action submit = async delegate
        {
            int value;
            if (!int.TryParse(level.Text, out value) || value < 0 || value > 100) return;
            menu.DropDown.Close();
            await apply(value);
        };
        level.KeyDown += delegate(object sender, KeyEventArgs args)
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            submit();
        };
        menu.DropDownItems.Add(level);
        menu.DropDownItems.Add("Übernehmen (%)", null, delegate { submit(); }).ForeColor = Color.WhiteSmoke;
        return menu;
    }

    async Task ToggleSwitch(int index)
    {
        if (switching || index >= config.Doors.Length || lastStates == null) return;
        string state = lastStates[index];
        string entity = config.Doors[index].Entity;
        string domain = entity.Split('.')[0];
        if (domain == "media_player")
        {
            var controls = new WidgetMenu();
            controls.Items.AddRange(DeviceActions(index).ToArray());
            if (controls.Items.Count == 0) { controls.Dispose(); return; }
            controls.Closed += delegate { BeginInvoke(new Action(controls.Dispose)); };
            controls.Show(Cursor.Position);
            return;
        }
        if (domain == "scene" || domain == "script")
        {
            if (state != null && state != "unavailable" && state != "unknown")
                await SendDeviceAction(entity, "turn_on", new { entity_id = entity });
            return;
        }
        if (state != "on" && state != "off") return;
        if (domain != "switch" && domain != "light" && domain != "input_boolean" && domain != "fan") return;
        await SendDeviceAction(entity, state == "on" ? "turn_off" : "turn_on", new { entity_id = entity });
    }

    async Task SendDeviceAction(string entity, string service, object payload)
    {
        if (switching || IsDisposed || !config.Doors.Any(door => door.Entity == entity)) return;
        var settings = connection();
        if (settings == null) return;
        switching = true;
        try
        {
            string url = settings.Url.TrimEnd('/') + "/api/services/" + entity.Split('.')[0] + "/" + service;
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadToken());
                request.Content = new StringContent(new JavaScriptSerializer().Serialize(payload),
                    System.Text.Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request)) response.EnsureSuccessStatusCode();
            }
        }
        catch (Exception)
        {
            if (!IsDisposed) MessageBox.Show(this, "Home Assistant konnte die Aktion nicht ausführen.", Text);
        }
        finally { switching = false; }
        if (!IsDisposed) await RefreshDoors();
    }

    void Save(Door[] doors, Appearance appearance, string name = null)
    {
        var next = new DoorSettings { Name = name ?? config.Name, Doors = doors, Position = new DoorPosition { X = Left, Y = Top }, Appearance = appearance };
        Directory.CreateDirectory(Path.GetDirectoryName(configPath));
        string temp = configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(next));
            if (File.Exists(configPath)) File.Replace(temp, configPath, configPath + ".bak");
            else File.Move(temp, configPath);
            config = next;
        }
        finally { File.Delete(temp); }
    }

    async void EditDoors()
    {
        var settings = connection();
        var editor = new DoorEditor(config.Doors, connection, switches, sensors: IsSensorWidget, widgetName: WidgetName);
        using (var dialog = new Options(settings, new string[0], config.Appearance ?? new Appearance(),
            settings == null ? null : settings.Appearance, editor))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                if (IsSensorWidget && config.Doors.Length == 0) Close();
                return;
            }
            try { Save(editor.Doors, dialog.SelectedAppearance, IsSensorWidget ? editor.WidgetName : null); }
            catch (Exception)
            {
                MessageBox.Show(this, "Die Einstellungen konnten nicht gespeichert werden.", Text);
                return;
            }
        }
        Render(config.Doors, null);
        await RefreshDoors();
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

    void Render(Door[] doors, string[] states)
    {
        lastStates = states;
        if (states == null) deviceStates = null;
        switchBounds = new RectangleF[switches ? Math.Max(1, doors.Length) : 0];
        var names = doors.Length == 0 ? new[] { IsSensorWidget ? WidgetName : switches ? SwitchTitle : DoorTitle } : doors.Select(door => door.Name).ToArray();
        var values = doors.Length == 0 ? new[] { switches ? "Schalter hinzufügen" : "Sensoren hinzufügen" } : doors.Select((door, index) =>
            states == null ? "—" : IsSensorWidget ? states[index] ?? "—" : door.Describe(states[index])).ToArray();
        var opened = doors.Select((door, index) => states != null &&
            (states[index] == "on" || states[index] == "playing" || states[index] == "open" || states[index] == "opening")).ToArray();
        if (opened.Length == 0) opened = new bool[1];
        if (switches && doors.Length == 0) doors = new[] { new Door { IconKind = "Schalter" } };
        float scale = LayeredTile.ScaleFor(this);
        using (var bitmap = IsSensorWidget
            ? PaintSensors(config.Appearance ?? new Appearance(), doors, names, values, scale, 1)
            : PaintDoors(config.Appearance ?? new Appearance(), names, values, opened, scale, 1, doors, switches ? switchBounds : null))
        {
            if (ClientSize != bitmap.Size) ClientSize = bitmap.Size;
            LayeredTile.Present(Handle, bitmap);
        }
        AccessibleName = Text = (IsSensorWidget ? WidgetName : switches ? SwitchTitle : DoorTitle) + ": " + string.Join(", ", names.Zip(values, (name, state) => name + " " + state));
    }

    internal static Bitmap PaintDoors(Appearance style, string[] names, string[] values, bool[] opened, float scale, int alpha, Door[] doors = null, RectangleF[] bounds = null)
    {
        return SolarTile.PaintTile(style, values, scale, alpha, new float[names.Length], names, (graphics, index) =>
        {
            Color color = Color.FromArgb(opened[index] ? style.OpenIconColor : style.Icons);
            string kind = doors != null && index < doors.Length ? doors[index].ResolvedIcon : "Tür";
            DoorIcons.Draw(graphics, kind, opened[index], color);
        }, doors != null && doors.Length > 0 && doors[0].IconKind == "Schalter", bounds);
    }

    internal static Bitmap PaintSensors(Appearance style, Door[] sensors, string[] captions, string[] values, float scale, int alpha)
    {
        return SolarTile.PaintTile(style, values, scale, alpha, new float[captions.Length], captions, (g, i) =>
        {
            string icon = i < sensors.Length ? sensors[i].IconKind : null;
            Color color = Color.FromArgb(style.Icons);
            switch (icon)
            {
                case "Solar": SolarIcon.Draw(g, 0, color); break;
                case "Akku": SolarIcon.Draw(g, 1, color); break;
                case "Haus": SolarIcon.Draw(g, 2, color); break;
                case "Strom": SolarIcon.Draw(g, 3, color); break;
                case "Luftfeuchtigkeit":
                    using (var drop = new GraphicsPath())
                    using (var pen = new Pen(color, 2f) { LineJoin = LineJoin.Round })
                    {
                        drop.AddBezier(16, 3, 13, 9, 7, 13, 7, 20);
                        drop.AddBezier(7, 20, 7, 31, 25, 31, 25, 20);
                        drop.AddBezier(25, 20, 25, 13, 19, 9, 16, 3);
                        drop.CloseFigure();
                        g.DrawPath(pen, drop);
                    }
                    break;
                case "Temperatur": DoorIcons.DrawHeatWave(g, color); break;
                default:
                    using (var pen = new Pen(color, 2))
                    {
                        g.DrawEllipse(pen, 5, 5, 22, 22);
                        g.DrawLine(pen, 16, 16, 22, 10);
                    }
                    break;
            }
        }, valueBesideName: style.ValueBesideName);
    }

    internal static bool IsSensor(string entity)
    {
        return entity != null && Regex.IsMatch(entity, @"^(sensor|binary_sensor)\.[a-z0-9_]+$");
    }

    internal static string SensorValue(SolarTile.Sensor state, int? decimals, string unitOverride = null)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.state) ||
            state.state == "unknown" || state.state == "unavailable") return "—";
        decimal value;
        string output = state.state;
        if (decimal.TryParse(output, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value))
            output = value.ToString(decimals.HasValue ? "F" + decimals.Value : "0.############################",
                System.Globalization.CultureInfo.CurrentCulture);
        else if (output == "on" || output == "off") output = output == "on" ? "Ein" : "Aus";
        var unit = string.IsNullOrWhiteSpace(unitOverride)
            ? (state.attributes == null ? null : state.attributes.unit_of_measurement) : unitOverride;
        return string.IsNullOrWhiteSpace(unit) ? output : output + " " + unit;
    }

    bool refreshQueued;

    internal Task RefreshLive(string[] changes)
    {
        if (!Visible || !changes.Any(entity => entity == null || config.Doors.Any(door => door.Entity == entity)))
            return Task.FromResult(0);
        return RefreshDoors(true);
    }

    async Task RefreshDoors(bool live = false)
    {
        if (IsDisposed || !Visible || config.Doors.Length == 0) return;
        if (loading || switching) { refreshQueued |= live; return; }
        var doors = config.Doors;
        var settings = connection();
        if (settings == null) return;
        loading = true;
        try
        {
            string token = settings.ReadToken();
            var received = await Task.WhenAll(doors.Select(door => ReadState(door, settings.Url, token)));
            if (!IsDisposed && doors == config.Doors && !switching && connection() == settings)
            {
                deviceStates = received;
                Render(doors, received.Select((state, index) => IsSensorWidget
                    ? SensorValue(state, doors[index].Decimals, doors[index].Unit) : state == null ? null : state.state).ToArray());
            }
        }
        catch (Exception)
        {
            if (!IsDisposed && doors == config.Doors) Render(doors, null);
        }
        finally { loading = false; }
        if (!IsDisposed && (doors != config.Doors || refreshQueued))
        {
            refreshQueued = false;
            await RefreshDoors();
        }
    }

    async Task<SolarTile.Sensor> ReadState(Door door, string url, string token)
    {
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url.TrimEnd('/') + "/api/states/" + Uri.EscapeDataString(door.Entity)))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await client.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    var state = new JavaScriptSerializer().Deserialize<SolarTile.Sensor>(await response.Content.ReadAsStringAsync());
                    return state;
                }
            }
        }
        catch (Exception) { return null; }
    }

    internal sealed class DoorEditor : UserControl
    {
        readonly DataGridView grid = new DataGridView();
        readonly Func<Settings> connection;
        internal Door[] Doors { get; private set; }
        internal event EventHandler RowsChanged;

        internal readonly bool Switches;
        internal readonly bool Heating;
        internal readonly bool Cover;
        internal readonly bool Sensors;
        readonly TextBox widgetNameInput = new TextBox();
        internal string WidgetName { get { return widgetNameInput.Text.Trim(); } }
        internal bool DeviceRows { get { return Heating || Cover; } }
        internal DoorEditor(Door[] doors, Func<Settings> connection, bool switches = false, bool heating = false, bool cover = false, bool sensors = false, string widgetName = null)
        {
            Heating = heating;
            Cover = cover;
            Sensors = sensors;
            this.connection = connection;
            Switches = switches;
            Font = new Font("Segoe UI", 10);
            BackColor = Surface;
            ForeColor = Color.WhiteSmoke;
            grid.Bounds = new Rectangle(0, 0, 644, 183);
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = Input;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Input;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
            grid.DefaultCellStyle.BackColor = Input;
            grid.DefaultCellStyle.ForeColor = ForeColor;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add("entity", "Entitäts-ID");
            grid.Columns[0].FillWeight = 185;
            grid.Columns.Add("name", "Name");
            grid.Columns.Add("open", switches ? "Text bei ein" : "Text bei offen");
            grid.Columns.Add("closed", switches ? "Text bei aus" : "Text bei geschlossen");
            var icons = new DataGridViewComboBoxColumn { HeaderText = "Icon", FlatStyle = FlatStyle.Flat };
            icons.Items.AddRange(switches ? new object[] { "Schalter" } : new object[] { "Automatisch", "Tür", "Garagentor", "Fenster" });
            icons.ReadOnly = switches;
            grid.Columns.Add(icons);
            icons.MinimumWidth = 94;
            icons.FillWeight = 84;
            grid.Columns[1].FillWeight = 95;
            grid.Columns[2].FillWeight = 82;
            grid.Columns[3].FillWeight = 105;
            grid.CellClick += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex < 0 || args.ColumnIndex != icons.Index) return;
                grid.BeginEdit(true);
                var picker = grid.EditingControl as ComboBox;
                if (picker != null)
                {
                    picker.BackColor = Input;
                    picker.ForeColor = ForeColor;
                    picker.FlatStyle = FlatStyle.Flat;
                    picker.DroppedDown = true;
                }
            };
            if (Sensors)
            {
                grid.Columns[2].HeaderText = "Dezimalstellen";
                grid.Columns[3].HeaderText = "Einheit";
                grid.Columns[3].FillWeight = 60;

                icons.Items.Clear();
                icons.Items.AddRange("Messwert", "Temperatur", "Luftfeuchtigkeit", "Solar", "Akku", "Haus", "Strom");
                widgetNameInput.Text = widgetName ?? SensorTitle;
                widgetNameInput.Bounds = new Rectangle(80, 0, 564, 24);
                widgetNameInput.BackColor = Input;
                widgetNameInput.ForeColor = ForeColor;
                widgetNameInput.MaxLength = 60;
                widgetNameInput.AccessibleName = "Widget-Name";
                Controls.Add(new Label { Text = "Name", Bounds = new Rectangle(0, 3, 72, 22) });
                Controls.Add(widgetNameInput);
                grid.Top = 29;
                grid.Height -= 29;
            }
            if (DeviceRows) { grid.Columns[2].Visible = false; grid.Columns[3].Visible = false; icons.Visible = false; }
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (var door in doors)
                grid.Rows.Add(door.Entity, door.Name, Sensors ? door.Decimals.ToString() : door.Open, Sensors ? door.Unit : door.Closed, Sensors ? door.IconKind ?? "Messwert" : switches ? "Schalter" : door.IconKind ?? "Automatisch");

            var add = Button("Sensor auswählen…", 0, 151);
            var manual = Button("Manuell hinzufügen", 157, 150);
            var remove = Button("Entfernen", 313, 86);
            var up = Button(DeviceRows ? "↑ Nach oben" : "↑ Nach links", 405, 109);
            var down = Button(DeviceRows ? "↓ Nach unten" : "↓ Nach rechts", 520, 124);
            add.Click += async delegate { await SelectDoor(add); };
            manual.Click += delegate { grid.Rows.Add("", "", Sensors ? "" : Switches ? "Ein" : "Offen", Sensors ? "" : Switches ? "Aus" : "Geschlossen", Sensors ? "Messwert" : Switches ? "Schalter" : "Automatisch"); };
            remove.Click += delegate { if (grid.CurrentRow != null) grid.Rows.Remove(grid.CurrentRow); };
            up.Click += delegate { MoveRow(-1); };
            down.Click += delegate { MoveRow(1); };
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += delegate { NotifyRows(); };
            grid.RowsAdded += delegate { NotifyRows(); };
            grid.RowsRemoved += delegate { NotifyRows(); };
            Controls.Add(grid);
        }

        void NotifyRows()
        {
            var changed = RowsChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }

        void MoveRow(int offset)
        {
            grid.EndEdit();
            var row = grid.CurrentRow;
            if (row == null) return;
            int target = row.Index + offset;
            if (target < 0 || target >= grid.Rows.Count) return;
            grid.Rows.Remove(row);
            grid.Rows.Insert(target, row);
            grid.CurrentCell = row.Cells[0];
        }

        Button Button(string text, int left, int width)
        {
            var button = new Button { Text = text, Bounds = new Rectangle(left, 190, width, 29),
                BackColor = Input, ForeColor = ForeColor, FlatStyle = FlatStyle.Flat };
            button.FlatAppearance.BorderColor = Color.DimGray;
            Controls.Add(button);
            return button;
        }

        async Task SelectDoor(Button add)
        {
            add.Enabled = false;
            try
            {
                var settings = connection();
                if (settings == null) throw new ArgumentException("Bitte zuerst Home Assistant einrichten.");
                var sensors = await Options.LoadSensors(new Uri(settings.Url), settings.ReadToken());
                if (IsDisposed) return;
                using (var picker = new Options.SensorPicker(sensors, Sensors ? "any_sensor" : Cover ? "window_cover" : Heating ? "climate" : Switches ? "switch" : null, ""))
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    var sensor = picker.Selection;
                    grid.Rows.Add(sensor.entity_id, sensor.attributes.friendly_name ?? sensor.entity_id, Sensors ? "" : Switches ? "Ein" : "Offen", Sensors ? "" : Switches ? "Aus" : "Geschlossen",
                        Sensors ? (sensor.attributes.device_class == "humidity" ? "Luftfeuchtigkeit" : "Messwert") : Switches ? "Schalter" :
                        sensor.attributes.device_class == "garage_door" ? "Garagentor" :
                        sensor.attributes.device_class == "window" ? "Fenster" : "Automatisch");
                }
            }
            catch (ArgumentException ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, Text); }
            catch (Exception) { if (!IsDisposed) MessageBox.Show(this, "Die Türsensoren konnten nicht geladen werden.", Text); }
            finally { if (!add.IsDisposed) add.Enabled = true; }
        }

        static int? ReadDecimals(object value)
        {
            int result;
            return int.TryParse(Convert.ToString(value), out result) && result >= 0 && result <= 6
                ? (int?)result : null;
        }

        internal Door[] ReadRows()
        {
            return grid.Rows.Cast<DataGridViewRow>().Select(row => new Door
            {
                Entity = Convert.ToString(row.Cells[0].Value).Trim().ToLowerInvariant(),
                Name = Convert.ToString(row.Cells[1].Value).Trim(),
                Open = Convert.ToString(row.Cells[2].Value).Trim(),
                Closed = Convert.ToString(row.Cells[3].Value).Trim(),
                IconKind = Convert.ToString(row.Cells[4].Value),
                Decimals = Sensors ? ReadDecimals(row.Cells[2].Value) : null,
                Unit = Sensors ? Convert.ToString(row.Cells[3].Value).Trim() : null
            }).ToArray();
        }

        internal bool SaveRows()
        {
            grid.EndEdit();
            var doors = ReadRows();
            if (Sensors && (WidgetName.Length == 0 || doors.Length == 0 ||
                doors.Any(door => !IsSensor(door.Entity) || door.Name.Length == 0 ||
                    (!string.IsNullOrWhiteSpace(door.Open) && !door.Decimals.HasValue))))
            {
                MessageBox.Show(this, "Bitte einen Widget-Namen und mindestens einen Sensor eintragen. Nachkommastellen: leer oder 0 bis 6.", SensorTitle);
                return false;
            }
            if (!Sensors && doors.Any(door => !(Cover ? HeatingWidget.IsCover(door.Entity) : Heating ? HeatingWidget.IsClimate(door.Entity) : Switches ? IsSwitchEntity(door.Entity) : Regex.IsMatch(door.Entity, @"^(binary_sensor|cover)\.[a-z0-9_]+$")) ||
                door.Name.Length == 0 || (!DeviceRows && (door.Open.Length == 0 || door.Closed.Length == 0))))
            {
                MessageBox.Show(this, "Bitte pro Sensor eine gültige Entitäts-ID, einen Namen und beide Statustexte eintragen.", Switches ? SwitchTitle : DoorTitle);
                return false;
            }
            Doors = doors;
            return true;
        }
    }
}
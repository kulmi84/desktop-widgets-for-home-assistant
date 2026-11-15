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
            case "open": return UiText.Status(Open);
            case "off":
            case "closed": return UiText.Status(Closed);
            case "playing": return UiText.Get("Wiedergabe", "Playing");
            case "paused": return UiText.Get("Pause", "Paused");
            case "idle": return UiText.Get("Bereit", "Ready");
            case "standby": return "Standby";
            case "opening": return UiText.Get("Öffnet", "Opening");
            case "closing": return UiText.Get("Schließt", "Closing");
            default: return UiText.Get("Nicht verfügbar", "Unavailable");
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
    public bool OnlyWhenOpen { get; set; }
}

sealed class DoorWidget : Form
{
    WidgetPlacement placement;
    static readonly Color Surface = Color.FromArgb(24, 24, 26);
    static readonly Color Input = Color.FromArgb(43, 43, 47);
    internal static readonly string DoorTitle = UiText.Get("Fenster-Tür-Sensoren", "Window and door sensors");
    internal static readonly string SwitchTitle = UiText.Get("Schalter", "Switches");
    readonly string configPath;
    readonly bool switches;
    readonly string instanceId;
    bool IsSensorWidget { get { return instanceId != null && !switches; } }
    bool IsContactWidget { get { return !switches && instanceId == null; } }
    bool ConditionalVisibility { get { return IsContactWidget && config.OnlyWhenOpen; } }
    internal bool ContactsEnabled { get; private set; }
    internal event Action ContactsDisabled;
    bool editingContacts;
    internal static readonly string SensorTitle = UiText.Get("Sensoren", "Sensors");
    internal string WidgetName { get { return config.Name ?? (switches ? SwitchTitle : SensorTitle); } }

    static string InstanceFolder(bool switches) { return Path.Combine(Path.GetDirectoryName(ConfigPath(false)), switches ? "switch-widgets" : "sensor-widgets"); }

    internal static bool IsSensorId(string id)
    {
        Guid value;
        return Guid.TryParseExact(id, "N", out value);
    }

    internal static System.Collections.Generic.Dictionary<string, string> SavedWidgets(bool switches = false)
    {
        var result = new System.Collections.Generic.Dictionary<string, string>();
        if (!Directory.Exists(InstanceFolder(switches))) return result;
        foreach (string file in Directory.GetFiles(InstanceFolder(switches), "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            if (!IsSensorId(id)) continue;
            try
            {
                var saved = new JavaScriptSerializer().Deserialize<DoorSettings>(File.ReadAllText(file));
                if (saved != null) result[id] = saved.Name ?? (switches ? SwitchTitle : SensorTitle);
            }
            catch (Exception) { }
        }
        return result;
    }

    internal static Appearance SavedInstanceStyle(string id, bool switches = false)
    {
        if (!IsSensorId(id)) return null;
        try
        {
            return new JavaScriptSerializer().Deserialize<DoorSettings>(
                File.ReadAllText(Path.Combine(InstanceFolder(switches), id + ".json"))).Appearance;
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
    event Action DeviceStateChanged;
    LightControls lightControls;
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

    internal DoorWidget(Func<Settings> connection, bool topMost, bool switches = false, string instanceId = null)
    {
        this.connection = connection;
        this.switches = switches;
        this.instanceId = instanceId;
        configPath = instanceId != null ? Path.Combine(InstanceFolder(switches), instanceId + ".json") : ConfigPath(switches);
        Text = switches ? SwitchTitle : DoorTitle;
        Icon = WidgetIcon.Load();
        Font = new Font("Segoe UI", 10);
        BackColor = Surface;
        ForeColor = Color.WhiteSmoke;
        TopMost = topMost;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        var menu = new WidgetMenu();
        menu.Items.Add(switches ? UiText.Get("Schalter bearbeiten…", "Edit switches…") : UiText.Get("Sensoren bearbeiten…", "Edit sensors…"), null, delegate { EditDoors(); });
        menu.Items.Add(UiText.Get("Aktualisieren", "Refresh"), null, async delegate { await RefreshDoors(); });
        WidgetPlacement.AddLockMenuItem(menu, this, () => placement);
        menu.Items.Add(UiText.Get("Schließen", "Close"), null, delegate
        {
            if (IsContactWidget) DisableContacts();
            else Hide();
        });
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
            if (!Visible)
            {
                if (!ContactsEnabled) timer.Stop();
                return;
            }
            timer.Start();
            await RefreshDoors();
        };
        StartPosition = FormStartPosition.CenterParent;
        Shown += async delegate
        {
            Render(config.Doors, lastStates);
            Point fallback = Owner == null ? Location : new Point(Owner.Left, Owner.Bottom + 10);
            if (config.Position != null) fallback = new Point(config.Position.X, config.Position.Y);
            string placementId = instanceId != null ? (switches ? "switch:" : "sensor:") + instanceId : switches ? "switches" : "doors";
            placement = new WidgetPlacement(this, placementId, fallback,
                horizontalAnchor: () => config.Appearance == null ? HorizontalAnchor.Right : config.Appearance.HorizontalAnchor);
            if (config.Doors.Length == 0 && !editingContacts) EditDoors();
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
            if (!dragStart.HasValue || !Capture || pressedSwitch >= 0 || (placement != null && placement.Locked)) return;
            var start = dragStart.Value;
            Location = new Point(windowStart.X + Cursor.Position.X - start.X, windowStart.Y + Cursor.Position.Y - start.Y);
        };
        MouseUp += async delegate(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left || !dragStart.HasValue) return;
            Point start = dragStart.Value;
            dragStart = null;
            Capture = false;
            int index = pressedSwitch;
            pressedSwitch = -1;
            if (index >= 0)
            {
                if (HitSwitch(args.Location) == index) await ToggleSwitch(index);
                return;
            }
            if (Location == windowStart)
            {
                Size threshold = SystemInformation.DragSize;
                if (switches && Math.Abs(Cursor.Position.X - start.X) < Math.Max(1, threshold.Width / 2) &&
                    Math.Abs(Cursor.Position.Y - start.Y) < Math.Max(1, threshold.Height / 2))
                    OpenLight(DeviceAt(args.Location));
                return;
            }
            try { if (placement != null) placement.Save(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }

    internal async void OpenContacts(bool edit)
    {
        ContactsEnabled = true;
        timer.Start();
        if (edit) EditDoors();
        else
        {
            UpdateContactVisibility();
            if (Visible && !ConditionalVisibility) Activate();
            if (!Visible) await RefreshDoors();
        }
    }

    internal void DisableContacts()
    {
        ContactsEnabled = false;
        lastStates = null;
        timer.Stop();
        Hide();
        if (ContactsDisabled != null) ContactsDisabled();
    }

    internal static bool AnyContactOpen(string[] states)
    {
        return states != null && states.Any(state => state == "on" || state == "open" ||
            state == "opening" || state == "closing");
    }

    void UpdateContactVisibility()
    {
        if (!IsContactWidget || !ContactsEnabled || editingContacts) return;
        bool show = !ConditionalVisibility || AnyContactOpen(lastStates);
        if (show && !Visible) Show();
        else if (!show && Visible) Hide();
    }

    protected override bool ShowWithoutActivation { get { return ConditionalVisibility; } }

    int HitSwitch(Point point)
    {
        if (!switches) return -1;
        return Array.FindIndex(switchBounds, bounds => bounds.Contains(point));
    }

    bool OpenLight(int index)
    {
        if (index < 0 || index >= config.Doors.Length) return false;
        var door = config.Doors[index];
        if (!door.Entity.StartsWith("light.", StringComparison.Ordinal)) return false;
        var anchor = PointToScreen(Point.Round(new PointF(switchBounds[index].Left, switchBounds[index].Bottom)));
        if (lightControls != null) lightControls.Close();
        var controls = new LightControls(door.Name, config.Appearance ?? new Appearance(),
            () => deviceStates == null || index >= deviceStates.Length ? null : deviceStates[index],
            (service, data) =>
            {
                data["entity_id"] = door.Entity;
                return SendDeviceAction(door.Entity, service, data);
            }, anchor);
        lightControls = controls;
        DeviceStateChanged += controls.RefreshState;
        controls.FormClosed += delegate
        {
            DeviceStateChanged -= controls.RefreshState;
            if (lightControls == controls) lightControls = null;
        };
        controls.Show(this);
        return true;
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
        if (device.Entity.StartsWith("light.", StringComparison.Ordinal)) return;
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
                add(group.DropDownItems, domain == "scene" ? UiText.Get("Szene aktivieren", "Activate scene") : UiText.Get("Skript starten", "Run script"), "turn_on", new { entity_id = entity });
            if (domain == "light" || domain == "fan" || domain == "switch" || domain == "input_boolean")
            {
                string action = sensor.state == "on" ? "turn_off" : "turn_on";
                add(group.DropDownItems, sensor.state == "on" ? UiText.Get("Ausschalten", "Turn off") : UiText.Get("Einschalten", "Turn on"), action, new { entity_id = entity });
            }
            if (domain == "fan" && (features & 1) != 0)
                group.DropDownItems.Add(LevelMenu(UiText.Get("Lüfterstufe", "Fan speed"), value => SendDeviceAction(entity, "set_percentage", new { entity_id = entity, percentage = value })));
            if (domain == "media_player")
            {
                if ((features & 16384) != 0) add(group.DropDownItems, UiText.Get("Wiedergabe", "Play"), "media_play", new { entity_id = entity });
                if ((features & 1) != 0) add(group.DropDownItems, "Pause", "media_pause", new { entity_id = entity });
                if ((features & 4096) != 0) add(group.DropDownItems, UiText.Get("Stopp", "Stop"), "media_stop", new { entity_id = entity });
                if ((features & 16) != 0) add(group.DropDownItems, UiText.Get("Vorheriger Titel", "Previous track"), "media_previous_track", new { entity_id = entity });
                if ((features & 32) != 0) add(group.DropDownItems, UiText.Get("Nächster Titel", "Next track"), "media_next_track", new { entity_id = entity });
                if ((features & 128) != 0) add(group.DropDownItems, UiText.Get("Einschalten", "Turn on"), "turn_on", new { entity_id = entity });
                if ((features & 256) != 0) add(group.DropDownItems, UiText.Get("Ausschalten", "Turn off"), "turn_off", new { entity_id = entity });
                if ((features & 4) != 0)
                    group.DropDownItems.Add(LevelMenu(UiText.Get("Lautstärke", "Volume"), value => SendDeviceAction(entity, "volume_set", new { entity_id = entity, volume_level = value / 100m })));
            }
            items.AddRange(group.DropDownItems.Cast<ToolStripItem>());
            group.DropDownItems.Clear();
        }
        return items;
    }

    static ToolStripMenuItem LevelMenu(string label, Func<int, Task> apply)
    {
        var menu = new ToolStripMenuItem(label) { ForeColor = Color.WhiteSmoke };
        var level = new ToolStripTextBox { AccessibleName = label + UiText.Get(" in Prozent", " in percent"), MaxLength = 3, Width = 65,
            BackColor = Input, ForeColor = Color.WhiteSmoke, ToolTipText = UiText.Get("0–100 eingeben, mit Enter bestätigen", "Enter 0–100, then press Enter") };
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
        menu.DropDownItems.Add(UiText.Get("Übernehmen (%)", "Apply (%)"), null, delegate { submit(); }).ForeColor = Color.WhiteSmoke;
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
            if (!IsDisposed) MessageBox.Show(this, UiText.Get("Home Assistant konnte die Aktion nicht ausführen.", "Home Assistant could not perform the action."), Text);
        }
        finally { switching = false; }
        if (!IsDisposed) await RefreshDoors();
    }

    void Save(Door[] doors, Appearance appearance, string name, bool onlyWhenOpen)
    {
        var next = new DoorSettings { Name = name ?? config.Name, Doors = doors, Position = config.Position,
            Appearance = appearance, OnlyWhenOpen = onlyWhenOpen };
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
        if (editingContacts) return;
        var settings = connection();
        var editor = new DoorEditor(config.Doors, connection, switches, sensors: IsSensorWidget, widgetName: instanceId != null ? WidgetName : null);
        editor.OnlyWhenOpen = config.OnlyWhenOpen;
        using (var dialog = new Options(settings, new string[0], config.Appearance ?? new Appearance(),
            settings == null ? null : settings.Appearance, editor))
        {
            DialogResult result;
            editingContacts = IsContactWidget;
            try { result = Visible ? dialog.ShowDialog(this) : dialog.ShowDialog(); }
            finally { editingContacts = false; }
            if (result != DialogResult.OK)
            {
                if (instanceId != null && config.Doors.Length == 0) Close();
                UpdateContactVisibility();
                if (IsContactWidget) await RefreshDoors();
                return;
            }
            try { Save(editor.Doors, dialog.SelectedAppearance, instanceId != null ? editor.WidgetName : null,
                IsContactWidget && editor.OnlyWhenOpen); }
            catch (Exception)
            {
                MessageBox.Show(this, UiText.Get("Die Einstellungen konnten nicht gespeichert werden.", "Could not save settings."), Text);
                return;
            }
        }
        Render(config.Doors, null);
        UpdateContactVisibility();
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
        var names = doors.Length == 0 ? new[] { instanceId != null ? WidgetName : switches ? SwitchTitle : DoorTitle } : doors.Select(door => door.Name).ToArray();
        var values = doors.Length == 0 ? new[] { switches ? UiText.Get("Schalter hinzufügen", "Add switches") : UiText.Get("Sensoren hinzufügen", "Add sensors") } : doors.Select((door, index) =>
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
            if (placement != null) placement.Resize(bitmap.Size);
            else if (ClientSize != bitmap.Size) ClientSize = bitmap.Size;
            LayeredTile.Present(Handle, bitmap);
        }
        AccessibleName = Text = (instanceId != null ? WidgetName : switches ? SwitchTitle : DoorTitle) + ": " + string.Join(", ", names.Zip(values, (name, state) => name + " " + state));
        if (DeviceStateChanged != null) DeviceStateChanged();
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
        else if (output == "on" || output == "off") output = output == "on" ? UiText.Status("Ein") : UiText.Status("Aus");
        var unit = string.IsNullOrWhiteSpace(unitOverride)
            ? (state.attributes == null ? null : state.attributes.unit_of_measurement) : unitOverride;
        return string.IsNullOrWhiteSpace(unit) ? output : output + " " + unit;
    }

    bool refreshQueued;

    internal Task RefreshLive(string[] changes)
    {
        if ((!Visible && !ContactsEnabled) || !changes.Any(entity => entity == null || config.Doors.Any(door => door.Entity == entity)))
            return Task.FromResult(0);
        return RefreshDoors(true);
    }

    async Task RefreshDoors(bool live = false)
    {
        if (IsDisposed || (!Visible && !ContactsEnabled) || config.Doors.Length == 0) return;
        if (loading || switching) { refreshQueued |= live; return; }
        var doors = config.Doors;
        var settings = connection();
        if (settings == null) return;
        loading = true;
        try
        {
            string token = settings.ReadToken();
            var received = await Task.WhenAll(doors.Select(door => ReadState(door, settings.Url, token)));
            if (!IsDisposed && (Visible || ContactsEnabled) && doors == config.Doors && !switching && connection() == settings)
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
        if (!IsDisposed) UpdateContactVisibility();
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
        sealed class IconChoice
        {
            public string Key { get; private set; }
            public string Label { get; private set; }

            internal IconChoice(string key, string english)
            {
                Key = key;
                Label = UiText.Get(key, english);
            }
        }

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
        internal bool OnlyWhenOpen { get; set; }
        internal bool DeviceRows { get { return Heating || Cover; } }
        internal DoorEditor(Door[] doors, Func<Settings> connection, bool switches = false, bool heating = false, bool cover = false, bool sensors = false, string widgetName = null)
        {
            Heating = heating;
            Cover = cover;
            Sensors = sensors;
            this.connection = connection;
            Switches = switches;
            Font = new Font("Segoe UI", 10);
            BackColor = SettingsTheme.Card;
            ForeColor = Color.WhiteSmoke;
            grid.Bounds = new Rectangle(0, 0, 644, 183);
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = SettingsTheme.Input;
            grid.GridColor = SettingsTheme.Border;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(53, 78, 98);
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Input;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
            grid.DefaultCellStyle.BackColor = Input;
            grid.DefaultCellStyle.ForeColor = ForeColor;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add("entity", UiText.Get("Entitäts-ID", "Entity ID"));
            grid.Columns[0].FillWeight = 185;
            grid.Columns.Add("name", "Name");
            grid.Columns.Add("open", switches ? UiText.Get("Text bei ein", "Text when on") : UiText.Get("Text bei offen", "Text when open"));
            grid.Columns.Add("closed", switches ? UiText.Get("Text bei aus", "Text when off") : UiText.Get("Text bei geschlossen", "Text when closed"));
            var icons = new DataGridViewComboBoxColumn { HeaderText = "Icon", FlatStyle = FlatStyle.Flat,
                DisplayMember = "Label", ValueMember = "Key" };
            icons.DataSource = switches ? new[] { new IconChoice("Schalter", "Switch") } :
                new[] { new IconChoice("Automatisch", "Automatic"), new IconChoice("Tür", "Door"),
                    new IconChoice("Garagentor", "Garage door"), new IconChoice("Fenster", "Window") };
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
                grid.Columns[2].HeaderText = UiText.Get("Dezimalstellen", "Decimals");
                grid.Columns[3].HeaderText = UiText.Get("Einheit", "Unit");
                grid.Columns[3].FillWeight = 60;

                icons.DataSource = new[] { new IconChoice("Messwert", "Reading"), new IconChoice("Temperatur", "Temperature"),
                    new IconChoice("Luftfeuchtigkeit", "Humidity"), new IconChoice("Solar", "Solar"),
                    new IconChoice("Akku", "Battery"), new IconChoice("Haus", "Home"), new IconChoice("Strom", "Power") };
            }
            if (Sensors || widgetName != null)
            {
                widgetNameInput.Text = widgetName ?? SensorTitle;
                widgetNameInput.Bounds = new Rectangle(80, 0, 564, 24);
                widgetNameInput.BackColor = Input;
                widgetNameInput.ForeColor = ForeColor;
                widgetNameInput.MaxLength = 60;
                widgetNameInput.AccessibleName = UiText.Get("Widget-Name", "Widget name");
                Controls.Add(new Label { Text = "Name", Bounds = new Rectangle(0, 3, 72, 22) });
                Controls.Add(widgetNameInput);
                grid.Top = 29;
                grid.Height -= 29;
            }
            if (DeviceRows) { grid.Columns[2].Visible = false; grid.Columns[3].Visible = false; icons.Visible = false; }
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (var door in doors)
                grid.Rows.Add(door.Entity, door.Name, Sensors ? door.Decimals.ToString() : UiText.Status(door.Open),
                    Sensors ? door.Unit : UiText.Status(door.Closed), Sensors ? door.IconKind ?? "Messwert" : switches ? "Schalter" : door.IconKind ?? "Automatisch");

            var add = Button(UiText.Get("Sensor auswählen…", "Select sensor…"), 0, 151);
            var manual = Button(UiText.Get("Manuell hinzufügen", "Add manually"), 157, 150);
            var remove = Button(UiText.Get("Entfernen", "Remove"), 313, 86);
            var up = Button(DeviceRows ? UiText.Get("↑ Nach oben", "↑ Move up") : UiText.Get("↑ Nach links", "↑ Move left"), 405, 109);
            var down = Button(DeviceRows ? UiText.Get("↓ Nach unten", "↓ Move down") : UiText.Get("↓ Nach rechts", "↓ Move right"), 520, 124);
            add.Click += async delegate { await SelectDoor(add); };
            manual.Click += delegate { grid.Rows.Add("", "", Sensors ? "" : UiText.Status(Switches ? "Ein" : "Offen"),
                Sensors ? "" : UiText.Status(Switches ? "Aus" : "Geschlossen"), Sensors ? "Messwert" : Switches ? "Schalter" : "Automatisch"); };
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
            var button = new SettingsButton { Text = text, Bounds = new Rectangle(left, 190, width, 29),
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
                if (settings == null) throw new ArgumentException(UiText.Get("Bitte zuerst Home Assistant einrichten.", "Set up Home Assistant first."));
                var sensors = await Options.LoadSensors(new Uri(settings.Url), settings.ReadToken());
                if (IsDisposed) return;
                using (var picker = new Options.SensorPicker(sensors, Sensors ? "any_sensor" : Cover ? "window_cover" : Heating ? "climate" : Switches ? "switch" : null, ""))
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    var sensor = picker.Selection;
                    grid.Rows.Add(sensor.entity_id, sensor.attributes.friendly_name ?? sensor.entity_id,
                        Sensors ? "" : UiText.Status(Switches ? "Ein" : "Offen"), Sensors ? "" : UiText.Status(Switches ? "Aus" : "Geschlossen"),
                        Sensors ? (sensor.attributes.device_class == "humidity" ? "Luftfeuchtigkeit" : "Messwert") : Switches ? "Schalter" :
                        sensor.attributes.device_class == "garage_door" ? "Garagentor" :
                        sensor.attributes.device_class == "window" ? "Fenster" : "Automatisch");
                }
            }
            catch (ArgumentException ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, Text); }
            catch (Exception) { if (!IsDisposed) MessageBox.Show(this, UiText.Get("Die Türsensoren konnten nicht geladen werden.", "Could not load the available entities."), Text); }
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
            if (Switches && widgetNameInput.Parent != null && (WidgetName.Length == 0 || doors.Length == 0))
            {
                MessageBox.Show(this, UiText.Get("Bitte einen Widget-Namen und mindestens einen Schalter angeben.", "Enter a widget name and add at least one switch."), SwitchTitle);
                return false;
            }
            if (Sensors && (WidgetName.Length == 0 || doors.Length == 0 ||
                doors.Any(door => !IsSensor(door.Entity) || door.Name.Length == 0 ||
                    (!string.IsNullOrWhiteSpace(door.Open) && !door.Decimals.HasValue))))
            {
                MessageBox.Show(this, UiText.Get("Bitte einen Widget-Namen und mindestens einen Sensor eintragen. Nachkommastellen: leer oder 0 bis 6.", "Enter a widget name and add at least one sensor. Leave decimal places blank or enter 0 to 6."), SensorTitle);
                return false;
            }
            if (!Sensors && doors.Any(door => !(Cover ? HeatingWidget.IsCover(door.Entity) : Heating ? HeatingWidget.IsClimate(door.Entity) : Switches ? IsSwitchEntity(door.Entity) : Regex.IsMatch(door.Entity, @"^(binary_sensor|cover)\.[a-z0-9_]+$")) ||
                door.Name.Length == 0 || (!DeviceRows && (door.Open.Length == 0 || door.Closed.Length == 0))))
            {
                MessageBox.Show(this, UiText.Get("Bitte pro Sensor eine gültige Entitäts-ID, einen Namen und beide Statustexte eintragen.", "Enter a valid entity ID, a name and both status labels for each sensor."), Switches ? SwitchTitle : DoorTitle);
                return false;
            }
            Doors = doors;
            return true;
        }
    }
}

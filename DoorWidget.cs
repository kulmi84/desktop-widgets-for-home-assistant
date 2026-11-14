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
    internal static string ConfigPath(bool switches)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolarKachel", switches ? "switches.json" : "doors.json");
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
        return Regex.IsMatch(entity, @"^(switch|light|input_boolean|fan|binary_sensor)\.[a-z0-9_]+$");
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

    internal DoorWidget(Func<Settings> connection, bool topMost, bool switches = false)
    {
        this.connection = connection;
        this.switches = switches;
        configPath = ConfigPath(switches);
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

    async Task ToggleSwitch(int index)
    {
        if (switching || index >= config.Doors.Length || lastStates == null) return;
        string state = lastStates[index];
        if (state != "on" && state != "off") return;
        string entity = config.Doors[index].Entity;
        string domain = entity.Split('.')[0];
        if (domain != "switch" && domain != "light" && domain != "input_boolean" && domain != "fan") return;
        var settings = connection();
        if (settings == null) return;
        switching = true;
        try
        {
            string service = state == "on" ? "turn_off" : "turn_on";
            string url = settings.Url.TrimEnd('/') + "/api/services/" + domain + "/" + service;
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadToken());
                request.Content = new StringContent(new JavaScriptSerializer().Serialize(new { entity_id = entity }),
                    System.Text.Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request)) response.EnsureSuccessStatusCode();
            }
            lastStates = null;
        }
        catch (Exception)
        {
            if (!IsDisposed) MessageBox.Show(this, "Home Assistant konnte den Schalter nicht umstellen.", Text);
        }
        finally { switching = false; }
        if (!IsDisposed) await RefreshDoors();
    }

    void Save(Door[] doors, Appearance appearance)
    {
        var next = new DoorSettings { Doors = doors, Position = new DoorPosition { X = Left, Y = Top }, Appearance = appearance };
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
        var editor = new DoorEditor(config.Doors, connection, switches);
        using (var dialog = new Options(settings, new string[0], config.Appearance ?? new Appearance(),
            settings == null ? null : settings.Appearance, editor))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { Save(editor.Doors, dialog.SelectedAppearance); }
            catch (Exception)
            {
                MessageBox.Show(this, "Die Türen konnten nicht gespeichert werden.", Text);
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
        switchBounds = new RectangleF[switches ? Math.Max(1, doors.Length) : 0];
        var names = doors.Length == 0 ? new[] { switches ? SwitchTitle : DoorTitle } : doors.Select(door => door.Name).ToArray();
        var values = doors.Length == 0 ? new[] { switches ? "Schalter hinzufügen" : "Sensoren hinzufügen" } : doors.Select((door, index) =>
            states == null ? "—" : door.Describe(states[index])).ToArray();
        var opened = doors.Select((door, index) => states != null &&
            (states[index] == "on" || states[index] == "open" || states[index] == "opening")).ToArray();
        if (opened.Length == 0) opened = new bool[1];
        if (switches && doors.Length == 0) doors = new[] { new Door { IconKind = "Schalter" } };
        float scale = LayeredTile.ScaleFor(this);
        using (var bitmap = PaintDoors(config.Appearance ?? new Appearance(), names, values, opened, scale, 1, doors, switches ? switchBounds : null))
        {
            if (ClientSize != bitmap.Size) ClientSize = bitmap.Size;
            LayeredTile.Present(Handle, bitmap);
        }
        AccessibleName = Text = (switches ? SwitchTitle : DoorTitle) + ": " + string.Join(", ", names.Zip(values, (name, state) => name + " " + state));
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

    async Task RefreshDoors()
    {
        if (loading || switching || IsDisposed || !Visible || config.Doors.Length == 0) return;
        var doors = config.Doors;
        var settings = connection();
        if (settings == null) return;
        loading = true;
        try
        {
            string token = settings.ReadToken();
            var states = await Task.WhenAll(doors.Select(door => ReadState(door, settings.Url, token)));
            if (!IsDisposed && doors == config.Doors && !switching) Render(doors, states);
        }
        catch (Exception)
        {
            if (!IsDisposed && doors == config.Doors) Render(doors, new string[doors.Length]);
        }
        finally { loading = false; }
        if (!IsDisposed && doors != config.Doors) await RefreshDoors();
    }

    async Task<string> ReadState(Door door, string url, string token)
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
                    return state == null ? null : state.state;
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
        internal DoorEditor(Door[] doors, Func<Settings> connection, bool switches = false, bool heating = false)
        {
            Heating = heating;
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
            if (heating) { grid.Columns[2].Visible = false; grid.Columns[3].Visible = false; icons.Visible = false; }
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (var door in doors)
                grid.Rows.Add(door.Entity, door.Name, door.Open, door.Closed, switches ? "Schalter" : door.IconKind ?? "Automatisch");

            var add = Button("Sensor auswählen…", 0, 151);
            var manual = Button("Manuell hinzufügen", 157, 150);
            var remove = Button("Entfernen", 313, 86);
            var up = Button(heating ? "↑ Nach oben" : "↑ Nach links", 405, 109);
            var down = Button(heating ? "↓ Nach unten" : "↓ Nach rechts", 520, 124);
            add.Click += async delegate { await SelectDoor(add); };
            manual.Click += delegate { grid.Rows.Add("", "", Switches ? "Ein" : "Offen", Switches ? "Aus" : "Geschlossen", Switches ? "Schalter" : "Automatisch"); };
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
                using (var picker = new Options.SensorPicker(sensors, Heating ? "climate" : Switches ? "switch" : null, ""))
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    var sensor = picker.Selection;
                    grid.Rows.Add(sensor.entity_id, sensor.attributes.friendly_name ?? sensor.entity_id, Switches ? "Ein" : "Offen", Switches ? "Aus" : "Geschlossen",
                        Switches ? "Schalter" :
                        sensor.attributes.device_class == "garage_door" ? "Garagentor" :
                        sensor.attributes.device_class == "window" ? "Fenster" : "Automatisch");
                }
            }
            catch (ArgumentException ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, Text); }
            catch (Exception) { if (!IsDisposed) MessageBox.Show(this, "Die Türsensoren konnten nicht geladen werden.", Text); }
            finally { if (!add.IsDisposed) add.Enabled = true; }
        }

        internal Door[] ReadRows()
        {
            return grid.Rows.Cast<DataGridViewRow>().Select(row => new Door
            {
                Entity = Convert.ToString(row.Cells[0].Value).Trim().ToLowerInvariant(),
                Name = Convert.ToString(row.Cells[1].Value).Trim(),
                Open = Convert.ToString(row.Cells[2].Value).Trim(),
                Closed = Convert.ToString(row.Cells[3].Value).Trim(),
                IconKind = Convert.ToString(row.Cells[4].Value)
            }).ToArray();
        }

        internal bool SaveRows()
        {
            grid.EndEdit();
            var doors = ReadRows();
            if (doors.Any(door => !(Heating ? HeatingWidget.IsClimate(door.Entity) : Switches ? IsSwitchEntity(door.Entity) : Regex.IsMatch(door.Entity, @"^(binary_sensor|cover)\.[a-z0-9_]+$")) ||
                door.Name.Length == 0 || (!Heating && (door.Open.Length == 0 || door.Closed.Length == 0))))
            {
                MessageBox.Show(this, "Bitte pro Sensor eine gültige Entitäts-ID, einen Namen und beide Statustexte eintragen.", Switches ? SwitchTitle : DoorTitle);
                return false;
            }
            Doors = doors;
            return true;
        }
    }
}
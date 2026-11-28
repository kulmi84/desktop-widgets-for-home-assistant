#if STORE
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class BoardOptions : BoardSettingsWindow
{
    internal static string MenuLabel { get { return "Windows Widgets…"; } }

    internal static bool IsSupported()
    {
        var build = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "CurrentBuildNumber", null) as string;
        int number;
        return int.TryParse(build, out number) && number >= 22000;
    }

    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly Settings connection;
    readonly BoardConfiguration config;
    readonly List<BoardProfile> profiles;
    readonly List<SolarBoardProfile> solarProfiles;
    readonly DataGridView table;
    readonly SettingsButton properties;
    readonly SettingsButton remove;

    internal BoardOptions(Settings connection)
        : base("Windows Widgets", new Size(640, 360))
    {
        this.connection = connection;
        config = new BoardConfiguration();
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
                "Could not read saved selection."), AutoSize = true, Location = new Point(14, 271) });
        }
        profiles = config.AllProfiles().ToList();
        solarProfiles = config.AllSolarProfiles().ToList();

        table = CreateTable(new Rectangle(14, 14, 612, 249));
        table.ReadOnly = true;
        table.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        table.Columns.Add(TextColumn(UiText.Get("Kachel", "Widget"), 65));
        table.Columns.Add(TextColumn(UiText.Get("Typ", "Type"), 35));
        table.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs args)
        {
            if (args.RowIndex >= 0) EditSelection();
        };
        table.KeyDown += delegate(object sender, KeyEventArgs args)
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            EditSelection();
        };
        Controls.Add(table);

        var add = CreateButton(UiText.Get("Neue Kachel…", "New widget…"), 14, 296, 112);
        properties = CreateButton(UiText.Get("Eigenschaften…", "Properties…"), 134, 296, 115);
        remove = CreateButton(UiText.Get("Entfernen", "Remove"), 257, 296, 96);
        var cancel = CreateButton(UiText.Get("Abbrechen", "Cancel"), 406, 296, 106);
        var save = CreateButton(UiText.Get("Speichern", "Save"), 520, 296, 106);
        Controls.Add(new Label { Text = ReleaseVersion.Display, ForeColor = Color.FromArgb(172, 179, 190),
            AutoSize = true, Location = new Point(14, 331), Anchor = AnchorStyles.Left | AnchorStyles.Bottom });
        save.Primary = true;
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        var types = new ContextMenuStrip();
        types.Items.Add("Home Assistant", null, delegate { AddProfile(new BoardProfile()); });
        types.Items.Add(UiText.Get("Solaranlage", "Solar installation"), null,
            delegate { AddProfile(new SolarBoardProfile()); });
        add.Disposed += delegate { types.Dispose(); };
        add.Click += delegate { types.Show(add, new Point(0, add.Height)); };
        properties.Click += delegate { EditSelection(); };
        remove.Click += delegate
        {
            if (table.CurrentRow == null) return;
            var profile = table.CurrentRow.Tag as BoardProfile;
            if (profile == null || profile.Id.Length == 0) return;
            int index = table.CurrentRow.Index;
            var solar = profile as SolarBoardProfile;
            if (solar == null) profiles.Remove(profile);
            else solarProfiles.Remove(solar);
            ShowRows(Math.Min(index, profiles.Count + solarProfiles.Count));
        };
        save.Click += Save;
        table.SelectionChanged += delegate
        {
            properties.Enabled = table.CurrentRow != null;
            var profile = table.CurrentRow == null ? null : table.CurrentRow.Tag as BoardProfile;
            remove.Enabled = profile != null && profile.Id.Length != 0;
        };
        ShowRows(0);
        SettingsTheme.ApplyMonitorStyle(this);
    }

    void ShowRows(int selected)
    {
        table.Rows.Clear();
        foreach (var profile in profiles)
        {
            var widgetText = profile.WidgetText;
            string title = widgetText != null && widgetText.UseCustomDescription && !widgetText.HideTitle
                ? widgetText.Description : null;
            int index = table.Rows.Add(string.IsNullOrWhiteSpace(title) ? profile.Name : title.Trim(), "Home Assistant");
            table.Rows[index].Tag = profile;
        }
        table.Rows.Add(config.SolarDisplayName(), UiText.Get("Solar-Widget", "Solar widget"));
        foreach (var solar in solarProfiles)
        {
            int index = table.Rows.Add(solar.Name, UiText.Get("Solar-Widget", "Solar widget"));
            table.Rows[index].Tag = solar;
        }
        table.CurrentCell = table.Rows[selected].Cells[0];
    }

    void AddProfile(BoardProfile profile)
    {
        profile.Id = Guid.NewGuid().ToString("N");
        var solar = profile as SolarBoardProfile;
        profile.Name = solar == null ? UiText.Get("Neue Kachel", "New widget") : UiText.Get("Neue Solarkachel", "New solar widget");
        using (var dialog = CreateProperties(profile))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (solar == null) profiles.Add(profile);
            else solarProfiles.Add(solar);
            ShowRows(solar == null ? profiles.Count - 1 : profiles.Count + solarProfiles.Count);
        }
    }

    Form CreateProperties(BoardProfile profile)
    {
        var solar = profile as SolarBoardProfile;
        if (profile == null || solar != null) return new SolarBoardProperties(connection, config, solar);
        return new BoardProperties(connection, profile);
    }

    void EditSelection()
    {
        if (table.CurrentRow == null) return;
        int selected = table.CurrentRow.Index;
        using (var dialog = CreateProperties(table.CurrentRow.Tag as BoardProfile))
            if (dialog.ShowDialog(this) == DialogResult.OK) ShowRows(selected);
    }

    void Save(object sender, EventArgs args)
    {
        try
        {
            config.Entities = profiles[0].Entities;
            config.HighlightDefault = profiles[0].HighlightValues;
            config.DefaultWidgetText = profiles[0].WidgetText;
            config.Profiles = profiles.Skip(1).ToArray();
            config.SolarProfiles = solarProfiles.ToArray();
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

sealed class BoardProperties : BoardSettingsWindow
{
    readonly DataGridView table;
    readonly BoardProfile profile;
    readonly Settings connection;
    readonly DataGridViewRow[] entities;
    DataGridViewRow name;
    readonly bool[] sizeHighlights;
    readonly ComboBox previewLayout = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label previewLayoutLabel = new Label();
    DataGridViewRow description;
    readonly string legacyAction, legacyActionCaption;
    DataGridViewRow compactTitle;
    DataGridViewRow[] compactModes, compactNames, compactSizes, compactIcons, rowActions, columnRows;
    readonly string[] columnNames = { UiText.Get("Automatisch", "Automatic"), UiText.Get("Links", "Left"), UiText.Get("Rechts", "Right") };
    int expandedRow = -1;
    SettingsButton backFromAppearance;
    readonly Dictionary<string, string> previewNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly string[] iconNames = { UiText.Get("Automatisch", "Automatic"), UiText.Get("Garagentor", "Garage door"),
        UiText.Get("Tür", "Door"), UiText.Get("Fenster", "Window"), UiText.Get("Akku", "Battery"),
        UiText.Get("Solar", "Solar"), UiText.Get("LED-Streifen", "LED strip"), UiText.Get("Glühlampe", "Light bulb"),
        UiText.Get("Schalter", "Switch"), UiText.Get("Licht", "Light"), UiText.Get("Kein Symbol", "No icon") };
    readonly string[] iconValues = { "", "garage", "door", "window-closed", "battery", "solar-panel",
        "led-strip", "lightbulb", "toggle-switch", "light", "none" };
    readonly string[] fontSizes = { UiText.Get("Automatisch", "Automatic"), UiText.Get("Normal", "Normal"),
        UiText.Get("Klein", "Small"), UiText.Get("Groß", "Large") };
    readonly string[] fontValues = { "", "Default", "Small", "Large" };
    readonly Panel preview = new Panel();
    readonly ComboBox previewType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox previewSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly RectangleF[] previewRows = new RectangleF[BoardSelection.TwoColumnCapacity];
    int draggedSlot = -1;
    Point dragStart;
    bool suppressPreviewClick;
    readonly string compactIconStatus = UiText.Get("Nur Icon und Status", "Icon and status only");
    readonly Label sectionHint = new Label();
    readonly Panel overview = new Panel();
    readonly TableLayoutPanel widgetSettings = new TableLayoutPanel();
    Label smallTitleLabel;
    Control smallTitleInput;
    readonly CheckBox columnDivider = new CheckBox();
    readonly SettingsButton[] actionButtons = new SettingsButton[BoardSelection.TwoColumnCapacity];
    readonly ToolTip overviewTips = new ToolTip();
    int selectedSlot = -1;
    bool browsing;

    internal BoardProperties(Settings connection, BoardProfile profile)
        : base(UiText.Get("Eigenschaften", "Properties") + " – " +
            profile.Name,
            new Size(1150, 584))
    {
        this.connection = connection;
        this.profile = profile;
        sectionHint.Bounds = new Rectangle(14, 54, 792, 32);
        sectionHint.ForeColor = Color.Silver;
        Controls.Add(sectionHint);
        table = CreateTable(new Rectangle(14, 89, 792, ClientSize.Height - 137));
        table.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        sectionHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        table.Columns.Add(TextColumn(UiText.Get("Bereich", "Category"), 18));
        table.Columns.Add(TextColumn(UiText.Get("Eigenschaft", "Property"), 31));
        table.Columns.Add(TextColumn(UiText.Get("Einstellung", "Setting"), 51));
        table.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = UiText.Get("Sensor", "Sensor"), Width = 96, ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        table.Columns[0].ReadOnly = table.Columns[1].ReadOnly = true;
        table.Columns.Add(TextColumn(UiText.Get("Beschriftung", "Label"), 25));
        table.Columns[4].DisplayIndex = 3;
        table.EditMode = DataGridViewEditMode.EditOnEnter;
        table.StandardTab = false;
        table.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs args)
        {
            args.Control.BackColor = SettingsTheme.MonitorInput;
            args.Control.ForeColor = Color.WhiteSmoke;
        };
        table.DataError += delegate(object sender, DataGridViewDataErrorEventArgs args) { args.ThrowException = false; };
        Controls.Add(table);

        var text = profile.WidgetText ?? new BoardWidgetText();
        columnDivider.Checked = text.ShowColumnDivider;
        string oldAction = text.ActionEntity ?? "";
        var oldEntities = profile.Entities ?? new string[0];
        int legacySlot = oldAction.Length == 0 ? -1 : Array.FindIndex(oldEntities, id =>
            !string.IsNullOrEmpty(id) && id != oldAction && id.StartsWith("binary_sensor.", StringComparison.Ordinal) &&
            (BoardSelection.ContactIconFromId(oldAction) != "garage" || BoardSelection.ContactIconFromId(id) == "garage"));
        legacyAction = legacySlot < 0 ? oldAction : "";
        legacyActionCaption = legacySlot < 0 ? text.ActionCaption : "";
        entities = new DataGridViewRow[BoardSelection.TwoColumnCapacity];
        int firstSensor = Array.FindIndex(oldEntities, id => !string.IsNullOrWhiteSpace(id) && id != oldAction);
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = AddRow(UiText.Get("Anzeige", "Display"), UiText.Get("Messwert / Status ", "Reading / status ") + (i + 1),
                (profile.Entities ?? new string[0]).ElementAtOrDefault(i), browse: true);
            var caption = (DataGridViewTextBoxCell)entities[i].Cells[4];
            caption.ReadOnly = false;
            caption.MaxInputLength = 40;
            caption.Value = (text.CompactCaptions ?? new string[0]).ElementAtOrDefault(i);
            if (i == firstSensor && string.IsNullOrWhiteSpace(Convert.ToString(caption.Value)))
                caption.Value = text.StatusCaption;
        }
        compactModes = new DataGridViewRow[BoardSelection.TwoColumnCapacity];
        compactNames = new DataGridViewRow[BoardSelection.TwoColumnCapacity];
        compactSizes = new DataGridViewRow[BoardSelection.TwoColumnCapacity];
        compactIcons = new DataGridViewRow[BoardSelection.TwoColumnCapacity];
        rowActions = new DataGridViewRow[BoardSelection.TwoColumnCapacity];
        columnRows = new DataGridViewRow[BoardSelection.TwoColumnCapacity];
        string compactFull = UiText.Get("Icon, Name und Status", "Icon, name and status");
        for (int i = 0; i < compactModes.Length; i++)
        {
            string rowLabel = UiText.Get("Sensorzeile ", "Sensor row ") + (i + 1);
            compactModes[i] = AddRow(rowLabel, UiText.Get("Inhalt", "Content"),
                (text.CompactHideNames ?? new bool[0]).ElementAtOrDefault(i) ? compactIconStatus : compactFull,
                new[] { compactFull, compactIconStatus });
            compactNames[i] = AddRow(rowLabel, UiText.Get("Beschriftung", "Label"), "");
            int font = Array.IndexOf(fontValues, (text.CompactSizes ?? new string[0]).ElementAtOrDefault(i));
            compactSizes[i] = AddRow(rowLabel, UiText.Get("Schriftgröße", "Font size"),
                fontSizes[Math.Max(0, font)], fontSizes);
            int icon = Array.IndexOf(iconValues, (text.CompactIcons ?? new string[0]).ElementAtOrDefault(i));
            compactIcons[i] = AddRow(rowLabel, UiText.Get("Symbol", "Icon"), iconNames[Math.Max(0, icon)], iconNames);
            int placement = (text.Columns ?? new int[0]).ElementAtOrDefault(i);
            columnRows[i] = AddRow(rowLabel, UiText.Get("Spalte (Zwei Spalten)", "Column (Two columns)"),
                columnNames[Math.Max(0, Math.Min(2, placement))], columnNames);
            string rowAction = (text.RowActions ?? new string[0]).ElementAtOrDefault(i);
            rowActions[i] = AddRow(rowLabel, UiText.Get("Schalter oder Taster beim Klick", "Switch or button on click"),
                string.IsNullOrWhiteSpace(rowAction) && i == legacySlot ? oldAction : rowAction);
        }
        name = AddRow(UiText.Get("Kachel", "Widget"), UiText.Get("Kachelname", "Widget name"), profile.Name);
        name.Cells[2].ReadOnly = profile.Id.Length == 0;
        sizeHighlights = Enumerable.Range(0, 4).Select(index =>
            text.HighlightFor(index, index != 3 && profile.HighlightValues)).ToArray();
        description = AddRow(UiText.Get("Kachel", "Widget"), UiText.Get("Inhaltstitel (optional)", "Content title (optional)"),
            text.HideTitle ? "" : text.UseCustomDescription ? text.Description : profile.Id.Length != 0 ? profile.Name : "");
        compactTitle = AddRow(UiText.Get("Kleine Kachel", "Small widget"), UiText.Get("Titel für Klein", "Title for Small"),
            text.HideTitle ? "" : text.CompactTitle);
        CreateOverview();
        ShowOverview();
        var previewHeading = new Label { Text = UiText.Get("Vorschau · Beispielwerte", "Preview · sample values"),
            Bounds = new Rectangle(822, 18, 314, 23), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        previewType.AccessibleName = UiText.Get("Widgettyp für die Vorschau", "Widget type for preview");
        previewType.ForeColor = Color.WhiteSmoke;
        previewType.Bounds = new Rectangle(822, 50, 188, 26);
        previewType.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        previewType.Items.AddRange(new[] { UiText.Get("Schalter und Sensoren", "Switches and sensors"),
            UiText.Get("Schalter", "Switches"), UiText.Get("Sensoren", "Sensors"), UiText.Get("Zwei Spalten", "Two columns") });
        previewType.SelectedIndex = Math.Max(0, Math.Min(3, text.PreferredWidgetType));
        previewSize.AccessibleName = UiText.Get("Vorschaugröße", "Preview size");
        previewSize.ForeColor = Color.WhiteSmoke;
        previewSize.Bounds = new Rectangle(1018, 50, 118, 26);
        previewSize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        previewSize.Items.AddRange(new[] { UiText.Get("Klein", "Small"), UiText.Get("Mittel", "Medium"), UiText.Get("Groß", "Large") });
        previewSize.SelectedIndex = previewType.SelectedIndex == 3 ? 1 : Math.Max(0, Math.Min(2, text.PreferredWidgetSize));
        previewSize.Enabled = previewType.SelectedIndex != 3;
        UpdateOverviewHint();
        preview.Bounds = new Rectangle(822, 89, 314, 310);
        preview.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        preview.AccessibleName = previewHeading.Text;
        preview.Paint += PaintWidgetPreview;
        preview.MouseClick += SelectPreviewRow;
        preview.MouseDown += BeginPreviewDrag;
        preview.MouseUp += EndPreviewDrag;
        previewLayoutLabel.Text = UiText.Get("Darstellung", "Layout");
        previewLayoutLabel.Anchor = previewLayout.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        previewLayout.AccessibleName = previewLayoutLabel.Text;
        previewLayout.ForeColor = Color.WhiteSmoke;
        previewLayout.Items.AddRange(new[] { ProfileLayout(false), ProfileLayout(true) });
        previewLayout.SelectionChangeCommitted += delegate
        {
            sizeHighlights[PreviewLayoutIndex] = previewLayout.SelectedIndex == 1;
            preview.Invalidate();
        };
        previewSize.SelectedIndexChanged += delegate { UpdatePreviewLayout(); };
        previewType.SelectedIndexChanged += delegate
        {
            bool twoColumns = previewType.SelectedIndex == 3;
            if (twoColumns) previewSize.SelectedIndex = 1;
            previewSize.Enabled = !twoColumns;
            if (expandedRow < 0) UpdateOverviewHint();
            UpdatePreviewLayout();
        };
        table.CellValueChanged += RefreshWidgetPreview;
        table.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs args)
        {
            if (expandedRow < 0 || args.ColumnIndex != 2 || args.RowIndex != compactNames[expandedRow].Index) return;
            entities[expandedRow].Cells[4].Value = compactNames[expandedRow].Cells[2].Value;
        };
        table.SelectionChanged += RefreshWidgetPreview;
        table.CurrentCellDirtyStateChanged += delegate
        {
            if (table.IsCurrentCellDirty && table.CurrentCell is DataGridViewComboBoxCell)
                table.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        table.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs args)
        {
            args.Control.TextChanged -= RefreshWidgetPreview;
            args.Control.TextChanged += RefreshWidgetPreview;
        };
        Controls.AddRange(new Control[] { previewHeading, previewType, previewSize, preview, previewLayoutLabel, previewLayout });
        previewLayoutLabel.BringToFront();
        previewLayout.BringToFront();
        widgetSettings.BringToFront();
        UpdatePreviewLayout();

        int footer = ClientSize.Height - 48;
        backFromAppearance = CreateButton(UiText.Get("Zurück", "Back"), 14, footer, 90);
        backFromAppearance.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        backFromAppearance.Visible = false;
        backFromAppearance.Click += delegate { ShowRowAppearance(-1); };
        var cancel = CreateButton(UiText.Get("Abbrechen", "Cancel"), 916, footer, 106);
        var apply = CreateButton(UiText.Get("Übernehmen", "Apply"), 1030, footer, 106);
        backFromAppearance.Height = cancel.Height = apply.Height = 32;
        cancel.Anchor = apply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        cancel.DialogResult = DialogResult.Cancel;
        apply.Primary = true;
        apply.Click += Apply;
        CancelButton = cancel;
        SettingsTheme.ApplyMonitorStyle(this);
    }

    void CreateOverview()
    {
        overview.Bounds = table.Bounds;
        overview.Anchor = table.Anchor;
        overview.AutoScroll = false;
        Disposed += delegate { overviewTips.Dispose(); };
        var list = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = entities.Length + 1 };
        list.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        for (int i = 0; i < entities.Length; i++) list.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / entities.Length));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        for (int i = 0; i < 3; i++) list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 37));
        list.Controls.Add(new Label { Text = UiText.Get("Sensor-ID", "Sensor ID"), AutoSize = true }, 1, 0);
        list.Controls.Add(new Label { Text = UiText.Get("Beschriftung", "Label"), AutoSize = true }, 2, 0);
        for (int i = 0; i < entities.Length; i++)
        {
            int slot = i;
            int row = i + 1;
            list.Controls.Add(new Label { Text = row.ToString(), Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
            list.Controls.Add(OverviewInput(entities[i], 2, UiText.Get("Sensor-ID", "Sensor ID") + " " + row, 255), 1, row);
            list.Controls.Add(OverviewInput(entities[i], 4, UiText.Get("Beschriftung", "Label") + " " + row, 40), 2, row);
            list.Controls.Add(OverviewButton("\uE721", UiText.Get("Sensor auswählen", "Select sensor"),
                delegate { Browse(slot, false); }), 3, row);
            actionButtons[i] = OverviewButton("\uE71B", "", delegate { Browse(slot, true); });
            list.Controls.Add(actionButtons[i], 4, row);
            UpdateActionButton(i);
            list.Controls.Add(OverviewButton("\uE713", UiText.Get("Darstellung", "Appearance"),
                delegate { ShowRowAppearance(slot); }), 5, row);
        }
        widgetSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        widgetSettings.ColumnCount = 2;
        widgetSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        widgetSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var settings = new[] { name, description, compactTitle };
        widgetSettings.RowCount = settings.Length + 1;
        for (int row = 0; row < settings.Length; row++)
        {
            var setting = settings[row];
            var label = new Label { Text = Value(setting, 1), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var field = OverviewInput(setting, 2, label.Text, 40);
            widgetSettings.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            widgetSettings.Controls.Add(label, 0, row);
            widgetSettings.Controls.Add(field, 1, row);
            if (setting == compactTitle)
            {
                smallTitleLabel = label;
                smallTitleInput = field;
            }
        }
        columnDivider.Text = UiText.Get("Trennlinie anzeigen", "Show divider");
        columnDivider.Dock = DockStyle.Fill;
        columnDivider.CheckedChanged += RefreshWidgetPreview;
        widgetSettings.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        widgetSettings.Controls.Add(columnDivider, 0, 3);
        widgetSettings.SetColumnSpan(columnDivider, 2);
        overview.Controls.Add(list);
        Controls.Add(overview);
        Controls.Add(widgetSettings);
    }

    TextBox OverviewInput(DataGridViewRow row, int column, string label, int limit)
    {
        var cell = row.Cells[column];
        var input = new TextBox { Dock = DockStyle.Fill, AccessibleName = label, MaxLength = limit,
            ForeColor = Color.WhiteSmoke, ReadOnly = cell.ReadOnly, Text = Value(row, column) };
        input.TextChanged += delegate { if (input.Text != Convert.ToString(cell.Value)) cell.Value = input.Text; };
        input.Enter += delegate { selectedSlot = Array.IndexOf(entities, row); preview.Invalidate(); };
        table.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs args)
        {
            if (args.RowIndex == row.Index && args.ColumnIndex == column && input.Text != Convert.ToString(cell.Value))
                input.Text = Convert.ToString(cell.Value);
        };
        return input;
    }

    SettingsButton OverviewButton(string icon, string label, EventHandler clicked)
    {
        var button = new SettingsButton { Text = icon, AccessibleName = label, ForeColor = Color.WhiteSmoke,
            Font = new Font("Segoe MDL2 Assets", 11), Size = new Size(32, 25) };
        button.Click += clicked;
        overviewTips.SetToolTip(button, label);
        return button;
    }

    int PreviewLayoutIndex { get { return previewType.SelectedIndex == 3 ? 3 : previewSize.SelectedIndex; } }

    void UpdatePreviewLayout()
    {
        int top = preview.Top + (previewSize.SelectedIndex == 0 ? 154 : 308) + 9;
        previewLayoutLabel.Bounds = new Rectangle(preview.Left, top + 4, 108, 23);
        previewLayout.Bounds = new Rectangle(preview.Left + 112, top, preview.Width - 112, 26);
        widgetSettings.Bounds = new Rectangle(preview.Left, top + 29, preview.Width, 94);
        bool small = previewSize.SelectedIndex == 0;
        bool twoColumns = previewType.SelectedIndex == 3;
        smallTitleLabel.Visible = smallTitleInput.Visible = small;
        columnDivider.Visible = twoColumns;
        widgetSettings.RowStyles[2].Height = small ? 30 : 0;
        widgetSettings.RowStyles[3].Height = twoColumns ? 30 : 0;
        previewLayout.SelectedIndex = sizeHighlights[PreviewLayoutIndex] ? 1 : 0;
        preview.Invalidate();
    }

    void UpdateOverviewHint()
    {
        sectionHint.Text = previewType.SelectedIndex == 3
            ? UiText.Get("Sensorzeilen in der Vorschau ziehen, um Reihenfolge und Spalte zu ändern.",
                "Drag sensor rows in the preview to change their order and column.")
            : UiText.Get("Sensor auswählen, Klickaktion zuweisen oder Darstellung öffnen.",
                "Select a sensor, assign a click action or open appearance settings.");
    }

    void ShowOverview()
    {
        table.CurrentCell = null;
        table.Visible = false;
        overview.Visible = true;
        UpdateOverviewHint();
    }

    void ShowRowAppearance(int slot)
    {
        if (!table.EndEdit()) return;
        bool detail = slot >= 0;
        int previous = expandedRow;
        SuspendLayout();
        expandedRow = slot;
        overview.Visible = !detail;
        table.Visible = detail;
        widgetSettings.Visible = !detail;
        backFromAppearance.Visible = detail;
        if (detail)
        {
            sectionHint.Text = UiText.Get("Darstellung · Sensorzeile ", "Appearance · Sensor row ") + (slot + 1);
            table.CurrentCell = null;
            compactNames[slot].Cells[2].Value = entities[slot].Cells[4].Value;
            foreach (DataGridViewRow row in table.Rows)
                row.Visible = row == compactModes[slot] || row == compactNames[slot] ||
                    row == compactSizes[slot] || row == compactIcons[slot] || row == columnRows[slot];
            table.Columns[3].Visible = table.Columns[4].Visible = false;
            table.CurrentCell = compactModes[slot].Cells[1];
        }
        else
        {
            ShowOverview();
            if (previous >= 0) actionButtons[previous].Focus();
        }
        ResumeLayout();
        if (detail) table.Focus();
        preview.Invalidate();
    }

    protected override void OnShown(EventArgs args)
    {
        base.OnShown(args);
        var work = Screen.FromControl(this).WorkingArea;
        if (Width > work.Width - 20)
        {
            Width = work.Width - 20;
            Left = work.Left + 10;
        }
        LoadPreviewNames();
    }

    async void LoadPreviewNames()
    {
        if (connection == null) return;
        try
        {
            var sensors = await Options.LoadSensors(new Uri(connection.Url), connection.ReadToken());
            if (IsDisposed) return;
            foreach (var sensor in sensors)
                if (!string.IsNullOrWhiteSpace(sensor.entity_id) && sensor.attributes != null &&
                    !string.IsNullOrWhiteSpace(sensor.attributes.friendly_name))
                    previewNames[sensor.entity_id] = sensor.attributes.friendly_name;
            preview.Invalidate();
        }
        catch (Exception)
        {
            // The preview can still use labels and sample values while Home Assistant is offline.
        }
    }

    void RefreshWidgetPreview(object sender, EventArgs args) { preview.Invalidate(); }

    string Draft(DataGridViewRow row, int column = 2)
    {
        return table.CurrentCell != null && table.CurrentCell.OwningRow == row && table.CurrentCell.ColumnIndex == column &&
            table.IsCurrentCellInEditMode && table.EditingControl != null ? table.EditingControl.Text.Trim() : Value(row, column);
    }

    void SelectPreviewRow(object sender, MouseEventArgs args)
    {
        if (suppressPreviewClick) return;
        if (expandedRow >= 0 || args.Button != MouseButtons.Left || !table.EndEdit()) return;
        float x = args.X * 314f / preview.Width, y = args.Y * 310f / preview.Height;
        int slot = Array.FindIndex(previewRows, row => row.Contains(x, y));
        if (slot < 0) return;
        selectedSlot = slot;
        actionButtons[slot].Focus();
        preview.Invalidate();
    }

    PointF PreviewPoint(Point point)
    {
        return new PointF(point.X * 314f / preview.Width, point.Y * 310f / preview.Height);
    }

    void BeginPreviewDrag(object sender, MouseEventArgs args)
    {
        suppressPreviewClick = false;
        if (previewType.SelectedIndex != 3 || expandedRow >= 0 || args.Button != MouseButtons.Left || !table.EndEdit()) return;
        var point = PreviewPoint(args.Location);
        draggedSlot = Array.FindIndex(previewRows, row => row.Contains(point));
        dragStart = args.Location;
    }

    void EndPreviewDrag(object sender, MouseEventArgs args)
    {
        int source = draggedSlot;
        draggedSlot = -1;
        if (source < 0 || (Math.Abs(args.X - dragStart.X) < SystemInformation.DragSize.Width / 2 &&
            Math.Abs(args.Y - dragStart.Y) < SystemInformation.DragSize.Height / 2)) return;
        suppressPreviewClick = true;
        if (!preview.ClientRectangle.Contains(args.Location)) return;
        var point = PreviewPoint(args.Location);
        var slots = Enumerable.Range(0, previewRows.Length).Where(i => !previewRows[i].IsEmpty).ToArray();
        var left = slots.Where(i => i != source && previewRows[i].X < 150).ToList();
        var right = slots.Where(i => i != source && previewRows[i].X >= 150).ToList();
        var target = point.X < 157 ? left : right;
        if (target.Count >= BoardSelection.TwoColumnCapacity / 2) return;
        int position = target.FindIndex(i => point.Y < previewRows[i].Y + previewRows[i].Height / 2);
        target.Insert(position < 0 ? target.Count : position, source);

        var order = left.Concat(right).ToArray();
        var fields = order.Select(i => new[] { Value(entities[i]), Value(entities[i], 4), Value(compactModes[i]),
            Value(compactSizes[i]), Value(compactIcons[i]), Value(rowActions[i]) }).ToArray();
        for (int i = 0; i < slots.Length; i++)
        {
            int slot = slots[i];
            entities[slot].Cells[2].Value = fields[i][0];
            entities[slot].Cells[4].Value = fields[i][1];
            compactModes[slot].Cells[2].Value = fields[i][2];
            compactSizes[slot].Cells[2].Value = fields[i][3];
            compactIcons[slot].Cells[2].Value = fields[i][4];
            rowActions[slot].Cells[2].Value = fields[i][5];
            columnRows[slot].Cells[2].Value = columnNames[i < left.Count ? 1 : 2];
            UpdateActionButton(slot);
        }
        selectedSlot = slots[Array.IndexOf(order, source)];
        preview.Invalidate();
    }

    void PaintWidgetPreview(object sender, PaintEventArgs args)
    {
        var graphics = args.Graphics;
        graphics.ScaleTransform(preview.Width / 314f, preview.Height / 310f);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        bool small = previewSize.SelectedIndex == 0;
        bool highlighted = sizeHighlights[PreviewLayoutIndex];
        int height = small ? 154 : 308;
        using (var outline = SettingsTheme.Rounded(new RectangleF(1, 1, 312, height), 10))
        using (var background = new SolidBrush(Color.FromArgb(48, 48, 48)))
            graphics.FillPath(background, outline);
        DrawPreviewText(graphics, "Home Assistant",
            new RectangleF(14, 10, 280, 22), 14, Color.WhiteSmoke);
        Array.Clear(previewRows, 0, previewRows.Length);
        bool twoColumns = previewType.SelectedIndex == 3;
        string actionId = previewType.SelectedIndex == 2 ? "" : legacyAction;
        var ids = entities.Select(row => Draft(row)).Select(id =>
        {
            if (!BoardSelection.IsSupported(id) || id == actionId) return "";
            bool control = BoardSelection.IsSwitch(id) || BoardSelection.IsButton(id);
            return previewType.SelectedIndex == 1 && !control || previewType.SelectedIndex == 2 && control ? "" : id;
        }).ToArray();
        int limit = twoColumns ? BoardSelection.TwoColumnCapacity : small ? BoardSelection.SmallCapacity : previewSize.SelectedIndex == 2
            ? BoardSelection.Capacity : BoardSelection.MediumCapacity;
        bool contacts = ids.Any(IsPreviewContact);
        string title = small ? Draft(compactTitle) : Draft(description);
        if (small && !contacts && title.Length == 0) title = Draft(description);
        float y = 35;
        if (title.Length != 0)
        {
            DrawPreviewText(graphics, title, new RectangleF(14, y, 280, 17), 13, Color.WhiteSmoke, true);
            y += 18;
        }
        int count = Math.Min(limit, ids.Count(id => id.Length != 0));
        bool dense = !twoColumns && !small && count > BoardSelection.SmallCapacity;
        var right = twoColumns ? BoardWidgetText.RightColumns(Enumerable.Range(0, ids.Length)
            .Where(i => ids[i].Length != 0).Take(count).ToArray(), columnRows.Select(row =>
                Math.Max(0, Array.IndexOf(columnNames, Draft(row)))).ToArray()) : new bool[count];
        float firstRowY = y;
        var columnY = new[] { y, y };
        int columnWidth = twoColumns ? 148 : 296;
        int shown = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[i];
            if (id.Length == 0) continue;
            if (shown++ == count) break;
            int column = right[shown - 1] ? 1 : 0;
            int columnX = column * 148;
            y = columnY[column];
            bool contact = IsPreviewContact(id);
            bool hideName = Draft(compactModes[i]) == compactIconStatus;
            string customCaption = expandedRow == i ? Draft(compactNames[i]) : Draft(entities[i], 4);
            string caption = customCaption;
            if (caption.Length == 0 && !previewNames.TryGetValue(id, out caption)) caption = PreviewEntityName(id);
            string rowSize = Draft(compactSizes[i]);
            string icon = iconValues[Math.Max(0, Array.IndexOf(iconNames, Draft(compactIcons[i])))];
            bool customStyle = hideName || rowSize != fontSizes[0] || (small && icon.Length != 0);
            float fontSize = rowSize == fontSizes[2] || (dense && rowSize == fontSizes[0] && !highlighted) ? 12
                : rowSize == fontSizes[0] && highlighted && (dense || twoColumns) ? 18
                : rowSize == fontSizes[3] || (small && rowSize == fontSizes[0] && highlighted && contact) ? 19
                : previewSize.SelectedIndex == 2 ? 16 : 14;
            bool stacked = twoColumns || (!dense && !customStyle && (!small ||
                (highlighted && count == 1 && !contact && customCaption.Length == 0)));
            bool standardStack = twoColumns || (stacked && !small && !highlighted);
            float rowHeight = twoColumns ? 36 : stacked ? (standardStack ? 38 : 52) : fontSize + 9;
            var row = new RectangleF(9 + columnX, y, columnWidth, rowHeight);
            previewRows[i] = row;
            var selected = table.CurrentRow;
            if (selectedSlot == i && expandedRow < 0 || expandedRow == i || selected == compactModes[i] ||
                selected == compactSizes[i] || selected == compactIcons[i])
                using (var highlight = new Pen(Color.FromArgb(125, 156, 209, 253)))
                    graphics.DrawRectangle(highlight, row.X, row.Y, row.Width, row.Height);
            Color ink = contact && !standardStack ? Color.FromArgb(143, 243, 216) : Color.FromArgb(132, 216, 242);
            string effectiveIcon = icon.Length == 0 && contact ? BoardSelection.ContactIconFromId(id) : icon;
            if (effectiveIcon.Length == 0 && BoardSelection.IsSwitch(id))
                effectiveIcon = id.StartsWith("light.", StringComparison.Ordinal) ? "lightbulb" : "toggle-switch-off";
            var state = graphics.Save();
            float iconSize = standardStack ? 18 : small ? (fontSize >= 19 ? 25 : fontSize <= 12 ? 18 : 22) : 22;
            graphics.TranslateTransform(14 + columnX, y + (rowHeight - iconSize) / 2);
            graphics.ScaleTransform(iconSize / 32f, iconSize / 32f);
            if (effectiveIcon == "garage" || effectiveIcon == "door" || effectiveIcon == "window-closed")
                DoorIcons.Draw(graphics, effectiveIcon == "garage" ? "Garagentor" : effectiveIcon == "door" ? "Tür" : "Fenster", false, ink);
            else if (DoorIcons.IsControlIcon(effectiveIcon))
                DoorIcons.DrawControlIcon(graphics, effectiveIcon == "toggle-switch" ? "toggle-switch-off" : effectiveIcon, ink);
            else if (icon == "battery" || icon == "solar-panel") SolarIcon.Draw(graphics, icon == "battery" ? 1 : 0, ink);
            else if (icon != "none") SolarIcon.Draw(graphics, 3, ink);
            graphics.Restore(state);
            string example = PreviewExample(id, contact);
            float left = icon == "none" ? 15 : standardStack ? 39 : 44;
            float valueX = stacked || hideName ? left : fontSize >= 19 ? 158 : 197;
            int labelHeight = standardStack ? 16 : 22;
            if (!hideName)
                DrawPreviewText(graphics, caption, new RectangleF(left + columnX, y, stacked ? columnWidth - 2 - left : valueX - left - 4, stacked ? labelHeight : rowHeight),
                    standardStack ? fontSize - 2 : fontSize, Color.WhiteSmoke);
            DrawPreviewText(graphics, example, new RectangleF(valueX + columnX, stacked && !hideName ? y + labelHeight : y,
                columnWidth + 1 - valueX, stacked && !hideName ? rowHeight - labelHeight : rowHeight),
                stacked && highlighted && !twoColumns ? 25 : fontSize, standardStack ? Color.WhiteSmoke : contact ? Color.FromArgb(108, 202, 100) : ink, true,
                !stacked && !hideName ? StringAlignment.Far : StringAlignment.Near);
            columnY[column] += rowHeight + 4;
        }
        if (twoColumns && columnDivider.Checked && right.Any(value => value) && right.Any(value => !value))
            using (var divider = new Pen(Color.Gainsboro))
                graphics.DrawLine(divider, 155, firstRowY, 155, Math.Max(columnY[0], columnY[1]) - 4);
        if (count == 0)
            DrawPreviewText(graphics, UiText.Get("Wähle einen Sensor aus.", "Choose a sensor."),
                new RectangleF(14, y, 276, 28), 13, Color.Gainsboro);
    }

    static bool IsPreviewContact(string id)
    {
        return id.StartsWith("binary_sensor.", StringComparison.OrdinalIgnoreCase) &&
            BoardSelection.ContactIconFromId(id).Length != 0;
    }

    static string PreviewEntityName(string id)
    {
        if (id.IndexOf("garagentor", StringComparison.OrdinalIgnoreCase) >= 0) return UiText.Get("Garagentor", "Garage door");
        if (id.IndexOf("kellertuer", StringComparison.OrdinalIgnoreCase) >= 0) return UiText.Get("Kellertür", "Basement door");
        string name = id.Substring(id.IndexOf('.') + 1).Replace('_', ' ');
        return name.Length == 0 ? id : char.ToUpper(name[0]) + name.Substring(1);
    }

    static string PreviewExample(string id, bool contact)
    {
        if (contact) return UiText.Get("Geschlossen", "Closed");
        if (BoardSelection.IsButton(id)) return UiText.Get("Taster", "Button");
        if (BoardSelection.IsSwitch(id) || id.StartsWith("binary_sensor.", StringComparison.Ordinal)) return UiText.Get("Aus", "Off");
        if (id.IndexOf("temp", StringComparison.OrdinalIgnoreCase) >= 0) return UiText.Get("21,5 °C", "21.5 °C");
        return "42";
    }

    internal static string ProfileLayout(bool highlight)
    {
        return highlight ? UiText.Get("Große Werte", "Large values") : UiText.Get("Liste", "List");
    }

    DataGridViewRow AddRow(string area, string property, string value, string[] options = null, bool browse = false)
    {
        var row = table.Rows[table.Rows.Add(area, property, value ?? "", "")];
        row.Cells[4].ReadOnly = true;
        row.Cells[0].Style.ForeColor = Color.Silver;
        row.Cells[2].ToolTipText = value ?? "";
        if (options != null)
        {
            var combo = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
            combo.Items.AddRange(options);
            row.Cells[2] = combo;
            combo.Value = value;
        }
        else
        {
            ((DataGridViewTextBoxCell)row.Cells[2]).MaxInputLength = browse ? 255 : profile == null ? 32 : 40;
        }
        return row;
    }

    async void Browse(int slot, bool action)
    {
        if (browsing) return;
        selectedSlot = slot;
        preview.Invalidate();
        var row = action ? rowActions[slot] : entities[slot];
        if (connection == null)
        {
            MessageBox.Show(this, UiText.Get("Bitte zuerst mit Home Assistant verbinden.",
                "Connect to Home Assistant first."), Text);
            return;
        }
        table.EndEdit();
        browsing = true;
        try
        {
            var sensors = await Options.LoadSensors(new Uri(connection.Url), connection.ReadToken());
            if (IsDisposed) return;
            string mode = action ? "board_action" : "board";
            using (var picker = new Options.SensorPicker(sensors, mode, Value(row), action))
            {
                var result = picker.ShowDialog(this);
                if (result == DialogResult.OK) row.Cells[2].Value = picker.SelectedEntity;
                else if (action && result == DialogResult.Abort) row.Cells[2].Value = "";
                if (action) UpdateActionButton(slot);
            }
        }
        catch (Exception)
        {
            if (!IsDisposed) MessageBox.Show(this, UiText.Get("Sensoren konnten nicht geladen werden. Bitte Verbindung prüfen.",
                "Could not load sensors. Check the connection."), Text);
        }
        finally { browsing = false; }
    }

    void UpdateActionButton(int slot)
    {
        string action = Value(rowActions[slot]);
        var button = actionButtons[slot];
        button.AccessibleName = action.Length == 0 ? UiText.Get("Aktion hinzufügen", "Add action")
            : UiText.Get("Aktion ändern…", "Change action…");
        overviewTips.SetToolTip(button, action.Length == 0 ? button.AccessibleName : button.AccessibleName + " · " + action);
    }

    static string Value(DataGridViewRow row, int column = 2)
    {
        return Convert.ToString(row.Cells[column].Value).Trim();
    }

    void Apply(object sender, EventArgs args)
    {
        if (!table.EndEdit()) return;
        var selected = entities.Select(row => Value(row).ToLowerInvariant()).ToArray();
        var columns = columnRows.Select(row => Math.Max(0, Array.IndexOf(columnNames, Value(row)))).ToArray();
        if (Enumerable.Range(0, selected.Length).Where(i => selected[i].Length != 0 && columns[i] != 0)
            .GroupBy(i => columns[i]).Any(group => group.Count() > BoardSelection.TwoColumnCapacity / 2))
        {
            MessageBox.Show(this, UiText.Get("Bitte höchstens sechs Einträge pro Spalte fest zuordnen.",
                "Assign no more than six entries to each column."), Text);
            return;
        }
        if (Value(name).Length == 0 || selected.Any(entity => entity.Length != 0 && !BoardSelection.IsSupported(entity)))
        {
            if (expandedRow >= 0) ShowRowAppearance(-1);
            ShowOverview();
            MessageBox.Show(this, UiText.Get("Bitte einen Namen und gültige Entitäts-IDs eingeben.",
                "Enter a name and valid entity IDs."), Text);
            return;
        }
        var actions = rowActions.Select(row => Value(row).ToLowerInvariant()).ToArray();
        int invalidAction = Array.FindIndex(actions, id => id.Length != 0 && !BoardSelection.IsSwitch(id) && !BoardSelection.IsButton(id));
        if (invalidAction >= 0)
        {
            if (expandedRow >= 0) ShowRowAppearance(-1);
            ShowOverview();
            actionButtons[invalidAction].Focus();
            MessageBox.Show(this, UiText.Get("Bitte einen gültigen Schalter oder Taster auswählen.",
                "Select a valid switch or button."), Text);
            return;
        }
        profile.Name = Value(name);
        profile.HighlightValues = sizeHighlights[1];
        profile.Entities = selected;
        profile.WidgetText = new BoardWidgetText { UseCustomDescription = true, Description = Value(description),
            Columns = columns, SizeHighlights = sizeHighlights, ShowColumnDivider = columnDivider.Checked,
            PreferredWidgetType = previewType.SelectedIndex, PreferredWidgetSize = previewSize.SelectedIndex,
            ActionEntity = legacyAction, ActionCaption = legacyActionCaption, RowActions = actions,
            CompactTitle = Value(compactTitle), CompactHideNames = compactModes.Select(row => Value(row) == compactIconStatus).ToArray(),
            CompactCaptions = entities.Select(row => Value(row, 4)).ToArray(),
            CompactSizes = compactSizes.Select(row => fontValues[Math.Max(0, Array.IndexOf(fontSizes, Value(row)))]).ToArray(),
            CompactIcons = compactIcons.Select(row => iconValues[Math.Max(0, Array.IndexOf(iconNames, Value(row)))]).ToArray() };
        DialogResult = DialogResult.OK;
    }
}

sealed class SolarBoardProperties : BoardSettingsWindow
{
    const int SensorColumn = 2, CaptionColumn = 3, DecimalsColumn = 4, PickerColumn = 5;
    readonly Settings connection;
    readonly BoardConfiguration config;
    readonly SolarBoardProfile profile;
    readonly SolarBoardSettings solar;
    readonly DataGridView table;
    readonly TextBox title, dailyTitle;
    readonly SolarEditorPreview preview;
    readonly CheckBox previewIcons;
    readonly string[] initialLive;
    readonly string automatic = UiText.Get("Automatisch", "Automatic");
    bool browsing;

    internal SolarBoardProperties(Settings connection, BoardConfiguration config, SolarBoardProfile profile)
        : base(UiText.Get("Eigenschaften", "Properties") + " – " +
            (profile == null ? config.SolarDisplayName() : profile.Name), new Size(1364, 436))
    {
        this.connection = connection;
        this.config = config;
        this.profile = profile;
        solar = profile == null ? config : profile.Solar ?? new SolarBoardSettings();
        initialLive = (profile == null
            ? config.DefaultSolarSensors(connection == null ? null : connection.Entities, connection == null ? null : connection.GridExportEntity)
            : Enumerable.Range(0, 4).Select(i => (profile.Entities ?? new string[0]).ElementAtOrDefault(i))
                .Concat(new[] { profile.GridExportEntity }).ToArray())
            .Select(value => (value ?? "").Trim()).ToArray();

        title = AddTitle(UiText.Get("Name der Kachel", "Widget name"), profile == null ? config.DefaultSolarName : profile.Name, 14);
        dailyTitle = AddTitle(UiText.Get("Titel der Tageswerte", "Daily totals heading"),
            (solar.SolarCaptions ?? new string[0]).ElementAtOrDefault(BoardSelection.SolarLabels.Length), 521);
        Controls.Add(new Label
        {
            Text = UiText.Get("Sensor-ID eingeben oder Sensor wählen. Leere Beschriftungen verwenden die Standardtexte.",
                "Enter an entity ID or choose a sensor. Empty labels use the defaults."),
            ForeColor = Color.Silver, Bounds = new Rectangle(14, 48, 1012, 24)
        });

        table = CreateTable(new Rectangle(14, 76, 1012, 310));
        table.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        table.Columns.Add(TextColumn(UiText.Get("Bereich", "Category"), 10));
        table.Columns.Add(TextColumn(UiText.Get("Messwert", "Reading"), 18));
        table.Columns.Add(TextColumn(UiText.Get("Sensor-ID", "Sensor ID"), 44));
        table.Columns.Add(TextColumn(UiText.Get("Beschriftung", "Label"), 28));
        table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = UiText.Get("Nachkommastellen", "Decimal places"),
            Width = 128, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.NotSortable });
        table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "", Width = 122, ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.NotSortable });
        table.Columns[0].ReadOnly = table.Columns[1].ReadOnly = true;
        table.Columns[0].MinimumWidth = 58;
        table.Columns[1].MinimumWidth = 102;
        table.Columns[2].MinimumWidth = 218;
        table.Columns[3].MinimumWidth = 110;
        table.EditMode = DataGridViewEditMode.EditOnEnter;
        table.StandardTab = false;
        table.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs args)
        {
            args.Control.BackColor = SettingsTheme.MonitorInput;
            args.Control.ForeColor = Color.WhiteSmoke;
        };
        table.CellContentClick += Browse;
        Controls.Add(table);
        for (int i = 0; i < initialLive.Length; i++)
        {
            string label = i < 3 ? BoardSelection.SolarLabels[i] : i == 3
                ? UiText.Get("Netz / Bezug", "Grid / import") : UiText.Get("Einspeisung einzeln", "Separate export");
            AddSensor(UiText.Get("Live", "Live"), label, initialLive[i], i == 3 ? BoardSelection.SolarGridCaptionIndex : i,
                solar.DecimalPlaces(i), i == 2 ? "%" : "W", i != 4);
        }
        for (int i = 0; i < BoardSelection.SolarDailyLabels.Length; i++)
            AddSensor(UiText.Get("Tageswert", "Daily total"), BoardSelection.SolarDailyLabels[i],
                (solar.SolarDailyEntities ?? new string[0]).ElementAtOrDefault(i),
                BoardSelection.SolarLabels.Length + 1 + i, solar.DailyDecimalPlaces(i), "daily_energy", true);

        UpdateSharedGridDisplay();
        table.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs args)
        {
            if (args.RowIndex == 3 && (args.ColumnIndex == CaptionColumn || args.ColumnIndex == DecimalsColumn))
                UpdateSharedGridDisplay();
        };

        var previewHeading = new Label { Text = UiText.Get("Vorschau · Beispielwerte", "Preview · sample values"),
            Bounds = new Rectangle(1042, 48, 214, 23), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        previewIcons = new CheckBox { Text = UiText.Get("Mit Icons", "With icons"), Checked = solar.SolarShowIcons == true,
            Bounds = new Rectangle(1263, 46, 87, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        preview = new SolarEditorPreview(PaintPreview)
        {
            Bounds = new Rectangle(1042, 76, 308, 310), Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AccessibleName = UiText.Get("Solarkachel-Vorschau", "Solar widget preview")
        };
        Controls.AddRange(new Control[] { previewHeading, previewIcons, preview });
        preview.MouseClick += SelectSolarPreviewRow;
        title.TextChanged += RefreshPreview;
        dailyTitle.TextChanged += RefreshPreview;
        previewIcons.CheckedChanged += RefreshPreview;
        table.SelectionChanged += RefreshPreview;
        table.CellValueChanged += RefreshPreview;
        table.CurrentCellDirtyStateChanged += delegate
        {
            if (table.IsCurrentCellDirty && table.CurrentCell is DataGridViewComboBoxCell)
                table.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        table.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs args)
        {
            args.Control.TextChanged -= RefreshPreview;
            args.Control.TextChanged += RefreshPreview;
        };
        var cancel = CreateButton(UiText.Get("Abbrechen", "Cancel"), 1130, 395, 106);
        var apply = CreateButton(UiText.Get("Übernehmen", "Apply"), 1244, 395, 106);
        cancel.Anchor = apply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        cancel.DialogResult = DialogResult.Cancel;
        apply.Primary = true;
        apply.Click += Apply;
        CancelButton = cancel;
        SettingsTheme.ApplyMonitorStyle(this);
    }

    protected override void OnShown(EventArgs args)
    {
        base.OnShown(args);
        var work = Screen.FromControl(this).WorkingArea;
        if (Width > work.Width - 20)
        {
            Width = work.Width - 20;
            Left = work.Left + 10;
        }
    }

    void RefreshPreview(object sender, EventArgs args) { preview.Invalidate(); }

    void SelectSolarPreviewRow(object sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left || !table.EndEdit()) return;
        float x = args.X * 308f / preview.Width;
        float y = args.Y * 310f / preview.Height - (string.IsNullOrWhiteSpace(title.Text) ? 42 : 62);
        bool daily = x >= 175;
        if (daily) y -= 23;
        if (y < 0) return;
        int index = (int)(y / (daily ? 45 : 51));
        if (index >= 4) return;
        table.CurrentCell = table.Rows[index + (daily ? 5 : 0)].Cells[SensorColumn];
        table.Focus();
    }

    string PreviewValue(int row, int column)
    {
        return table.IsCurrentCellInEditMode && table.CurrentCell.RowIndex == row &&
            table.CurrentCell.ColumnIndex == column && table.EditingControl != null
            ? table.EditingControl.Text.Trim() : Value(table.Rows[row], column);
    }

    void PaintPreview(Graphics graphics, Size size)
    {
        graphics.ScaleTransform(size.Width / 308f, size.Height / 310f);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var outline = SettingsTheme.Rounded(new RectangleF(1, 1, 306, 308), 10))
        using (var background = new SolidBrush(Color.FromArgb(48, 48, 48)))
            graphics.FillPath(background, outline);
        bool icons = previewIcons.Checked;
        string widgetName = UiText.Get("Solaranlage", "Solar installation");
        DrawPreviewText(graphics, widgetName, new RectangleF(14, 12, 276, 22), 14, Color.WhiteSmoke);
        int top = 42;
        if (!string.IsNullOrWhiteSpace(title.Text))
        {
            DrawPreviewText(graphics, title.Text.Trim(), new RectangleF(14, top, 276, 18), 13, Color.WhiteSmoke, true);
            top += 20;
        }
        using (var line = new Pen(Color.FromArgb(151, 151, 151)))
            graphics.DrawLine(line, 172, top, 172, top + 207);
        string today = string.IsNullOrWhiteSpace(dailyTitle.Text) ? BoardSelection.SolarCaptionDefaults[3] : dailyTitle.Text.Trim();
        DrawPreviewText(graphics, today, new RectangleF(181, top, 110, 19), 12, Color.Gainsboro, true);

        int selected = table.CurrentCell == null ? -1 : table.CurrentCell.RowIndex;
        if (selected == 4) selected = 3;
        Color[] colors = { Color.FromArgb(255, 229, 92), Color.FromArgb(132, 216, 242), Color.FromArgb(143, 243, 216), Color.WhiteSmoke };
        decimal[] live = { 4.39m, 883m, 84m, -3.57m };
        string[] units = { "kW", "W", "%", "kW" };
        decimal[] daily = { 21.96m, 6.53m, 4.67m, 0.04m };
        int[] metricIcons = { 0, 2, 1, 3 };
        var culture = CultureInfo.GetCultureInfo(UiText.Get("de-DE", "en-US"));
        for (int i = 0; i < 4; i++)
        {
            int liveY = top + i * 51;
            int dailyY = top + 23 + i * 45;
            using (var marker = new SolidBrush(Color.FromArgb(60, 156, 209, 253)))
            {
                if (selected == i) graphics.FillRectangle(marker, 8, liveY - 1, 158, 47);
                if (selected == i + 5) graphics.FillRectangle(marker, 177, dailyY - 1, 121, 41);
            }
            int left = icons ? 41 : 14;
            DrawPreviewText(graphics, PreviewCaption(i, i == 3 ? BoardSelection.SolarGridCaptionIndex : i),
                new RectangleF(left, liveY, 166 - left, 16), 12, Color.WhiteSmoke);
            DrawPreviewText(graphics, Example(i, live[i], units[i], culture),
                new RectangleF(left, liveY + 14, 166 - left, 31), 23, colors[i], true);
            if (icons)
            {
                var state = graphics.Save();
                graphics.TranslateTransform(14, liveY + 20);
                graphics.ScaleTransform(0.625f, 0.625f);
                SolarIcon.Draw(graphics, metricIcons[i], colors[i]);
                graphics.Restore(state);
            }
            DrawPreviewText(graphics, PreviewCaption(i + 5, 4 + i),
                new RectangleF(181, dailyY, 116, 16), 12, Color.WhiteSmoke);
            DrawPreviewText(graphics, Example(i + 5, daily[i], "kWh", culture),
                new RectangleF(181, dailyY + 15, 116, 19), 14, Color.WhiteSmoke, true);
        }
        DrawPreviewText(graphics, UiText.Get("Stand: ", "Updated: ") + "14:15",
            new RectangleF(14, top + 214, 276, 18), 12, Color.Gainsboro);
    }

    string PreviewCaption(int row, int index)
    {
        string value = PreviewValue(row, CaptionColumn);
        return value.Length == 0 ? BoardSelection.SolarCaptionDefaults[index] : value;
    }

    string Example(int row, decimal value, string unit, CultureInfo culture)
    {
        if (PreviewValue(row, SensorColumn).Length == 0) return "—";
        int digits;
        string format = unit == "W" ? "F0" : int.TryParse(PreviewValue(row, DecimalsColumn), out digits)
            ? "F" + digits : unit == "kW" ? "0.###" : "0.##";
        return value.ToString(format, culture) + " " + unit;
    }

    sealed class SolarEditorPreview : Panel
    {
        readonly Action<Graphics, Size> paint;
        internal SolarEditorPreview(Action<Graphics, Size> paint) { this.paint = paint; DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs args) { base.OnPaint(args); paint(args.Graphics, ClientSize); }
    }

    TextBox AddTitle(string label, string value, int x)
    {
        Controls.Add(new Label { Text = label, Bounds = new Rectangle(x, 18, 145, 22) });
        var field = new TextBox { Text = value ?? "", AccessibleName = label, MaxLength = 40,
            Bounds = new Rectangle(x + 148, 14, 342, 24) };
        Controls.Add(field);
        return field;
    }

    void AddSensor(string area, string label, string sensor, int captionIndex, int? places, string picker, bool editableDisplay)
    {
        var row = table.Rows[table.Rows.Add(area, label, sensor ?? "", "", "", "")];
        row.Tag = picker;
        ((DataGridViewTextBoxCell)row.Cells[SensorColumn]).MaxInputLength = 255;
        row.Cells[PickerColumn] = new DataGridViewButtonCell
        {
            FlatStyle = FlatStyle.Flat, Value = UiText.Get("Sensor wählen…", "Choose sensor…")
        };
        if (!editableDisplay)
        {
            row.Cells[CaptionColumn].ReadOnly = row.Cells[DecimalsColumn].ReadOnly = true;
            return;
        }
        row.Cells[CaptionColumn].Value = (solar.SolarCaptions ?? new string[0]).ElementAtOrDefault(captionIndex) ?? "";
        row.Cells[CaptionColumn].ToolTipText = UiText.Get("Standard: ", "Default: ") + BoardSelection.SolarCaptionDefaults[captionIndex];
        ((DataGridViewTextBoxCell)row.Cells[CaptionColumn]).MaxInputLength = 32;
        var combo = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
        combo.Items.Add(automatic);
        for (int i = 0; i <= BoardSelection.MaxDecimalPlaces; i++) combo.Items.Add(i.ToString());
        row.Cells[DecimalsColumn] = combo;
        combo.Value = places.HasValue ? places.Value.ToString() : automatic;
    }

    static string Value(DataGridViewRow row, int column) { return Convert.ToString(row.Cells[column].Value).Trim(); }

    void UpdateSharedGridDisplay()
    {
        var grid = table.Rows[3];
        var export = table.Rows[4];
        string caption = Value(grid, CaptionColumn);
        export.Cells[CaptionColumn].Value = caption.Length == 0
            ? BoardSelection.SolarCaptionDefaults[BoardSelection.SolarGridCaptionIndex] : caption;
        export.Cells[DecimalsColumn].Value = grid.Cells[DecimalsColumn].Value;
        string hint = UiText.Get("Für Bezug und Einspeisung gemeinsam. In „Netz / Bezug“ ändern.",
            "Shared by import and export. Change this in Grid / import.");
        export.Cells[CaptionColumn].ToolTipText = hint;
        export.Cells[DecimalsColumn].ToolTipText = hint;
    }

    async void Browse(object sender, DataGridViewCellEventArgs args)
    {
        if (browsing || args.RowIndex < 0 || args.ColumnIndex != PickerColumn || !table.EndEdit()) return;
        if (connection == null)
        {
            MessageBox.Show(this, UiText.Get("Bitte zuerst mit Home Assistant verbinden.", "Connect to Home Assistant first."), Text);
            return;
        }
        var row = table.Rows[args.RowIndex];
        browsing = true;
        try
        {
            var sensors = await Options.LoadSensors(new Uri(connection.Url), connection.ReadToken());
            if (IsDisposed) return;
            using (var picker = new Options.SensorPicker(sensors, (string)row.Tag, Value(row, SensorColumn)))
                if (picker.ShowDialog(this) == DialogResult.OK) row.Cells[SensorColumn].Value = picker.SelectedEntity;
        }
        catch (Exception)
        {
            if (!IsDisposed) MessageBox.Show(this, UiText.Get("Sensoren konnten nicht geladen werden. Bitte Verbindung prüfen.",
                "Could not load sensors. Check the connection."), Text);
        }
        finally { browsing = false; }
    }

    void Apply(object sender, EventArgs args)
    {
        if (!table.EndEdit() || browsing) return;
        var rows = table.Rows.Cast<DataGridViewRow>().ToArray();
        var ids = rows.Select(row => Value(row, SensorColumn).ToLowerInvariant()).ToArray();
        if (ids.Any(id => id.Length != 0 && !BoardSelection.IsSolarDailySensor(id)) ||
            (ids[4].Length != 0 && ids[3].Length == 0) || (profile != null && string.IsNullOrWhiteSpace(title.Text)))
        {
            MessageBox.Show(this, UiText.Get("Bitte gültige Sensor-IDs angeben. Ein separater Einspeisesensor benötigt einen Bezugssensor. Zusätzliche Kacheln benötigen einen Namen.",
                "Enter valid sensor IDs. Separate export requires an import sensor. Additional widgets require a name."), Text);
            return;
        }
        var live = ids.Take(initialLive.Length).ToArray();
        if (profile != null)
        {
            profile.Name = title.Text.Trim();
            profile.Entities = live.Take(4).ToArray();
            profile.GridExportEntity = live[4];
            profile.Solar = solar;
        }
        else
        {
            config.DefaultSolarName = title.Text.Trim();
            if (!live.SequenceEqual(initialLive, StringComparer.OrdinalIgnoreCase)) config.SolarLiveEntities = live;
        }
        var decimals = rows.Select(row => {
            int places;
            return int.TryParse(Value(row, DecimalsColumn), out places) ? (int?)places : null;
        }).ToArray();
        solar.SolarDecimals = decimals.Take(4).ToArray();
        solar.SolarDailyDecimals = decimals.Skip(initialLive.Length).ToArray();
        solar.SolarDailyEntities = ids.Skip(initialLive.Length).ToArray();
        var captions = new string[BoardSelection.SolarCaptionDefaults.Length];
        for (int i = 0; i < 3; i++) captions[i] = Value(rows[i], CaptionColumn);
        captions[3] = dailyTitle.Text.Trim();
        captions[BoardSelection.SolarGridCaptionIndex] = Value(rows[3], CaptionColumn);
        for (int i = 0; i < BoardSelection.SolarDailyLabels.Length; i++)
            captions[4 + i] = Value(rows[initialLive.Length + i], CaptionColumn);
        solar.SolarCaptions = captions;
        solar.SolarShowIcons = previewIcons.Checked;
        DialogResult = DialogResult.OK;
    }
}

abstract class BoardSettingsWindow : Form
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        SettingsTheme.UseRoundedWindowCorners(Handle);
    }

    protected BoardSettingsWindow(string title, Size size)
    {
        Text = title;
        Icon = WidgetIcon.Load();
        Font = new Font("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        ClientSize = size;
        BackColor = SettingsTheme.MonitorSurface;
        ForeColor = Color.WhiteSmoke;
    }

    protected static void DrawPreviewText(Graphics graphics, string text, RectangleF bounds, float size, Color color,
        bool bold = false, StringAlignment alignment = StringAlignment.Near)
    {
        using (var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(color))
        using (var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter,
            LineAlignment = StringAlignment.Center, Alignment = alignment
        })
            graphics.DrawString(text, font, brush, bounds, format);
    }

    protected static DataGridView CreateTable(Rectangle bounds)
    {
        var table = new DataGridView
        {
            Bounds = bounds, BackgroundColor = SettingsTheme.MonitorSurface, GridColor = SettingsTheme.MonitorBorder,
            BorderStyle = BorderStyle.FixedSingle, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, AllowUserToResizeColumns = false, MultiSelect = false,
            RowHeadersVisible = false, EnableHeadersVisualStyles = false, StandardTab = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 31, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
        };
        table.RowTemplate.Height = 28;
        table.DefaultCellStyle.BackColor = SettingsTheme.MonitorInput;
        table.AlternatingRowsDefaultCellStyle.BackColor = SettingsTheme.MonitorSurface;
        table.DefaultCellStyle.ForeColor = Color.WhiteSmoke;
        table.DefaultCellStyle.SelectionBackColor = Color.FromArgb(48, 62, 80);
        table.DefaultCellStyle.SelectionForeColor = Color.White;
        table.DefaultCellStyle.Padding = new Padding(5, 0, 3, 0);
        table.ColumnHeadersDefaultCellStyle.BackColor = SettingsTheme.MonitorButton;
        table.ColumnHeadersDefaultCellStyle.ForeColor = Color.WhiteSmoke;
        table.ColumnHeadersDefaultCellStyle.SelectionBackColor = SettingsTheme.MonitorButton;
        table.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 0, 0);
        table.ColumnHeadersDefaultCellStyle.Font = new Font(table.Font, FontStyle.Bold);
        return table;
    }

    protected static DataGridViewTextBoxColumn TextColumn(string heading, int weight)
    {
        return new DataGridViewTextBoxColumn { HeaderText = heading, FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.NotSortable };
    }

    protected SettingsButton CreateButton(string label, int x, int y, int width)
    {
        var button = new SettingsButton { Text = label, Bounds = new Rectangle(x, y, width, 28) };
        Controls.Add(button);
        return button;
    }
}
#endif

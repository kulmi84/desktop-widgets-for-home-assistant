using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

sealed class SolarFieldRule
{
    public bool Enabled { get; set; }
    public int Comparison { get; set; }
    public double First { get; set; }
    public double Second { get; set; }

    internal bool Hides(double? value)
    {
        if (!Enabled || !value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) ||
            double.IsNaN(First) || double.IsInfinity(First) || double.IsNaN(Second) || double.IsInfinity(Second)) return false;
        double number = value.Value;
        switch (Comparison)
        {
            case 0: return number < First;
            case 1: return number <= First;
            case 2: return number > First;
            case 3: return number >= First;
            case 4: return number == First;
            case 5: return number != First;
            case 6: return First <= Second && number >= First && number <= Second;
            case 7: return First <= Second && (number < First || number > Second);
            default: return false;
        }
    }

    internal static SolarFieldRule[] Copy(SolarFieldRule[] rules)
    {
        return Enumerable.Range(0, 4).Select(i => rules != null && i < rules.Length && rules[i] != null
            ? new SolarFieldRule { Enabled = rules[i].Enabled, Comparison = rules[i].Comparison,
                First = rules[i].First, Second = rules[i].Second } : new SolarFieldRule()).ToArray();
    }
}

static class SolarVisibility
{
    internal static readonly string[] Names = { "Solar", UiText.Get("Akku", "Battery"), UiText.Get("Haus", "Home"), UiText.Get("Netz", "Grid") };

    internal static int[] VisibleFields(SolarFieldRule[] rules, double?[] readings)
    {
        return Enumerable.Range(0, 4).Where(i => rules == null || i >= rules.Length || rules[i] == null ||
            !rules[i].Hides(readings != null && i < readings.Length ? readings[i] : null)).ToArray();
    }

    internal static Bitmap Paint(Appearance appearance, string[] values, double?[] readings, float scale,
        int alpha, float[] starts, out int[] fields)
    {
        fields = VisibleFields(appearance.SolarRules, readings);
        if (fields.Length == 0) return null;
        int[] map = fields;
        var style = appearance.Copy();
        style.ReportFields = null;
        style.FieldWidths = appearance.FieldWidths == null ? null : map.Select(i =>
            i < appearance.FieldWidths.Length ? appearance.FieldWidths[i] : 0).ToArray();
        return SolarTile.PaintTile(style, map.Select(i => values[i]).ToArray(), scale, alpha, starts,
            map.Select(i => Names[i]).ToArray(), (graphics, i) => SolarIcon.Draw(graphics, map[i], Color.FromArgb(style.Icons)));
    }
}

sealed class SolarRulesDialog : Form
{
    readonly CheckBox[] enabled = new CheckBox[4];
    readonly ComboBox[] comparison = new ComboBox[4];
    readonly TextBox[] first = new TextBox[4], second = new TextBox[4], samples = new TextBox[4];
    readonly Label message = new Label();
    readonly Panel preview = new Panel();
    readonly Button save = new SettingsButton { Primary = true, Text = UiText.Get("Übernehmen", "Apply") };
    readonly Appearance appearance;
    SolarFieldRule[] draft;
    double?[] numbers;
    internal SolarFieldRule[] Result { get; private set; }

    internal SolarRulesDialog(Appearance style, SolarFieldRule[] rules, double?[] current)
    {
        appearance = style;
        Text = UiText.Get("Solar – Ausblendbedingungen", "Solar – Hide conditions");
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = SettingsTheme.Surface;
        ForeColor = Color.WhiteSmoke;
        ClientSize = new Size(1000, 428);
        SettingsTheme.UseRoundedWindowCorners(Handle);
        AddLabel(UiText.Get("Dieses Feld ausblenden, wenn …  ·  Werte immer in W, Akku in %", "Hide this field when …  ·  Always use W, or % for the battery"), 18, 16, 960);
        string[] headers = { UiText.Get("Aktiv / Feld", "Enabled / Field"), UiText.Get("Bedingung", "Condition"),
            UiText.Get("Grenzwert / von", "Threshold / from"), UiText.Get("bis", "to"), UiText.Get("Vorschauwert", "Preview reading") };
        int[] positions = { 18, 180, 460, 620, 800 };
        for (int i = 0; i < headers.Length; i++) AddLabel(headers[i], positions[i], 53, i == 1 ? 260 : 160);
        var loaded = SolarFieldRule.Copy(rules);
        string[] operations = { UiText.Get("kleiner als (<)", "less than (<)"), UiText.Get("kleiner / gleich (≤)", "less than or equal (≤)"),
            UiText.Get("größer als (>)", "greater than (>)"), UiText.Get("größer / gleich (≥)", "greater than or equal (≥)"),
            UiText.Get("gleich (=)", "equal (=)"), UiText.Get("ungleich (≠)", "not equal (≠)"),
            UiText.Get("zwischen (inkl. Grenzen)", "between (inclusive)"), UiText.Get("außerhalb des Bereichs", "outside the range") };
        for (int i = 0; i < 4; i++)
        {
            int y = 80 + i * 38;
            enabled[i] = new CheckBox { Text = SolarVisibility.Names[i] + (i == 1 ? " (%)" : " (W)"),
                Checked = loaded[i].Enabled, Bounds = new Rectangle(18, y, 158, 28) };
            comparison[i] = new RulePicker { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = SettingsTheme.Input, ForeColor = ForeColor, Bounds = new Rectangle(180, y, 262, 28),
                AccessibleName = SolarVisibility.Names[i] + UiText.Get(" Bedingung", " condition") };
            comparison[i].Items.AddRange(operations);
            comparison[i].SelectedIndex = Math.Max(0, Math.Min(7, loaded[i].Comparison));
            first[i] = Input(loaded[i].First, 460, y, SolarVisibility.Names[i] + " " + headers[2]);
            second[i] = Input(loaded[i].Second, 620, y, SolarVisibility.Names[i] + " " + headers[3]);
            samples[i] = Input(current != null && i < current.Length && current[i].HasValue ? current[i].Value : 0,
                800, y, SolarVisibility.Names[i] + " " + headers[4]);
            Controls.AddRange(new Control[] { enabled[i], comparison[i] });
            enabled[i].CheckedChanged += delegate { UpdatePreview(); };
            comparison[i].SelectedIndexChanged += delegate { UpdatePreview(); };
            first[i].TextChanged += delegate { UpdatePreview(); };
            second[i].TextChanged += delegate { UpdatePreview(); };
            samples[i].TextChanged += delegate { UpdatePreview(); };
        }
        message.Bounds = new Rectangle(18, 239, 960, 28);
        message.ForeColor = Color.Silver;
        Controls.Add(message);
        preview.Bounds = new Rectangle(18, 272, 964, 100);
        preview.BackColor = Color.FromArgb(64, 66, 70);
        preview.Paint += PaintPreview;
        Controls.Add(preview);
        var cancel = new SettingsButton { Text = UiText.Get("Abbrechen", "Cancel"), DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(766, 386, 104, 30) };
        save.Bounds = new Rectangle(878, 386, 104, 30);
        save.Click += delegate { if (UpdatePreview()) { Result = SolarFieldRule.Copy(draft); DialogResult = DialogResult.OK; } };
        Controls.AddRange(new Control[] { cancel, save });
        CancelButton = cancel;
        AcceptButton = save;
        UpdatePreview();
    }

    void AddLabel(string text, int x, int y, int width)
    { Controls.Add(new Label { Text = text, Bounds = new Rectangle(x, y, width, 26) }); }

    TextBox Input(double value, int x, int y, string name)
    {
        var field = new TextBox { Text = value.ToString("0.########", CultureInfo.CurrentCulture), Bounds = new Rectangle(x, y, 148, 26),
            BackColor = SettingsTheme.Input, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, AccessibleName = name };
        Controls.Add(field);
        return field;
    }

    static bool Number(TextBox field, out double number)
    {
        return (double.TryParse(field.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out number) ||
            double.TryParse(field.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) &&
            !double.IsNaN(number) && !double.IsInfinity(number);
    }

    bool UpdatePreview()
    {
        draft = new SolarFieldRule[4];
        numbers = new double?[4];
        bool valid = true;
        for (int i = 0; i < 4; i++)
        {
            bool range = comparison[i].SelectedIndex >= 6;
            first[i].Enabled = enabled[i].Checked;
            second[i].Enabled = enabled[i].Checked && range;
            double a = 0, b = 0, sample;
            bool parsedA = Number(first[i], out a), parsedB = Number(second[i], out b);
            if (enabled[i].Checked && (!parsedA || (range && (!parsedB || a > b)))) valid = false;
            if (Number(samples[i], out sample)) numbers[i] = sample; else valid = false;
            draft[i] = new SolarFieldRule { Enabled = enabled[i].Checked, Comparison = comparison[i].SelectedIndex,
                First = parsedA ? a : 0, Second = parsedB ? b : 0 };
        }
        save.Enabled = valid;
        message.Text = valid ? UiText.Get("Vorschau mit Beispielwerten rechts. Unbekannte Sensorwerte werden nicht ausgeblendet.",
            "Preview uses the sample readings on the right. Unknown sensor values stay visible.")
            : UiText.Get("Gültige Zahlen eingeben; bei Bereichen muss „von“ kleiner oder gleich „bis“ sein.",
                "Enter valid numbers. A range's lower bound must not exceed its upper bound.");
        preview.Invalidate();
        return valid;
    }

    void PaintPreview(object sender, PaintEventArgs args)
    {
        if (draft == null) return;
        float dpi = args.Graphics.DpiX / 96f;
        var style = appearance.Copy();
        style.SolarRules = draft;
        int[] fields;
        using (var bitmap = SolarVisibility.Paint(style, Enumerable.Range(0, 4).Select(i => numbers[i].HasValue
            ? SolarTile.DisplayReading(i, numbers[i].Value) : "—").ToArray(), numbers, dpi, 0, new float[4], out fields))
        {
            if (bitmap == null)
            {
                TextRenderer.DrawText(args.Graphics, UiText.Get("Alle Felder ausgeblendet – das Widget ist unsichtbar.",
                    "All fields hidden – the widget is invisible."), Font, preview.ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            float fit = Math.Min(1, Math.Min((preview.Width - 12f) / bitmap.Width, (preview.Height - 12f) / bitmap.Height));
            using (var fitted = SolarVisibility.Paint(style, Enumerable.Range(0, 4).Select(i => numbers[i].HasValue
                ? SolarTile.DisplayReading(i, numbers[i].Value) : "—").ToArray(), numbers, dpi * fit, 0, new float[4], out fields))
                args.Graphics.DrawImageUnscaled(fitted, (preview.Width - fitted.Width) / 2, (preview.Height - fitted.Height) / 2);
        }
    }

    sealed class RulePicker : ComboBox
    {
        internal RulePicker() { DrawMode = DrawMode.OwnerDrawFixed; }
        protected override void OnDrawItem(DrawItemEventArgs args)
        {
            using (var brush = new SolidBrush((args.State & DrawItemState.Selected) != 0 ? SettingsTheme.Border : SettingsTheme.Input))
                args.Graphics.FillRectangle(brush, args.Bounds);
            if (args.Index >= 0) TextRenderer.DrawText(args.Graphics, GetItemText(Items[args.Index]), Font,
                args.Bounds, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

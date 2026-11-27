using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class SettingsTheme
{
    internal static readonly Color Surface = Color.FromArgb(32, 32, 32);
    internal static readonly Color Card = Color.FromArgb(39, 39, 41);
    internal static readonly Color Input = Color.FromArgb(48, 48, 51);
    internal static readonly Color Border = Color.FromArgb(76, 76, 80);
    internal static readonly Color MonitorSurface = Color.FromArgb(24, 24, 26);
    internal static readonly Color MonitorInput = Color.FromArgb(29, 29, 32);
    internal static readonly Color MonitorButton = Color.FromArgb(45, 45, 49);
    internal static readonly Color MonitorBorder = Color.FromArgb(64, 64, 70);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr handle, string appName, string idList);

    internal static void ApplyMonitorStyle(Control parent)
    {
        parent.BackColor = MonitorSurface;
        foreach (Control child in parent.Controls)
        {
            var button = child as SettingsButton;
            var card = child as SettingsCard;
            var textBox = child as TextBox;
            var combo = child as ComboBox;
            if (button != null) button.MonitorStyle = true;
            else if (card != null)
            {
                card.MonitorStyle = true;
                ApplyMonitorStyle(card);
            }
            else if (child is Panel) ApplyMonitorStyle(child);
            else if (textBox != null)
            {
                textBox.BackColor = MonitorInput;
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (combo != null)
            {
                combo.BackColor = MonitorInput;
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.HandleCreated += delegate { SetWindowTheme(combo.Handle, "DarkMode_Explorer", null); };
                if (combo.IsHandleCreated) SetWindowTheme(combo.Handle, "DarkMode_Explorer", null);
                combo.DrawItem += DrawMonitorComboItem;
            }
        }
    }

    static void DrawMonitorComboItem(object sender, DrawItemEventArgs e)
    {
        var combo = (ComboBox)sender;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var brush = new SolidBrush(selected ? SystemColors.Highlight : MonitorInput))
            e.Graphics.FillRectangle(brush, e.Bounds);
        if (e.Index >= 0 && e.Index < combo.Items.Count)
        {
            var bounds = e.Bounds;
            bounds.X += 3;
            TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), e.Font, bounds,
                selected ? SystemColors.HighlightText : combo.ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        e.DrawFocusRectangle();
    }

    internal static void UseRoundedWindowCorners(IntPtr handle)
    {
        const int windowCornerPreference = 33;
        int roundCorners = 2;
        DwmSetWindowAttribute(handle, windowCornerPreference, ref roundCorners, sizeof(int));
    }

    internal static GraphicsPath Rounded(RectangleF area, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(area.Width, area.Height));
        if (d <= 0) return path;
        path.AddArc(area.Left, area.Top, d, d, 180, 90);
        path.AddArc(area.Right - d, area.Top, d, d, 270, 90);
        path.AddArc(area.Right - d, area.Bottom - d, d, d, 0, 90);
        path.AddArc(area.Left, area.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

sealed class SettingsCard : GroupBox
{
    internal bool MonitorStyle { get; set; }
    internal SettingsCard()
    {
        FlatStyle = FlatStyle.Standard;
        BackColor = SettingsTheme.Card;
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent == null ? SettingsTheme.Surface : Parent.BackColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (MonitorStyle)
        {
            float scale = e.Graphics.DpiX / 96f;
            int inset = (int)Math.Round(8 * scale);
            int middle = Font.Height / 2;
            int captionWidth = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            using (var border = new Pen(SettingsTheme.MonitorBorder))
                e.Graphics.DrawRectangle(border, 0, middle, Width - 1, Height - middle - 1);
            using (var fill = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(fill, inset - 2, 0, captionWidth + 4, Font.Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Point(inset, 0), ForeColor, TextFormatFlags.NoPadding);
            return;
        }
        using (var outline = SettingsTheme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7 * e.Graphics.DpiX / 96f))
        using (var fill = new SolidBrush(SettingsTheme.Card))
        using (var border = new Pen(Color.FromArgb(57, 57, 61)))
        {
            e.Graphics.FillPath(fill, outline);
            e.Graphics.DrawPath(border, outline);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Point(10, 3), ForeColor, TextFormatFlags.NoPadding);
    }
}

sealed class SettingsButton : Button
{
    internal bool Primary { get; set; }
    internal bool MonitorStyle { get; set; }
    bool hover, pressed;

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (MonitorStyle)
        {
            Color background = !Enabled ? SettingsTheme.MonitorSurface : pressed ? SettingsTheme.MonitorInput
                : hover ? SettingsTheme.Border : SettingsTheme.MonitorButton;
            e.Graphics.Clear(background);
            using (var border = new Pen(Enabled && (Primary || (Focused && ShowFocusCues))
                ? Color.FromArgb(122, 127, 140) : SettingsTheme.MonitorBorder))
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.DarkGray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }
        var fill = !Enabled ? SettingsTheme.Card : Primary
            ? Color.FromArgb(pressed ? 130 : hover ? 180 : 153, 206, 247)
            : Color.FromArgb(pressed ? 47 : hover ? 65 : 54, pressed ? 47 : hover ? 65 : 54, pressed ? 49 : hover ? 68 : 57);
        Color ink = Enabled && Primary ? Color.FromArgb(12, 29, 42) : Enabled ? ForeColor : Color.DarkGray;
        e.Graphics.Clear(Parent == null ? SettingsTheme.Surface : Parent.BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var outline = SettingsTheme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 4 * e.Graphics.DpiX / 96f))
        using (var brush = new SolidBrush(fill))
        using (var edge = new Pen(Focused && ShowFocusCues ? Color.WhiteSmoke : SettingsTheme.Border))
        {
            e.Graphics.FillPath(brush, outline);
            if (!Primary || (Focused && ShowFocusCues)) e.Graphics.DrawPath(edge, outline);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

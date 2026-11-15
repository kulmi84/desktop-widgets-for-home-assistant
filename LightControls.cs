using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class LightControls : Form
{
    static readonly string[] ColorModes = { "hs", "xy", "rgb", "rgbw", "rgbww" };
    static readonly string[] ColorNames = { "Orange", "Apricot", UiText.Get("Warmweiß", "Warm white"), UiText.Get("Weiß", "White"), UiText.Get("Blau", "Blue"), UiText.Get("Violett", "Purple"), UiText.Get("Rosa", "Pink"), UiText.Get("Rot", "Red") };
    static readonly int[] Palette = { 0xff8900, 0xffc089, 0xffe4cf, 0xfffcf8, 0x7daaff, 0xd597fa, 0xff9bef, 0xff6954 };

    readonly Label status;
    readonly Dimmer dimmer;
    readonly Swatch[] swatches;
    readonly Func<SolarTile.Sensor> read;
    readonly Func<string, Dictionary<string, object>, Task> send;
    bool busy;
    bool hasDimmer, hasColor;
    readonly Point anchor;
    readonly int padding;
    readonly Font tileFont;

    internal LightControls(string name, Appearance style, Func<SolarTile.Sensor> read,
        Func<string, Dictionary<string, object>, Task> send, Point anchor)
    {
        this.read = read;
        this.send = send;
        this.anchor = anchor;
        padding = Math.Max(10, style.FramePadding);
        Text = name;
        tileFont = SolarTile.CreateTileFont(style.Typeface, Math.Max(8, Math.Min(28, style.TextSize)), FontStyle.Regular);
        Font = tileFont;
        BackColor = Color.FromArgb(style.Background | unchecked((int)0xff000000));
        ForeColor = Color.FromArgb(style.Text | unchecked((int)0xff000000));
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = anchor;
        status = new Label { TextAlign = ContentAlignment.MiddleLeft };
        Controls.Add(status);
        dimmer = new Dimmer();
        dimmer.Committed += async delegate
        {
            int brightness = dimmer.Value;
            await Apply(brightness == 0 ? "turn_off" : "turn_on", brightness == 0
                ? new Dictionary<string, object>()
                : new Dictionary<string, object> { { "brightness_pct", brightness } });
        };
        Controls.Add(dimmer);
        swatches = new Swatch[Palette.Length];
        for (int i = 0; i < Palette.Length; ++i)
        {
            var color = Color.FromArgb(unchecked((int)0xff000000) | Palette[i]);
            var swatch = new Swatch(color) { AccessibleName = ColorNames[i] };
            swatch.Click += async delegate
            {
                await Apply("turn_on", new Dictionary<string, object>
                {
                    { "rgb_color", new[] { (int)color.R, (int)color.G, (int)color.B } }
                });
            };
            swatches[i] = swatch;
            Controls.Add(swatch);
        }
        UpdateState();
        Load += delegate { LayoutCard(); };
        DpiChanged += delegate { BeginInvoke(new Action(LayoutCard)); };
        Deactivate += delegate { if (!busy) Close(); };
    }

    static Color Blend(Color background, Color ink, float amount)
    {
        return Color.FromArgb((int)(background.R + (ink.R - background.R) * amount),
            (int)(background.G + (ink.G - background.G) * amount),
            (int)(background.B + (ink.B - background.B) * amount));
    }

    void LayoutCard()
    {
        if (IsDisposed) return;
        int inset = LogicalToDeviceUnits(padding);
        int gap = LogicalToDeviceUnits(6);
        int cell = LogicalToDeviceUnits(34);
        int content = swatches.Length * (cell + gap) - gap;
        int y = inset;
        if (!string.IsNullOrEmpty(status.Text))
        {
            status.SetBounds(inset, y, content, Math.Max(LogicalToDeviceUnits(24), status.PreferredHeight));
            y += status.Height;
        }
        if (hasDimmer)
        {
            if (y > inset) y += gap;
            dimmer.SetBounds(inset, y, content, Math.Max(LogicalToDeviceUnits(29), Font.Height + gap));
            y += dimmer.Height;
        }
        if (hasColor)
        {
            y += gap;
            int step = (content + gap) / swatches.Length;
            for (int i = 0; i < swatches.Length; ++i)
                swatches[i].SetBounds(inset + i * step, y, step - gap, LogicalToDeviceUnits(26));
            y += swatches[0].Height;
        }
        ClientSize = new Size(content + inset * 2, y + inset);
        var area = Screen.FromPoint(anchor).WorkingArea;
        var position = new Point(anchor.X, anchor.Y + gap);
        if (position.Y + Height > area.Bottom) position.Y = anchor.Y - Height - gap;
        Location = WidgetPlacement.KeepOnScreen(position, Size, area);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SettingsTheme.UseRoundedWindowCorners(Handle);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && tileFont != null) tileFont.Dispose();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    async Task Apply(string service, Dictionary<string, object> data)
    {
        if (busy) return;
        busy = true;
        dimmer.Enabled = false;
        foreach (var swatch in swatches) swatch.Enabled = false;
        try { await send(service, data); }
        finally
        {
            busy = false;
            if (!IsDisposed) UpdateState();
        }
    }

    internal void RefreshState()
    {
        if (!busy && !dimmer.Dragging) UpdateState();
    }

    void UpdateState()
    {
        var state = read();
        bool isOn = state != null && state.state == "on";
        bool available = state != null && (isOn || state.state == "off");
        var attributes = state == null ? null : state.attributes;
        var modes = attributes == null ? null : attributes.supported_color_modes;
        bool hadColor = hasColor, hadDimmer = hasDimmer;
        string previousStatus = status.Text;
        hasColor = modes != null && modes.Any(mode => ColorModes.Contains(mode));
        hasDimmer = hasColor || (attributes != null && attributes.brightness.HasValue) ||
            (modes != null && modes.Any(mode => mode == "brightness" || mode == "color_temp" || mode == "white"));
        dimmer.Value = isOn && attributes != null && attributes.brightness.HasValue
            ? (int)Math.Round(attributes.brightness.Value * 100.0 / 255) : isOn ? 100 : 0;
        status.Text = !available ? UiText.Get("Nicht verfügbar", "Unavailable") : string.Empty;
        status.Visible = status.Text.Length != 0;
        dimmer.Visible = hasDimmer;
        dimmer.Enabled = available;
        var rgb = attributes == null ? null : attributes.rgb_color;
        foreach (var swatch in swatches)
        {
            swatch.Visible = hasColor;
            swatch.Enabled = available;
            swatch.SelectedColor = isOn && rgb != null && rgb.Length >= 3 &&
                swatch.Sample.R == rgb[0] && swatch.Sample.G == rgb[1] && swatch.Sample.B == rgb[2];
            swatch.Invalidate();
        }
        if (IsHandleCreated && (hasColor != hadColor || hasDimmer != hadDimmer || status.Text != previousStatus)) LayoutCard();
    }

    sealed class Swatch : Button
    {
        internal Color Sample { get; private set; }
        internal bool SelectedColor;

        internal Swatch(Color color) { Sample = color; Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent.BackColor);
            using (var path = SettingsTheme.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), LogicalToDeviceUnits(9)))
            using (var fill = new SolidBrush(Enabled ? Sample : ControlPaint.Dark(Sample)))
            using (var edge = new Pen(Parent.ForeColor, LogicalToDeviceUnits(2)))
            {
                e.Graphics.FillPath(fill, path);
                if (SelectedColor || (Focused && ShowFocusCues)) e.Graphics.DrawPath(edge, path);
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    sealed class Dimmer : Control
    {
        int value;
        bool dragging;
        internal bool Dragging { get { return dragging; } }
        internal event Action Committed;
        internal string Percentage { get { return Value > 0 ? Value + " %" : string.Empty; } }
        internal int Value
        {
            get { return value; }
            set
            {
                this.value = Math.Max(0, Math.Min(100, value));
                AccessibleDescription = this.value + UiText.Get(" Prozent", " percent");
                Invalidate();
            }
        }

        internal Dimmer()
        {
            DoubleBuffered = true;
            TabStop = true;
            AccessibleName = UiText.Get("Helligkeit", "Brightness");
            AccessibleRole = AccessibleRole.Slider;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = LogicalToDeviceUnits(9);
            var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
            using (var track = SettingsTheme.Rounded(bounds, radius))
            using (var background = new SolidBrush(Blend(Parent.BackColor, Parent.ForeColor, 0.12f)))
            using (var fill = new SolidBrush(Blend(Parent.BackColor, Parent.ForeColor, Enabled ? 0.28f : 0.2f)))
            using (var grip = new Pen(Parent.BackColor, LogicalToDeviceUnits(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                e.Graphics.FillPath(background, track);
                float filled = bounds.Width * Value / 100;
                if (filled > 0)
                {
                    using (var progress = SettingsTheme.Rounded(new RectangleF(bounds.Left, bounds.Top, filled, bounds.Height), radius))
                        e.Graphics.FillPath(fill, progress);
                }
                float x = Math.Max(5, Math.Min(Width - 5, filled - LogicalToDeviceUnits(5)));
                e.Graphics.DrawLine(grip, x, Height * 0.29f, x, Height * 0.71f);
                TextRenderer.DrawText(e.Graphics, Percentage, Font, ClientRectangle, Parent.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            dragging = Capture = true;
            Value = (int)Math.Round(e.X * 100.0 / Width);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Value = (int)Math.Round(e.X * 100.0 / Width);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = Capture = false;
            Value = (int)Math.Round(e.X * 100.0 / Width);
            if (Committed != null) Committed();
        }

        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: --Value; break;
                case Keys.Right: ++Value; break;
                case Keys.Home: Value = 0; break;
                case Keys.End: Value = 100; break;
                default: return;
            }
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Home || e.KeyCode == Keys.End) &&
                Committed != null) Committed();
        }
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

static class SolarValueImage
{
    internal const int Width = 250;
    internal const int Height = 43;
    internal const int LargeHeight = 55;
    internal static int HeightFor(bool large, bool dense) => dense ? (large ? 45 : 34) : large ? LargeHeight : Height;

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    internal static float Scale()
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        float scale;
        try { scale = Math.Max(96u, GetDpiForSystem()) / 96f; }
        finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
        return scale;
    }

    internal static string Render(string value, int rgb, bool large = false, int width = Width, bool dense = false)
    {
        float scale = Scale();
        using var bitmap = new Bitmap((int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(HeightFor(large, dense) * scale), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Segoe UI", (dense ? (large ? 35 : 26) : large ? 45 : 34) * scale,
            FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(unchecked((int)0xFF000000) | rgb));
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var bounds = new RectangleF(2 * scale, 0, bitmap.Width - 4 * scale, bitmap.Height);
        float measuredWidth = graphics.MeasureString(value, font, PointF.Empty, format).Width;
        if (measuredWidth > bounds.Width)
        {
            using var fitted = new Font(font.FontFamily, font.Size * bounds.Width / measuredWidth * 0.97f,
                font.Style, GraphicsUnit.Pixel);
            format.LineAlignment = StringAlignment.Center;
            graphics.DrawString(value, fitted, brush, bounds, format);
        }
        else graphics.DrawString(value, font, brush, bounds, format);
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(png.GetBuffer(), 0, (int)png.Length);
    }
}

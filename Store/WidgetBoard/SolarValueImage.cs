using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

static class SolarValueImage
{
    internal const int Width = 250;
    internal const int Height = 43;
    internal const int LargeHeight = 55;

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    internal static string Render(string value, int rgb, bool large = false)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        float scale;
        try { scale = Math.Max(96u, GetDpiForSystem()) / 96f; }
        finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }

        using var bitmap = new Bitmap((int)Math.Ceiling(Width * scale),
            (int)Math.Ceiling((large ? LargeHeight : Height) * scale), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Segoe UI", (large ? 45 : 34) * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(unchecked((int)0xFF000000) | rgb));
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString(value, font, brush,
            new RectangleF(2 * scale, 0, bitmap.Width - 2 * scale, bitmap.Height), format);
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(png.GetBuffer(), 0, (int)png.Length);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Media;

static class BoardIcon
{
    // Material Design Icons by Pictogrammers. See MDI-NOTICE.txt.
    static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["flash"] = "M7,2V13H10V22L17,10H13L17,2H7Z",
        ["battery"] = "M16,4H15V2H9V4H8A2,2 0 0,0 6,6V20A2,2 0 0,0 8,22H16A2,2 0 0,0 18,20V6A2,2 0 0,0 16,4Z",
        ["home-lightning-bolt"] = "M12,3L2,12H5V21H11V15H9L13,8V13H15L11,21H19V12H22L12,3Z",
        ["solar-panel"] = "M3,4H21L23,17H13V20H17V22H7V20H11V17H1L3,4M5,6L4.4,10H8V6H5M10,6V10H14V6H10M16,6V10H19.6L19,6H16M4.1,12L3.6,15H8V12H4.1M10,12V15H14V12H10M16,12V15H20.4L19.9,12H16Z",
        ["thermometer"] = "M15,13V5A3,3 0 0,0 9,5V13A5,5 0 1,0 15,13M12,4A1,1 0 0,1 13,5V10H11V5A1,1 0 0,1 12,4Z",
        ["water-percent"] = "M12,3C12,3 5,10.5 5,15A7,7 0 0,0 19,15C19,10.5 12,3 12,3M9,12A1.25,1.25 0 1,1 9,14.5A1.25,1.25 0 1,1 9,12M15,16A1.25,1.25 0 1,1 15,18.5A1.25,1.25 0 1,1 15,16M8.5,17.5L14.5,11.5L15.5,12.5L9.5,18.5L8.5,17.5Z",
        ["gesture-tap-button"] = "M3,3H21V13H17V11H19V5H5V11H8V13H3V3M10,8A1.5,1.5 0 0,1 13,8V14L17,15.5L18,17L17,22H10L6,17L7.5,15.5L10,17V8Z",
        ["chart-line"] = "M3,3H5V19H21V21H3V3M7,15L11,11L14,14L19,8H16V6H22V12H20V10L14,17L11,14L8.5,16.5L7,15Z"
    };

    internal static string ForEntity(string entity, EntityState? reading)
    {
        string? custom = reading?.Attributes?.Icon;
        if (custom?.StartsWith("mdi:", StringComparison.Ordinal) == true)
        {
            string icon = custom[4..];
            if (Paths.ContainsKey(icon) || DoorIcons.IsControlIcon(icon) || icon is "garage" or "garage-open" or "door" or "door-open"
                or "window-closed" or "window-open" or "toggle-switch" or "toggle-switch-off")
                return icon;
        }

        bool on = reading?.State == "on";
        return reading?.Attributes?.DeviceClass switch
        {
            "garage_door" => on ? "garage-open" : "garage",
            "door" or "opening" => on ? "door-open" : "door",
            "window" => on ? "window-open" : "window-closed",
            "battery" or "battery_charging" => "battery",
            "power" or "energy" or "voltage" or "current" => "flash",
            "temperature" => "thermometer",
            "humidity" or "moisture" => "water-percent",
            _ when BoardSelection.IsButton(entity) => "gesture-tap-button",
            _ when entity.StartsWith("light.", StringComparison.Ordinal) => "lightbulb",
            _ when BoardSelection.IsSwitch(entity) => on ? "toggle-switch" : "toggle-switch-off",
            _ when reading?.Attributes?.Unit is "W" or "kW" or "Wh" or "kWh" => "flash",
            _ => "chart-line"
        };
    }

    internal static string Render(string icon, int size, int rgb, int topPadding = 0)
    {
        float scale = SolarValueImage.Scale();
        int pixels = (int)Math.Ceiling(size * scale);
        using var bitmap = new Bitmap(pixels, (int)Math.Ceiling((size + topPadding) * scale),
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TranslateTransform(0, topPadding * scale);
        var color = System.Drawing.Color.FromArgb(unchecked((int)0xFF000000) | rgb);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        if (DoorIcons.IsControlIcon(icon))
        {
            graphics.ScaleTransform(pixels / 32f, pixels / 32f);
            DoorIcons.DrawControlIcon(graphics, icon, color);
        }
        else if (Paths.TryGetValue(icon, out string? path))
        {
            using var shape = new GraphicsPath(FillMode.Alternate);
            foreach (var figure in Geometry.Parse(path).GetFlattenedPathGeometry(0.05, ToleranceType.Absolute).Figures)
            {
                var points = new[] { figure.StartPoint }.Concat(figure.Segments.SelectMany(segment =>
                    segment is PolyLineSegment line ? line.Points.AsEnumerable() : new[] { ((LineSegment)segment).Point }));
                shape.AddPolygon(points.Select(point => new PointF((float)point.X * pixels / 24,
                    (float)point.Y * pixels / 24)).ToArray());
            }
            using var brush = new System.Drawing.SolidBrush(color);
            graphics.FillPath(brush, shape);
        }
        else
        {
            string kind = icon switch
            {
                "garage" or "garage-open" => "Garagentor",
                "window-open" or "window-closed" => "Fenster",
                "toggle-switch" or "toggle-switch-off" => "Schalter",
                _ => "Tür"
            };
            graphics.ScaleTransform(pixels / 32f, pixels / 32f);
            DoorIcons.Draw(graphics, kind, icon.EndsWith("-open", StringComparison.Ordinal) || icon == "toggle-switch", color);
        }
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(png.GetBuffer(), 0, (int)png.Length);
    }
}

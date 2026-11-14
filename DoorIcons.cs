using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Media;

static class DoorIcons
{
    // Material Design Icons by Pictogrammers, Apache 2.0. See MDI-NOTICE.txt.
    static readonly GraphicsPath[] Shapes = {
        Parse("M19,20H17V11H7V20H5V9L12,5L19,9V20M8,12H16V14H8V12M8,15H16V17H8V15M16,18V20H8V18H16Z"),
        Parse("M19,20H17V11H7V20H5V9L12,5L19,9V20M8,12H16V14H8V12Z"),
        Parse("M16,11H18V13H16V11M12,3H19C20.11,3 21,3.89 21,5V19H22V21H2V19H10V5C10,3.89 10.89,3 12,3M12,5V19H19V5H12Z"),
        Parse("M12,3C10.89,3 10,3.89 10,5H3V19H2V21H22V19H21V5C21,3.89 20.11,3 19,3H12M12,5H19V19H12V5M5,11H7V13H5V11Z"),
        Parse("M6,11H10V9H14V11H18V4H6V11M18,13H6V20H18V13M6,2H18A2,2 0 0,1 20,4V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V4A2,2 0 0,1 6,2Z"),
        Parse("M6,8H10V6H14V8H18V4H6V8M18,10H6V15H18V10M6,20H18V17H6V20M6,2H18A2,2 0 0,1 20,4V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V4A2,2 0 0,1 6,2Z")    };

    static GraphicsPath Parse(string data)
    {
        var geometry = Geometry.Parse(data).GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
        var path = new GraphicsPath(FillMode.Alternate);
        foreach (var figure in geometry.Figures)
        {
            var points = new[] { figure.StartPoint }.Concat(figure.Segments.SelectMany(segment =>
                segment is PolyLineSegment ? ((PolyLineSegment)segment).Points.AsEnumerable()
                    : new[] { ((LineSegment)segment).Point }));
            path.AddPolygon(points.Select(point => new PointF((float)point.X * 4 / 3, (float)point.Y * 4 / 3)).ToArray());
        }
        return path;
    }

    internal static void Draw(Graphics graphics, string kind, bool open, System.Drawing.Color color)
    {
        int index = kind == "Garagentor" ? 0 : kind == "Fenster" ? 4 : 2;
        if (kind == "Schalter")
        {
            DrawSwitch(graphics, open, color);
            return;
        }
        using (var brush = new System.Drawing.SolidBrush(color))
        using (var path = (GraphicsPath)Shapes[index + (open ? 1 : 0)].Clone())
        using (var transform = graphics.Transform)
        {
            path.Transform(transform);
            var points = path.PathPoints;
            var types = path.PathTypes;
            var alignedPoints = (PointF[])points.Clone();
            int start = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if ((types[i] & 7) == 0) start = i;
                int next = (types[i] & 128) != 0 ? start : i + 1;
                if (next >= points.Length || (next != start && (types[next] & 7) == 0)) continue;
                // Snap straight horizontal and vertical edges; keep diagonal and curved segments smooth.
                if (points[i].X == points[next].X && System.Math.Abs(points[i].Y - points[next].Y) >= 1f)
                    alignedPoints[i].X = alignedPoints[next].X = (float)System.Math.Round(points[i].X);
                if (points[i].Y == points[next].Y && System.Math.Abs(points[i].X - points[next].X) >= 1f)
                    alignedPoints[i].Y = alignedPoints[next].Y = (float)System.Math.Round(points[i].Y);
            }
            using (var aligned = new GraphicsPath(alignedPoints, types, path.FillMode))
            {
                var state = graphics.Save();
                try
                {
                    graphics.ResetTransform();
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    graphics.FillPath(brush, aligned);
                }
                finally { graphics.Restore(state); }
            }
        }
    }

    static void DrawSwitch(Graphics graphics, bool on, System.Drawing.Color color)
    {
        var corners = new[] { new PointF(1, 8), new PointF(31, 24) };
        using (var transform = graphics.Transform) transform.TransformPoints(corners);
        int left = (int)System.Math.Round(corners[0].X);
        int top = (int)System.Math.Round(corners[0].Y);
        int width = (int)System.Math.Round(corners[1].X) - left;
        int height = (int)System.Math.Round(corners[1].Y) - top;
        int line = System.Math.Max(1, (int)System.Math.Round(height / 16f));
        int padding = line + 1;
        int thumbSize = System.Math.Max(1, height - 2 * padding);
        var outer = new Rectangle(left, top, width, height);
        var inner = Rectangle.Inflate(outer, -line, -line);
        if (inner.Width <= 0 || inner.Height <= 0) return;

        using (var track = Capsule(outer))
        using (var hole = Capsule(inner))
        using (var ring = new GraphicsPath(FillMode.Alternate))
        using (var outline = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(on ? 255 : 190, color)))
        using (var tint = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(35, color)))
        using (var thumb = new System.Drawing.SolidBrush(color))
        {
            ring.AddPath(track, false);
            ring.AddPath(hole, false);
            var state = graphics.Save();
            try
            {
                graphics.ResetTransform();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                if (on) graphics.FillPath(tint, hole);
                graphics.FillPath(outline, ring);
                int thumbX = on ? outer.Right - padding - thumbSize : left + padding;
                graphics.FillEllipse(thumb, thumbX, top + padding, thumbSize, thumbSize);
            }
            finally { graphics.Restore(state); }
        }
    }

    static GraphicsPath Capsule(Rectangle bounds)
    {
        var path = new GraphicsPath();
        int diameter = bounds.Height;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }
}
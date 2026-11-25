using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

enum HorizontalAnchor
{
    Right,
    Left,
    Center
}

sealed class WidgetPlacement
{
    internal const string FileName = "display-layouts.json";
    static readonly string ProfilePath = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), ReleaseVersion.DataFolder, FileName);
    static readonly List<WidgetPlacement> Active = new List<WidgetPlacement>();
    static readonly Timer Watcher = new Timer { Interval = 1000 };

    readonly Form widget;
    readonly string widgetId;
    readonly string path;
    readonly Point fallback;
    readonly Func<HorizontalAnchor> horizontalAnchor;
    string layout;
    internal bool Locked { get; private set; }
    bool centered;

    static WidgetPlacement()
    {
        Watcher.Tick += delegate
        {
            var screens = GetScreens();
            string current = LayoutKey(screens);
            foreach (var placement in Active.ToArray())
                if (placement.layout != current)
                    placement.Restore(screens, current);
        };
    }

    internal WidgetPlacement(Form widget, string widgetId, Point fallback, string path = null,
        Func<HorizontalAnchor> horizontalAnchor = null)
    {
        this.widget = widget;
        this.widgetId = widgetId;
        this.fallback = fallback;
        this.horizontalAnchor = horizontalAnchor ?? (() => HorizontalAnchor.Left);
        this.path = path ?? ProfilePath;
        var screens = GetScreens();
        Restore(screens, LayoutKey(screens));
        if (path != null) return;
        Active.Add(this);
        Watcher.Start();
        widget.Disposed += delegate
        {
            Active.Remove(this);
            if (Active.Count == 0) Watcher.Stop();
        };
    }

    public sealed class Position
    {
        public int Monitor { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public bool Locked { get; set; }
        public bool Centered { get; set; }
    }

    static Screen[] GetScreens()
    {
        return Screen.AllScreens.OrderBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top).ThenBy(screen => screen.DeviceName, StringComparer.Ordinal).ToArray();
    }

    static string LayoutKey(Screen[] screens)
    {
        return (SystemInformation.TerminalServerSession ? "rdp|" : "local|") +
            string.Join(";", screens.Select(screen => FormattableBounds(screen.Bounds) + (screen.Primary ? "p" : "")));
    }

    static string FormattableBounds(Rectangle bounds)
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0},{1},{2},{3}", bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    void Restore(Screen[] screens, string current)
    {
        layout = current;
        Point point = fallback;
        centered = false;
        var monitor = Screen.FromPoint(point);
        try
        {
            var data = Read();
            Position position;
            Locked = data.TryGetValue(LockKey, out position) && position != null && position.Locked;
            if (data.TryGetValue(Key(current), out position) && position != null &&
                position.Monitor >= 0 && position.Monitor < screens.Length)
            {
                monitor = screens[position.Monitor];
                centered = position.Centered;
                point = monitor.WorkingArea.Location + new Size(position.X, position.Y);
                if (position.Width > 0)
                    point.X = LeftAfterResize(point.X, position.Width, widget.Width, horizontalAnchor());
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (centered) point.X = CenteredLeft(monitor.WorkingArea, widget.Width);
        widget.Location = KeepOnScreen(point, widget.Size, monitor.WorkingArea);
    }

    internal static Point KeepOnScreen(Point point, Size size, Rectangle area)
    {
        return new Point(Math.Max(area.Left, Math.Min(point.X, area.Right - size.Width)),
            Math.Max(area.Top, Math.Min(point.Y, area.Bottom - size.Height)));
    }

    internal static int LeftAfterResize(int left, int previousWidth, int width, HorizontalAnchor anchor)
    {
        switch (anchor)
        {
            case HorizontalAnchor.Right: return left + previousWidth - width;
            case HorizontalAnchor.Center: return left + previousWidth / 2 - width / 2;
            default: return left;
        }
    }

    internal void Resize(Size clientSize)
    {
        if (widget.ClientSize == clientSize) return;
        int width = widget.Width + clientSize.Width - widget.ClientSize.Width;
        int height = widget.Height + clientSize.Height - widget.ClientSize.Height;
        int left = centered ? CenteredLeft(Screen.FromRectangle(widget.Bounds).WorkingArea, width) :
            LeftAfterResize(widget.Left, widget.Width, width, horizontalAnchor());
        widget.SetBounds(left, widget.Top, width, height);
    }

    internal static int CenteredLeft(Rectangle area, int width)
    {
        return area.Left + Math.Max(0, (area.Width - width) / 2);
    }

    internal Point DragLocation(Point requested)
    {
        return centered ? new Point(CenteredLeft(Screen.FromRectangle(widget.Bounds).WorkingArea, widget.Width),
            requested.Y) : requested;
    }

    internal void Save()
    {
        var screens = GetScreens();
        var monitor = Screen.FromRectangle(widget.Bounds);
        int index = Array.FindIndex(screens, screen => screen.DeviceName == monitor.DeviceName);
        if (index < 0) return;
        string current = LayoutKey(screens);
        var data = Read();
        var origin = monitor.WorkingArea.Location;
        data[Key(current)] = new Position { Monitor = index, X = widget.Left - origin.X, Y = widget.Top - origin.Y,
            Width = widget.Width, Centered = centered };
        Write(data);
        layout = current;
    }

    void SetLocked(bool value)
    {
        var data = Read();
        if (value) data[LockKey] = new Position { Locked = true };
        else data.Remove(LockKey);
        Write(data);
        Locked = value;
    }

    internal static void AddLockMenuItem(ContextMenuStrip menu, Form widget, Func<WidgetPlacement> getPlacement)
    {
        var item = new ToolStripMenuItem(UiText.Get("Widget fixieren", "Lock widget position")) { CheckOnClick = true };
        menu.Items.Add(item);
        var center = new ToolStripMenuItem(UiText.Get("Horizontal auf dem Bildschirm zentrieren",
            "Center horizontally on screen")) { CheckOnClick = true };
        menu.Items.Add(center);
        menu.Opening += delegate
        {
            var placement = getPlacement();
            item.Enabled = placement != null;
            item.Checked = placement != null && placement.Locked;
            center.Enabled = placement != null;
            center.Checked = placement != null && placement.centered;
        };
        item.Click += delegate
        {
            var placement = getPlacement();
            if (placement == null) return;
            try { placement.SetLocked(item.Checked); }
            catch (IOException) { ShowLockError(widget); }
            catch (UnauthorizedAccessException) { ShowLockError(widget); }
            item.Checked = placement.Locked;
        };
        center.Click += delegate
        {
            var placement = getPlacement();
            if (placement == null) return;
            bool previous = placement.centered;
            Point previousLocation = widget.Location;
            Action restoreCentering = delegate
            {
                placement.centered = previous;
                widget.Location = previousLocation;
                MessageBox.Show(widget, UiText.Get("Die Ausrichtung konnte nicht gespeichert werden.",
                    "Could not save the alignment."), ReleaseVersion.ProductName);
            };
            try
            {
                placement.centered = center.Checked;
                if (placement.centered)
                    widget.Left = CenteredLeft(Screen.FromRectangle(widget.Bounds).WorkingArea, widget.Width);
                placement.Save();
            }
            catch (IOException) { restoreCentering(); }
            catch (UnauthorizedAccessException) { restoreCentering(); }
            center.Checked = placement.centered;
        };
    }

    static void ShowLockError(Form widget)
    {
        MessageBox.Show(widget, UiText.Get("Die Positionssperre konnte nicht gespeichert werden.",
            "Could not save the position lock."), ReleaseVersion.ProductName);
    }

    void Write(Dictionary<string, Position> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(data));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { File.Delete(temporary); }
    }

    string Key(string current) { return current + "|" + widgetId; }
    string LockKey { get { return "lock|" + widgetId; } }

    Dictionary<string, Position> Read()
    {
        if (File.Exists(path))
        {
            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, Position>>(File.ReadAllText(path))
                    ?? new Dictionary<string, Position>();
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        return new Dictionary<string, Position>();
    }
}

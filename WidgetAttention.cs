using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;

sealed class WidgetAttention : IDisposable
{
    static readonly Dictionary<IntPtr, byte> Alphas = new Dictionary<IntPtr, byte>();
    readonly Form widget;
    readonly Func<Appearance> appearance;
    readonly Action repaint;
    readonly Timer timer = new Timer { Interval = 1000 };
    readonly Timer foregroundTimer = new Timer();
    bool temporaryTopMost;
    bool blinkTopMost;
    bool raised;
    readonly Stopwatch elapsed = Stopwatch.StartNew();
    readonly Stopwatch blinkElapsed = new Stopwatch();
    int halfCycleMilliseconds;
    int interval, flashesLeft;
    bool dimmed;
    IntPtr handle;
    static IntPtr mouseHook;
    static event Action OutsideClick;
    static readonly MouseHook MouseCallback = ObserveMouse;
    static readonly uint OwnProcess = (uint)Process.GetCurrentProcess().Id;
    delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    struct MouseData
    {
        internal Point Point;
        internal uint MouseInfo, Flags, Time;
        internal UIntPtr Extra;
    }

    [DllImport("user32.dll")]
    static extern IntPtr SetWindowsHookEx(int kind, MouseHook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr GetModuleHandle(string module);

    static IntPtr ObserveMouse(int code, IntPtr message, IntPtr data)
    {
        int kind = message.ToInt32();
        if (code >= 0 && (kind == 0x0201 || kind == 0x0204 || kind == 0x0207))
        {
            var mouse = (MouseData)Marshal.PtrToStructure(data, typeof(MouseData));
            uint process;
            IntPtr window = WindowFromPoint(mouse.Point);
            if (window != IntPtr.Zero)
            {
                GetWindowThreadProcessId(window, out process);
                if (process != OwnProcess && OutsideClick != null) OutsideClick();
            }
        }
        return CallNextHookEx(mouseHook, code, message, data);
    }

    void EndAttentionOnClick()
    {
        var style = appearance();
        if (style == null || !style.BackgroundOnAnyClick) return;
        if (!raised || temporaryTopMost || blinkTopMost || widget.IsDisposed || !widget.IsHandleCreated) return;
        widget.BeginInvoke(new Action(delegate
        {
            if (widget.IsDisposed) return;
            if (temporaryTopMost || blinkTopMost) return;
            raised = false;
            elapsed.Restart();
            if (!widget.TopMost)
                SetWindowPos(widget.Handle, new IntPtr(1), 0, 0, 0, 0, 0x0013);
        }));
    }

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    internal WidgetAttention(Form widget, Func<Appearance> appearance, Action repaint)
    {
        this.widget = widget;
        this.appearance = appearance;
        this.repaint = repaint;
        OutsideClick += EndAttentionOnClick;
        if (mouseHook == IntPtr.Zero)
            mouseHook = SetWindowsHookEx(14, MouseCallback, GetModuleHandle(null), 0);
        timer.Tick += delegate { Tick(); };
        foregroundTimer.Tick += delegate { ReleaseForeground(); };
        widget.VisibleChanged += delegate
        {
            if (!widget.Visible) { raised = false; ResetBlink(); ReleaseForeground(); }
            elapsed.Restart();
        };
        widget.Disposed += delegate { Dispose(); };
        timer.Start();
    }

    internal static byte Alpha(IntPtr handle)
    {
        byte value;
        return Alphas.TryGetValue(handle, out value) ? value : (byte)255;
    }

    internal void HoldForeground(int seconds)
    {
        seconds = Math.Max(0, Math.Min(3600, seconds));
        if (!widget.Visible || widget.IsDisposed || seconds == 0) return;
        temporaryTopMost = true;
        raised = true;
        SetWindowPos(widget.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        foregroundTimer.Stop();
        foregroundTimer.Interval = seconds * 1000;
        foregroundTimer.Start();
    }

    void ReleaseForeground()
    {
        foregroundTimer.Stop();
        if (!temporaryTopMost) return;
        temporaryTopMost = false;
        if (!blinkTopMost && !widget.IsDisposed && widget.IsHandleCreated && !widget.TopMost)
            SetWindowPos(widget.Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0013);
    }

    void Tick()
    {
        var style = appearance() ?? new Appearance();
        int minutes = Math.Max(0, Math.Min(1440, style.ForegroundMinutes));
        if (interval != minutes)
        {
            interval = minutes;
            elapsed.Restart();
            ResetBlink();
        }
        if (!widget.Visible || minutes == 0 || !widget.Enabled)
        {
            ResetBlink();
            elapsed.Restart();
            return;
        }
        if (flashesLeft > 0)
        {
            if (blinkElapsed.ElapsedMilliseconds < halfCycleMilliseconds) return;
            blinkElapsed.Restart();
            dimmed = !dimmed;
            handle = widget.Handle;
            if (dimmed) Alphas[handle] = 55;
            else
            {
                Alphas.Remove(handle);
                --flashesLeft;
                if (flashesLeft == 0) ReleaseBlinkForeground();
            }
            repaint();
            return;
        }
        if (elapsed.Elapsed.TotalMinutes < minutes) return;
        elapsed.Restart();
        const uint flags = 0x0001 | 0x0002 | 0x0010; // Preserve size, position and keyboard focus.
        SetWindowPos(widget.Handle, new IntPtr(-1), 0, 0, 0, 0, flags);
        raised = true;
        if (!widget.TopMost && !temporaryTopMost) SetWindowPos(widget.Handle, new IntPtr(-2), 0, 0, 0, 0, flags);
        flashesLeft = Math.Max(0, Math.Min(20, style.ForegroundBlinks));
        blinkTopMost = flashesLeft > 0 && style.ForegroundWhileBlinking;
        if (blinkTopMost) SetWindowPos(widget.Handle, new IntPtr(-1), 0, 0, 0, 0, flags);
        halfCycleMilliseconds = Math.Max(1, Math.Min(30, style.BlinkCycleSeconds)) * 500;
        if (flashesLeft > 0) timer.Interval = halfCycleMilliseconds;
        blinkElapsed.Restart();
    }

    void ResetBlink()
    {
        flashesLeft = 0;
        ReleaseBlinkForeground();
        blinkElapsed.Reset();
        timer.Interval = 1000;
        dimmed = false;
        if (handle == IntPtr.Zero || !Alphas.Remove(handle) || widget.IsDisposed || !widget.Visible) return;
        repaint();
    }

    void ReleaseBlinkForeground()
    {
        if (!blinkTopMost) return;
        blinkTopMost = false;
        if (!temporaryTopMost && !widget.IsDisposed && widget.IsHandleCreated && !widget.TopMost)
            SetWindowPos(widget.Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0013);
    }

    public void Dispose()
    {
        OutsideClick -= EndAttentionOnClick;
        if (OutsideClick == null && mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
        }
        timer.Dispose();
        foregroundTimer.Dispose();
        if (handle != IntPtr.Zero) Alphas.Remove(handle);
    }
}

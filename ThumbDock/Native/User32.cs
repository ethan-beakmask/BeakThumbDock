using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThumbDock.Native;

/// <summary>
/// user32 wrappers for foreign windows. Every call assumes the window may already be gone:
/// return values are checked and nothing throws.
/// </summary>
internal static class User32
{
    public const int WsExToolWindow = 0x00000080;
    public const int WsExAppWindow = 0x00040000;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExTopmost = 0x00000008;

    public static List<nint> EnumTopLevelWindows()
    {
        var list = new List<nint>();
        PInvoke.EnumWindows((hwnd, _) => { list.Add(hwnd); return true; }, 0);
        return list;
    }

    public static bool IsWindow(nint hwnd) => hwnd != 0 && PInvoke.IsWindow((HWND)hwnd);
    public static bool IsVisible(nint hwnd) => PInvoke.IsWindowVisible((HWND)hwnd);
    public static bool IsMinimized(nint hwnd) => PInvoke.IsIconic((HWND)hwnd);
    public static bool IsMaximized(nint hwnd) => PInvoke.IsZoomed((HWND)hwnd);
    public static bool HasOwner(nint hwnd) => PInvoke.GetWindow((HWND)hwnd, GET_WINDOW_CMD.GW_OWNER) != HWND.Null;
    public static int GetExStyle(nint hwnd) => PInvoke.GetWindowLong((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

    public static string GetTitle(nint hwnd)
    {
        int len = PInvoke.GetWindowTextLength((HWND)hwnd);
        if (len <= 0) return "";
        Span<char> buf = len < 1024 ? stackalloc char[len + 1] : new char[len + 1];
        int n = PInvoke.GetWindowText((HWND)hwnd, buf);
        return n > 0 ? new string(buf[..n]) : "";
    }

    public static string GetClassName(nint hwnd)
    {
        Span<char> buf = stackalloc char[256];
        int n = PInvoke.GetClassName((HWND)hwnd, buf);
        return n > 0 ? new string(buf[..n]) : "";
    }

    public static uint GetProcessId(nint hwnd)
    {
        PInvoke.GetWindowThreadProcessId((HWND)hwnd, out uint pid);
        return pid;
    }

    /// <summary>Window placement snapshot; null if the window is gone.</summary>
    public static WindowPlacement? GetPlacement(nint hwnd)
    {
        var wp = new WINDOWPLACEMENT { length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!PInvoke.GetWindowPlacement((HWND)hwnd, ref wp)) return null;
        var r = wp.rcNormalPosition;
        var normal = Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
        normal.Offset(WorkspaceOrigin());
        return new WindowPlacement(
            normal,
            Maximized: wp.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED,
            Minimized: wp.showCmd == SHOW_WINDOW_CMD.SW_SHOWMINIMIZED,
            RestoreToMaximized: (wp.flags & WINDOWPLACEMENT_FLAGS.WPF_RESTORETOMAXIMIZED) != 0);
    }

    /// <summary>
    /// Sets the window's normal rectangle (screen coordinates; this also picks its monitor) and shows it
    /// maximized or normal. Asynchronous so a hung target cannot block the UI thread.
    /// </summary>
    public static bool SetPlacement(nint hwnd, Rectangle normalBounds, bool maximized)
    {
        var n = normalBounds;
        var origin = WorkspaceOrigin();
        n.Offset(-origin.X, -origin.Y);
        var wp = new WINDOWPLACEMENT
        {
            length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>(),
            flags = WINDOWPLACEMENT_FLAGS.WPF_ASYNCWINDOWPLACEMENT,
            showCmd = maximized ? SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED : SHOW_WINDOW_CMD.SW_SHOWNORMAL,
            ptMinPosition = new System.Drawing.Point(-1, -1),
            ptMaxPosition = new System.Drawing.Point(-1, -1),
            rcNormalPosition = new RECT(n.Left, n.Top, n.Right, n.Bottom),
        };
        // A window that is already maximized ignores a maximize on another monitor (it stays where it is and only its
        // normal rectangle changes). Put it in the normal state at the target first, then maximize there.
        if (maximized && PInvoke.IsZoomed((HWND)hwnd))
        {
            var normal = wp;
            normal.showCmd = SHOW_WINDOW_CMD.SW_SHOWNORMAL;
            PInvoke.SetWindowPlacement((HWND)hwnd, in normal);
        }
        return PInvoke.SetWindowPlacement((HWND)hwnd, in wp);
    }

    public static Rectangle? GetBounds(nint hwnd) =>
        PInvoke.GetWindowRect((HWND)hwnd, out RECT r) ? Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom) : null;

    /// <summary>
    /// Origin of workspace coordinates = top-left of the primary monitor's work area.
    /// It moves when an AppBar (ours, or the taskbar) sits on the primary monitor's left or top edge.
    /// </summary>
    private static unsafe Point WorkspaceOrigin()
    {
        RECT wa;
        bool ok = PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWORKAREA, 0, &wa, 0);
        return ok ? new Point(wa.left, wa.top) : Point.Empty;
    }

    /// <summary>Effective DPI of the monitor containing <paramref name="point"/>; 96 on failure.</summary>
    public static int GetDpiAt(Point point)
    {
        HMONITOR monitor = PInvoke.MonitorFromPoint(point, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpi, out _).Succeeded && dpi > 0
            ? (int)dpi : 96;
    }

    public const int WM_HOTKEY = 0x0312;

    /// <summary>Registers a system-wide hotkey delivered as WM_HOTKEY to <paramref name="hwnd"/>. Auto-repeat is suppressed.</summary>
    public static bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey) =>
        PInvoke.RegisterHotKey((HWND)hwnd, id, (HOT_KEY_MODIFIERS)modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT, virtualKey);

    public static void UnregisterHotKey(nint hwnd, int id) => PInvoke.UnregisterHotKey((HWND)hwnd, id);

    public static nint GetForegroundWindow() => PInvoke.GetForegroundWindow();
    public static bool SetForegroundWindow(nint hwnd) => PInvoke.SetForegroundWindow((HWND)hwnd);

    /// <summary>
    /// SetForegroundWindow can be refused when this process did not receive the last input; attaching to the foreground
    /// thread's input queue for the call is the usual workaround. Used as a fallback only.
    /// </summary>
    public static bool ForceForeground(nint hwnd)
    {
        nint foreground = GetForegroundWindow();
        uint foregroundThread = foreground != 0 ? PInvoke.GetWindowThreadProcessId((HWND)foreground, out _) : 0;
        uint thisThread = PInvoke.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != thisThread && PInvoke.AttachThreadInput(thisThread, foregroundThread, true);
        PInvoke.BringWindowToTop((HWND)hwnd);
        bool ok = PInvoke.SetForegroundWindow((HWND)hwnd);
        if (attached) PInvoke.AttachThreadInput(thisThread, foregroundThread, false);
        return ok || GetForegroundWindow() == hwnd;
    }

    /// <summary>
    /// Puts the window at the top of the normal z-order band without activating it. A window that is already the
    /// foreground window is not raised by SetForegroundWindow, so one the wall covers needs this to come out again.
    /// Asynchronous so a hung target cannot block the UI thread.
    /// </summary>
    public static bool BringToTop(nint hwnd) =>
        PInvoke.SetWindowPos((HWND)hwnd, HWND.Null, 0, 0, 0, 0, // HWND.Null as the insert-after value means HWND_TOP
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    /// <summary>The top-level window drawn at a screen point (what the user would see there), or 0.</summary>
    public static nint TopLevelAt(Point screenPoint)
    {
        HWND hit = PInvoke.WindowFromPoint(screenPoint);
        return hit == HWND.Null ? 0 : PInvoke.GetAncestor(hit, GET_ANCESTOR_FLAGS.GA_ROOT);
    }
    public static bool Restore(nint hwnd) => PInvoke.ShowWindow((HWND)hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
    public static bool Maximize(nint hwnd) => PInvoke.ShowWindow((HWND)hwnd, SHOW_WINDOW_CMD.SW_MAXIMIZE);

    /// <summary>
    /// Un-minimizes a window without activating it and sends it to the bottom of the z-order,
    /// so it keeps rendering (and its thumbnail keeps updating) without covering anything (R-1).
    /// </summary>
    public static bool RestoreBehind(nint hwnd)
    {
        PInvoke.ShowWindow((HWND)hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        PInvoke.SetWindowPos((HWND)hwnd, HWND.HWND_BOTTOM, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);
        return !PInvoke.IsIconic((HWND)hwnd);
    }

    /// <summary>Invisible resize borders on each side (window rectangle minus DWM visible frame); zero if unknown.</summary>
    public static Padding GetInvisibleBorders(nint hwnd)
    {
        if (GetBounds(hwnd) is not { } win || Dwm.GetFrameBounds(hwnd) is not { } frame) return Padding.Empty;
        var p = new Padding(frame.Left - win.Left, frame.Top - win.Top, win.Right - frame.Right, win.Bottom - frame.Bottom);
        // Sanity: real borders are a few pixels; anything else means a measurement during a transition.
        return p.Left is < 0 or > 32 || p.Top is < 0 or > 32 || p.Right is < 0 or > 32 || p.Bottom is < 0 or > 32 ? Padding.Empty : p;
    }

    /// <summary>Moves and sizes a foreign window without waiting for it (SWP_ASYNCWINDOWPOS).</summary>
    public static bool MoveWindowAsync(nint hwnd, Rectangle bounds) =>
        PInvoke.SetWindowPos((HWND)hwnd, HWND.Null, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    public static bool MoveTo(nint hwnd, Point location) =>
        PInvoke.SetWindowPos((HWND)hwnd, HWND.Null, location.X, location.Y, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    public static bool MoveWindow(nint hwnd, Rectangle bounds) =>
        PInvoke.MoveWindow((HWND)hwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height, true);

    /// <summary>Puts the window into (or out of) the topmost band without activating it.</summary>
    public static bool SetTopmost(nint hwnd, bool topmost) =>
        PInvoke.SetWindowPos((HWND)hwnd, (HWND)(topmost ? -1 : -2), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    public static int GetDpi(nint hwnd)
    {
        uint dpi = PInvoke.GetDpiForWindow((HWND)hwnd);
        return dpi == 0 ? 96 : (int)dpi;
    }

    public static uint RegisterWindowMessage(string name) => PInvoke.RegisterWindowMessage(name);

    /// <summary>Asks the window for its icon with a short timeout so a hung window cannot block us.</summary>
    public static nint GetWindowIconHandle(nint hwnd)
    {
        foreach (uint kind in new[] { PInvoke.ICON_SMALL2, PInvoke.ICON_SMALL, PInvoke.ICON_BIG })
        {
            LRESULT ok = PInvoke.SendMessageTimeout((HWND)hwnd, PInvoke.WM_GETICON, kind, 0,
                SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_BLOCK, 100, out nuint icon);
            if (ok != 0 && icon != 0) return (nint)icon;
        }
        return 0;
    }

    /// <summary>
    /// Placement snapshot. <see cref="NormalBounds"/> is in screen coordinates
    /// (converted from GetWindowPlacement's workspace coordinates).
    /// </summary>
    public readonly record struct WindowPlacement(Rectangle NormalBounds, bool Maximized, bool Minimized, bool RestoreToMaximized)
    {
        /// <summary>True if the window is, or will come back as, maximized.</summary>
        public bool IsEffectivelyMaximized => Maximized || (Minimized && RestoreToMaximized);
    }

    public delegate void WinEventCallback(nint hwnd);

    /// <summary>
    /// Out-of-context hook for EVENT_SYSTEM_FOREGROUND. Callbacks arrive on the installing thread's message loop.
    /// Dispose the returned object to unhook; the delegate is kept alive by the returned object.
    /// </summary>
    public static IDisposable HookForeground(WinEventCallback callback)
    {
        WINEVENTPROC proc = (_, _, hwnd, _, _, _, _) => callback(hwnd);
        var handle = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND,
            null, proc, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        return new HookLifetime(handle, proc);
    }

    private sealed class HookLifetime(UnhookWinEventSafeHandle handle, WINEVENTPROC proc) : IDisposable
    {
        private WINEVENTPROC? _proc = proc; // keeps the delegate from being collected while hooked

        public void Dispose()
        {
            handle.Dispose();
            _proc = null;
        }
    }
}

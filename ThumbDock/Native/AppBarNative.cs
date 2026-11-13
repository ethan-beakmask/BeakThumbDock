using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using ThumbDock.Layout;
using Windows.Win32.UI.Shell;

namespace ThumbDock.Native;

/// <summary>SHAppBarMessage wrappers. Coordinates are physical pixels.</summary>
internal static class AppBarNative
{
    public const int NotifyPosChanged = (int)PInvoke.ABN_POSCHANGED;
    public const int NotifyFullScreenApp = (int)PInvoke.ABN_FULLSCREENAPP;

    public enum Edge : uint
    {
        Left = PInvoke.ABE_LEFT,
        Top = PInvoke.ABE_TOP,
        Right = PInvoke.ABE_RIGHT,
        Bottom = PInvoke.ABE_BOTTOM,
    }

    /// <summary>Explicit mapping: DockEdge (Left, Right, Top, Bottom) and ABE_* (Left, Top, Right, Bottom) differ in order.</summary>
    public static Edge FromDockEdge(DockEdge edge) => edge switch
    {
        DockEdge.Left => Edge.Left,
        DockEdge.Right => Edge.Right,
        DockEdge.Top => Edge.Top,
        _ => Edge.Bottom,
    };

    public static bool Register(nint hwnd, uint callbackMessage)
    {
        var data = NewData(hwnd);
        data.uCallbackMessage = callbackMessage;
        return PInvoke.SHAppBarMessage(PInvoke.ABM_NEW, ref data) != 0;
    }

    public static void Remove(nint hwnd)
    {
        var data = NewData(hwnd);
        PInvoke.SHAppBarMessage(PInvoke.ABM_REMOVE, ref data);
    }

    /// <summary>
    /// Proposes <paramref name="proposed"/> on <paramref name="edge"/>, lets the shell adjust it,
    /// then reserves the result. Returns the rectangle the shell approved.
    /// </summary>
    public static Rectangle QueryAndSetPos(nint hwnd, Edge edge, Rectangle proposed, int thickness)
    {
        var data = NewData(hwnd);
        data.uEdge = (uint)edge;
        data.rc = ToRect(proposed);
        PInvoke.SHAppBarMessage(PInvoke.ABM_QUERYPOS, ref data);
        ApplyThickness(ref data.rc, edge, thickness);
        PInvoke.SHAppBarMessage(PInvoke.ABM_SETPOS, ref data);
        return Rectangle.FromLTRB(data.rc.left, data.rc.top, data.rc.right, data.rc.bottom);
    }

    public static void NotifyActivate(nint hwnd)
    {
        var data = NewData(hwnd);
        PInvoke.SHAppBarMessage(PInvoke.ABM_ACTIVATE, ref data);
    }

    public static void NotifyWindowPosChanged(nint hwnd)
    {
        var data = NewData(hwnd);
        PInvoke.SHAppBarMessage(PInvoke.ABM_WINDOWPOSCHANGED, ref data);
    }

    private static void ApplyThickness(ref RECT rc, Edge edge, int thickness)
    {
        switch (edge)
        {
            case Edge.Left: rc.right = rc.left + thickness; break;
            case Edge.Right: rc.left = rc.right - thickness; break;
            case Edge.Top: rc.bottom = rc.top + thickness; break;
            case Edge.Bottom: rc.top = rc.bottom - thickness; break;
        }
    }

    private static APPBARDATA NewData(nint hwnd) =>
        new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = (HWND)hwnd };

    private static RECT ToRect(Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);
}

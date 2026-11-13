using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace ThumbDock.Native;

/// <summary>DWM thumbnail and cloak attribute wrappers. Never throws.</summary>
internal static class Dwm
{
    /// <summary>Value of DWM_CLOAKED_SHELL: window cloaked by the shell (e.g. on another virtual desktop).</summary>
    public const int CloakedShell = 0x2;

    /// <summary>Returns the thumbnail id, or 0 on failure.</summary>
    public static nint RegisterThumbnail(nint destination, nint source)
    {
        HRESULT hr = PInvoke.DwmRegisterThumbnail((HWND)destination, (HWND)source, out nint id);
        return hr.Succeeded ? id : 0;
    }

    public static void UnregisterThumbnail(nint id)
    {
        if (id != 0) PInvoke.DwmUnregisterThumbnail(id);
    }

    public static Size? QuerySourceSize(nint id)
    {
        if (id == 0) return null;
        HRESULT hr = PInvoke.DwmQueryThumbnailSourceSize(id, out SIZE size);
        return hr.Succeeded && size.cx > 0 && size.cy > 0 ? new Size(size.cx, size.cy) : null;
    }

    /// <summary>Sets the destination rectangle (in destination client coordinates) and visibility.</summary>
    public static bool UpdateThumbnail(nint id, Rectangle destination, bool visible)
    {
        if (id == 0) return false;
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = PInvoke.DWM_TNP_RECTDESTINATION | PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_OPACITY,
            rcDestination = new RECT(destination.Left, destination.Top, destination.Right, destination.Bottom),
            opacity = 255,
            fVisible = visible,
        };
        return PInvoke.DwmUpdateThumbnailProperties(id, in props).Succeeded;
    }

    /// <summary>DWMWA_CLOAKED value; 0 when not cloaked or on failure.</summary>
    public static unsafe int GetCloaked(nint hwnd)
    {
        int value = 0;
        HRESULT hr = PInvoke.DwmGetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &value, sizeof(int));
        return hr.Succeeded ? value : 0;
    }

    /// <summary>Visible frame of a window (DWMWA_EXTENDED_FRAME_BOUNDS) in physical pixels; null on failure.</summary>
    public static unsafe Rectangle? GetFrameBounds(nint hwnd)
    {
        RECT r;
        HRESULT hr = PInvoke.DwmGetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &r, (uint)sizeof(RECT));
        return hr.Succeeded ? Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom) : null;
    }
}

using System.Runtime.InteropServices;

namespace ThumbDock.Native;

/// <summary>
/// Wrapper over the documented IVirtualDesktopManager COM interface only.
/// Undocumented interfaces (needed for crossDesktop "pull") live in VirtualDesktopPull; see risk R-3.
/// </summary>
internal sealed class VirtualDesktop
{
    private readonly IVirtualDesktopManager? _manager;

    public VirtualDesktop()
    {
        try { _manager = (IVirtualDesktopManager)new VirtualDesktopManagerClass(); }
        catch (COMException) { _manager = null; }
        catch (InvalidCastException) { _manager = null; }
    }

    /// <summary>Desktop the window belongs to, or null if it has none (or the call failed).</summary>
    public Guid? GetDesktopId(nint hwnd)
    {
        if (_manager is null) return null;
        try
        {
            return _manager.GetWindowDesktopId(hwnd, out Guid id) == 0 && id != Guid.Empty ? id : null;
        }
        catch (COMException) { return null; }
    }

    /// <summary>True/false when known; null if the call failed.</summary>
    public bool? IsOnCurrentDesktop(nint hwnd)
    {
        if (_manager is null) return null;
        try
        {
            return _manager.IsWindowOnCurrentVirtualDesktop(hwnd, out int onCurrent) == 0 ? onCurrent != 0 : null;
        }
        catch (COMException) { return null; }
    }

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, out int onCurrentDesktop);
        [PreserveSig] int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
    }

    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerClass { }
}

using System.Runtime.InteropServices;
using ThumbDock.Core;

namespace ThumbDock.Native;

internal enum PullResult
{
    /// <summary>The window is now on the current desktop.</summary>
    Pulled,
    /// <summary>The window was already on the current desktop (or is on all desktops).</summary>
    NotNeeded,
    /// <summary>This Windows build has no known interface; pull is disabled for this run.</summary>
    Unsupported,
    /// <summary>The move was attempted and did not take effect.</summary>
    Failed,
}

/// <summary>
/// crossDesktop "pull" (risk R-3): moves another process's window to the current virtual desktop.
///
/// The documented IVirtualDesktopManager.MoveWindowToDesktop refuses windows of other processes
/// (E_ACCESSDENIED), so this uses the shell's undocumented IVirtualDesktopManagerInternal and
/// IApplicationViewCollection. Their IIDs change whenever Microsoft changes the vtable, so only
/// IIDs whose layout is known are requested: on any other build QueryService fails cleanly and the
/// caller falls back to "switch". Only four vtable slots are used and called through raw pointers;
/// every COM pointer is released in the same method that obtained it.
/// </summary>
internal sealed unsafe class VirtualDesktopPull
{
    private static readonly Guid ClsidImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid IidServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid SidVirtualDesktopManagerInternal = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid IidApplicationViewCollection = new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");

    // IVirtualDesktopManagerInternal IIDs whose first methods are
    // GetCount(), MoveViewToDesktop(view, desktop), CanViewMoveDesktops(view), GetCurrentDesktop(out desktop).
    private static readonly (Guid Iid, string Builds)[] KnownManagerIids =
    [
        (new("53F5CA0B-158F-4124-900C-057158060B27"), "Windows 11 24H2+, also late 23H2 updates (verified on 22631.6199)"),
        (new("A3175F2D-239C-4BD2-8AA0-EEBA8B0B138E"), "Windows 11 22H2 (22621.2215+) / early 23H2"),
        (new("F31574D6-B682-4CDC-BD56-1827860ABEC6"), "Windows 10"),
    ];

    // vtable slots (IUnknown takes 0–2).
    private const int SlotQueryService = 3;
    private const int SlotGetViewForHwnd = 6;
    private const int SlotMoveViewToDesktop = 4;
    private const int SlotGetCurrentDesktop = 6;

    private const uint ClsctxLocalServer = 0x4;

    private readonly VirtualDesktop _desktops;
    private bool _unsupported;

    public VirtualDesktopPull(VirtualDesktop desktops) => _desktops = desktops;

    /// <summary>False once a run found no usable interface; pull is then skipped without retrying.</summary>
    public bool Available => !_unsupported;

    /// <summary>True only when Windows says the window is on a virtual desktop other than the current one.</summary>
    public bool IsOnOtherDesktop(nint hwnd) => _desktops.IsOnCurrentDesktop(hwnd) == false;

    public PullResult TryPull(nint hwnd)
    {
        if (_desktops.IsOnCurrentDesktop(hwnd) != false) return PullResult.NotNeeded;
        if (_unsupported) return PullResult.Unsupported;
        nint shell = 0, manager = 0, views = 0, view = 0, desktop = 0;
        try
        {
            if (CoCreateInstance(ClsidImmersiveShell, 0, ClsctxLocalServer, IidServiceProvider, out shell) < 0)
                return Fail("ImmersiveShell unavailable", unsupported: false);
            manager = QueryManager(shell, out string? builds);
            if (manager == 0) return Fail("no known IVirtualDesktopManagerInternal IID on this build", unsupported: true);
            if (QueryService(shell, IidApplicationViewCollection, IidApplicationViewCollection, out views) < 0)
                return Fail("IApplicationViewCollection unavailable", unsupported: true);

            int hr = CallWithArg(views, SlotGetViewForHwnd, hwnd, out view);
            if (hr < 0 || view == 0) return Fail($"GetViewForHwnd 0x{hr:X8}", unsupported: false);
            hr = CallOut(manager, SlotGetCurrentDesktop, out desktop);
            if (hr < 0 || desktop == 0) return Fail($"GetCurrentDesktop 0x{hr:X8}", unsupported: false);
            hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)VTable(manager, SlotMoveViewToDesktop))(manager, view, desktop);
            if (hr < 0) return Fail($"MoveViewToDesktop 0x{hr:X8}", unsupported: false);

            bool ok = _desktops.IsOnCurrentDesktop(hwnd) == true;
            AppLog.Info($"pull 0x{hwnd:X} via {builds} interface -> {(ok ? "ok" : "not moved")}");
            return ok ? PullResult.Pulled : PullResult.Failed;
        }
        catch (Exception ex) when (ex is COMException or SEHException or InvalidCastException)
        {
            return Fail(ex.Message, unsupported: false);
        }
        finally
        {
            Release(desktop);
            Release(view);
            Release(views);
            Release(manager);
            Release(shell);
        }
    }

    private PullResult Fail(string why, bool unsupported)
    {
        if (unsupported) _unsupported = true;
        AppLog.Info($"pull failed: {why}{(unsupported ? " (pull disabled for this run)" : "")}");
        return unsupported ? PullResult.Unsupported : PullResult.Failed;
    }

    private static nint QueryManager(nint shell, out string? builds)
    {
        foreach (var (iid, name) in KnownManagerIids)
        {
            if (QueryService(shell, SidVirtualDesktopManagerInternal, iid, out nint manager) >= 0 && manager != 0)
            {
                builds = name;
                return manager;
            }
        }
        builds = null;
        return 0;
    }

    private static int QueryService(nint provider, Guid service, Guid iid, out nint result)
    {
        nint value = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)VTable(provider, SlotQueryService))(provider, &service, &iid, &value);
        result = value;
        return hr;
    }

    private static int CallWithArg(nint self, int slot, nint arg, out nint result)
    {
        nint value = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)VTable(self, slot))(self, arg, &value);
        result = value;
        return hr;
    }

    private static int CallOut(nint self, int slot, out nint result)
    {
        nint value = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)VTable(self, slot))(self, &value);
        result = value;
        return hr;
    }

    private static nint VTable(nint self, int slot) => (*(nint**)self)[slot];

    private static void Release(nint p)
    {
        if (p != 0) Marshal.Release(p);
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint result);
}

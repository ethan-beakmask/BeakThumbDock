using System.Diagnostics;
using ThumbDock.Native;

namespace ThumbDock.Core;

/// <summary>FR-1: lists watchable top-level windows, including those on other virtual desktops.</summary>
internal sealed class WindowEnumerator(VirtualDesktop desktops, DesktopNames desktopNames)
{
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public VirtualDesktop Desktops => desktops;

    public List<WindowInfo> Enumerate()
    {
        desktopNames.Reload();
        var processNames = SnapshotProcessNames();
        var result = new List<WindowInfo>();
        foreach (nint hwnd in User32.EnumTopLevelWindows())
        {
            if (!User32.IsVisible(hwnd)) continue; // cheap early-out before the more expensive reads
            var traits = ReadTraits(hwnd);
            if (WindowFilter.IsCandidate(traits)) result.Add(Describe(hwnd, traits, processNames));
        }
        return result;
    }

    private WindowTraits ReadTraits(nint hwnd)
    {
        uint pid = User32.GetProcessId(hwnd);
        int cloaked = Dwm.GetCloaked(hwnd);
        return new WindowTraits(
            Visible: true,
            Title: User32.GetTitle(hwnd),
            ExStyle: User32.GetExStyle(hwnd),
            HasOwner: User32.HasOwner(hwnd),
            IsOwnProcess: pid == _ownPid,
            Cloaked: cloaked,
            DesktopId: desktops.GetDesktopId(hwnd),
            IsOnCurrentDesktop: desktops.IsOnCurrentDesktop(hwnd));
    }

    private WindowInfo Describe(nint hwnd, in WindowTraits t, Dictionary<uint, string> processNames)
    {
        uint pid = User32.GetProcessId(hwnd);
        bool onCurrent = t.IsOnCurrentDesktop ?? true;
        return new WindowInfo(
            hwnd, t.Title, User32.GetClassName(hwnd),
            processNames.TryGetValue(pid, out var name) ? name : $"pid {pid}",
            pid, t.DesktopId, onCurrent, DesktopLabel(t.DesktopId, onCurrent));
    }

    private string DesktopLabel(Guid? id, bool onCurrent)
    {
        if (onCurrent) return Loc.T("目前桌面");
        return id is { } g ? desktopNames.NameOf(g) ?? Loc.T("其他桌面") : Loc.T("其他桌面");
    }

    // Process names come from the system process list, which works even for elevated processes.
    private static Dictionary<uint, string> SnapshotProcessNames()
    {
        var map = new Dictionary<uint, string>();
        foreach (var p in Process.GetProcesses())
        {
            using (p) map[(uint)p.Id] = p.ProcessName + ".exe";
        }
        return map;
    }
}

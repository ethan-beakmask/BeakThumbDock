using ThumbDock.Core;
using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.UI;

/// <summary>
/// Single-monitor fill ("wall"): shows a source window above the wall when its thumbnail is clicked, remembers how the
/// window looked before the wall maximized it, and puts it back when returning to the wall. Browsers hidden under a
/// maximized window stop painting, so without the restore their thumbnails would freeze. Never minimizes anything.
/// </summary>
internal sealed class WallController : IDisposable
{
    private const int ReRaiseMs = 250;

    private readonly Func<DockForm> _dock;
    private readonly WindowActivator _activator;
    private readonly Dictionary<nint, WindowBefore> _before = [];
    private readonly System.Windows.Forms.Timer _reRaise = new() { Interval = ReRaiseMs };
    private readonly Dictionary<nint, (WindowBefore Before, long Tick)> _restored = [];
    private nint _last;
    private WallCommand _lastCommand;
    private long _lastCommandTick;

    public WallController(Func<DockForm> dock, WindowActivator activator)
    {
        _dock = dock;
        _activator = activator;
        // SetPlacement is asynchronous, so a restored window can activate itself after the first raise; raise once more.
        _reRaise.Tick += (_, _) => { _reRaise.Stop(); if (_dock().IsWall) _dock().RaiseWall(); };
    }

    /// <summary>Brings the slot's window above the wall (maximized by default) after putting back the previous one.</summary>
    public ActivationResult Show(SlotControl slot, LayoutModel layout)
    {
        nint src = slot.Source;
        _reRaise.Stop(); // a pending re-raise from a quick return would put the wall back above this window
        var dock = _dock();
        string wallMonitor = dock.Zone.ThumbDevice;
        var screen = MonitorService.ScreenFor(wallMonitor) ?? MonitorService.Primary;

        if (layout.RestoreOnReturn)
        {
            RestoreAll(except: src, wallMonitor);
            if (!_before.ContainsKey(src))
            {
                WindowBefore? current = User32.GetPlacement(src) is { } now
                    ? new WindowBefore(now.Maximized, now.Minimized, now.NormalBounds, MonitorService.ScreenOf(src, now).DeviceName) : null;
                WindowBefore? recent = _restored.TryGetValue(src, out var r) ? r.Before : null;
                long age = _restored.TryGetValue(src, out r) ? Environment.TickCount64 - r.Tick : long.MaxValue;
                if (WallPlanner.PickBefore(recent, age, current) is { } before) _before[src] = before;
            }
        }

        // Zone is empty: the wall lies below the window, so "fit" must use the whole work area.
        var ctx = new ActivationContext(null, screen, Rectangle.Empty, layout.CrossDesktop, ForceForeground: true);
        var result = _activator.Activate(src, slot.Model.Place, slot.Model.Home, ctx);
        if (result == ActivationResult.SourceGone) _before.Remove(src);
        else
        {
            // Returning to the wall leaves the restored window as the foreground window, just covered by the wall.
            // Activating an already-foreground window does not raise it, so raise it explicitly.
            User32.BringToTop(src);
            _last = src;
            Remember(WallCommand.ShowSource);
        }
        return result;
    }

    /// <summary>Restores the windows the wall maximized (when the layout asks for it) and raises the wall.</summary>
    public void ReturnToWall(LayoutModel layout)
    {
        var dock = _dock();
        bool restored = layout.RestoreOnReturn && _before.Count > 0;
        if (layout.RestoreOnReturn) RestoreAll(except: 0, dock.Zone.ThumbDevice);
        else _before.Clear();
        dock.RaiseWall();
        Remember(WallCommand.ReturnToWall);
        if (restored) { _reRaise.Stop(); _reRaise.Start(); }
    }

    /// <summary>The return hotkey: raise the wall, or when it is already on top go back to the last source window.</summary>
    public void Toggle(LayoutModel layout)
    {
        var dock = _dock();
        var lastSlot = _last == 0 || !User32.IsWindow(_last) ? null : dock.SlotFor(_last);
        bool onTop = WallPlanner.EffectiveWallOnTop(dock.WallIsOnTop(), _lastCommand, Environment.TickCount64 - _lastCommandTick);
        if (WallPlanner.Toggle(onTop, lastSlot is not null) == WallToggleAction.ShowLastSource) Show(lastSlot!, layout);
        else ReturnToWall(layout);
    }

    /// <summary>Puts every window back without raising anything (layout switch, exit).</summary>
    public void RestoreAll(LayoutModel layout)
    {
        if (layout.RestoreOnReturn) RestoreAll(except: 0, _dock().Zone.ThumbDevice);
        else _before.Clear();
    }

    private void RestoreAll(nint except, string wallMonitor)
    {
        foreach (var (hwnd, before) in _before.ToList())
        {
            if (hwnd == except) continue;
            _before.Remove(hwnd);
            if (RestoreOne(hwnd, before, wallMonitor)) _restored[hwnd] = (before, Environment.TickCount64);
        }
    }

    private void Remember(WallCommand command)
    {
        _lastCommand = command;
        _lastCommandTick = Environment.TickCount64;
    }

    private static bool RestoreOne(nint hwnd, WindowBefore before, string wallMonitor)
    {
        if (!User32.IsWindow(hwnd) || User32.GetPlacement(hwnd) is not { } now) return false;
        var action = WallPlanner.Decide(before, new WindowNow(now.Maximized, MonitorService.ScreenOf(hwnd, now).DeviceName), wallMonitor);
        if (action.Kind == RestoreKind.None) return false;
        bool ok = User32.SetPlacement(hwnd, action.Bounds, action.Kind == RestoreKind.Maximized);
        AppLog.Info($"wall: restore 0x{hwnd:X} {action.Kind} {action.Bounds} -> {ok}");
        return ok;
    }

    public void Dispose() => _reRaise.Dispose();
}

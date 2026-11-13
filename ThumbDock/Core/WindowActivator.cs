using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.Core;

internal enum ActivationResult { Activated, SourceGone, ForegroundRefused }

/// <param name="ThumbDevice">Thumbnail monitor in dual mode; null in single mode.</param>
/// <param name="FocusScreen">Monitor windows are redirected to in dual mode (the only monitor in single mode).</param>
/// <param name="Zone">Screen rectangle of the visible thumbnail zone, or empty; excluded from "fit".</param>
/// <param name="CrossDesktop">What to do with a window on another virtual desktop.</param>
/// <param name="ForceForeground">Retry with the input-queue workaround when Windows refuses to bring the window to the front (the wall).</param>
internal sealed record ActivationContext(string? ThumbDevice, Screen FocusScreen, Rectangle Zone, CrossDesktop CrossDesktop, bool ForceForeground = false);

/// <summary>
/// FR-3: brings a source window to the front and places it according to <see cref="PlacementPlanner"/>.
/// Never minimizes any window (minimized windows stop updating their thumbnails, R-1).
/// </summary>
internal sealed class WindowActivator(VirtualDesktopPull pull)
{
    /// <summary>Raised once per run when "pull" cannot work and "switch" is used instead.</summary>
    public event Action<string>? PullFellBack;

    /// <summary>The window was activated but needs moving that ThumbDock is not allowed to do (the window is elevated).</summary>
    public event Action<nint>? MoveBlocked;

    private bool _fallbackReported;

    public ActivationResult Activate(nint hwnd, PlaceMode place, SavedPlacement? home, ActivationContext ctx)
    {
        if (!User32.IsWindow(hwnd) || User32.GetPlacement(hwnd) is not { } now) return ActivationResult.SourceGone;

        if (ctx.CrossDesktop == CrossDesktop.Pull) Pull(hwnd);

        var connectedHome = home is not null && MonitorService.ScreenFor(home.Monitor) is not null ? home : null;
        var current = MonitorService.ScreenOf(hwnd, now);
        var input = new PlacementInput(place, now.Minimized, now.Maximized,
            current.DeviceName, connectedHome?.Monitor, ctx.ThumbDevice, ctx.FocusScreen.DeviceName);
        var action = PlacementPlanner.Decide(input);

        // crossDesktop "switch" (or a pull that failed): activating a window on another
        // virtual desktop makes Windows switch to that desktop (S-4).
        if (action != PlacementAction.None && ProcessElevation.BlocksMoving(hwnd))
        {
            AppLog.Info($"activate 0x{hwnd:X}: window is elevated, {action} will not work");
            MoveBlocked?.Invoke(hwnd);
        }
        // A plain SetForegroundWindow reports success for a window on another desktop but does not switch to it
        // (seen with Chrome in dual mode); attaching to the foreground thread first does.
        bool otherDesktop = pull.IsOnOtherDesktop(hwnd);
        bool foreground = otherDesktop ? User32.ForceForeground(hwnd) : User32.SetForegroundWindow(hwnd);
        Apply(hwnd, action, now, connectedHome, current, ctx);
        if (!foreground && ctx.ForceForeground) foreground = User32.ForceForeground(hwnd);
        AppLog.Info($"activate 0x{hwnd:X}: {input} -> {action}, foreground={foreground}{(otherDesktop ? ", other desktop" : "")}");
        return foreground ? ActivationResult.Activated : ActivationResult.ForegroundRefused;
    }

    private void Pull(nint hwnd)
    {
        var result = pull.TryPull(hwnd);
        if (result is PullResult.Pulled or PullResult.NotNeeded || _fallbackReported) return;
        _fallbackReported = true;
        PullFellBack?.Invoke(result == PullResult.Unsupported
            ? Loc.T("這個 Windows 版本無法把視窗搬到目前桌面，已改為切換到視窗所在的桌面。")
            : Loc.T("無法把視窗搬到目前桌面，已改為切換到視窗所在的桌面。"));
    }

    private static void Apply(nint hwnd, PlacementAction action, User32.WindowPlacement now, SavedPlacement? home, Screen current, ActivationContext ctx)
    {
        switch (action)
        {
            case PlacementAction.Maximize: User32.Maximize(hwnd); break;
            case PlacementAction.Restore: User32.Restore(hwnd); break;
            case PlacementAction.RestoreHome: PlaceAtHome(hwnd, home, maximized: false); break;
            case PlacementAction.MaximizeAtHome: PlaceAtHome(hwnd, home, maximized: true); break;
            case PlacementAction.Fit: Fit(hwnd, HomeScreen(home) ?? current, ctx.Zone, now); break;
            case PlacementAction.MoveToFocus: PlaceOnFocus(hwnd, now, ctx.FocusScreen, maximized: false); break;
            case PlacementAction.MaximizeOnFocus: PlaceOnFocus(hwnd, now, ctx.FocusScreen, maximized: true); break;
            case PlacementAction.FitOnFocus: Fit(hwnd, ctx.FocusScreen, ctx.Zone, now); break;
        }
    }

    private static Screen? HomeScreen(SavedPlacement? home) => home is null ? null : MonitorService.ScreenFor(home.Monitor);

    private static void PlaceAtHome(nint hwnd, SavedPlacement? home, bool maximized)
    {
        if (home is null) { if (maximized) User32.Maximize(hwnd); else User32.Restore(hwnd); return; }
        User32.SetPlacement(hwnd, home.Bounds, maximized);
    }

    // Keeps the window's normal size (shrunk if needed) centered on the focus monitor's work area.
    private static void PlaceOnFocus(nint hwnd, User32.WindowPlacement now, Screen focus, bool maximized) =>
        User32.SetPlacement(hwnd, SlotGeometry.CenterIn(focus.WorkingArea, now.NormalBounds.Size), maximized);

    /// <summary>
    /// "fit": normal (not maximized) window whose visible frame covers the monitor's operation area.
    /// Windows 10/11 frames have invisible resize borders, so the window rectangle is grown by them.
    /// They can only be measured on a normal window: when the window is maximized or minimized it is
    /// placed first and corrected once it has become normal. All moves are asynchronous so a hung
    /// target cannot block the UI thread.
    /// </summary>
    private static void Fit(nint hwnd, Screen screen, Rectangle zone, User32.WindowPlacement now)
    {
        var area = SlotGeometry.OperationArea(screen.WorkingArea, zone);
        if (!now.Minimized && !now.Maximized)
        {
            User32.SetPlacement(hwnd, SlotGeometry.CompensateFrame(area, User32.GetInvisibleBorders(hwnd)), maximized: false);
            return;
        }
        User32.SetPlacement(hwnd, area, maximized: false);
        var timer = new System.Windows.Forms.Timer { Interval = FitCorrectionMs };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            if (!User32.IsWindow(hwnd) || User32.GetPlacement(hwnd) is not { Minimized: false, Maximized: false }) return;
            var borders = User32.GetInvisibleBorders(hwnd);
            if (borders != Padding.Empty) User32.MoveWindowAsync(hwnd, SlotGeometry.CompensateFrame(area, borders));
        };
        timer.Start();
    }

    private const int FitCorrectionMs = 200;
}

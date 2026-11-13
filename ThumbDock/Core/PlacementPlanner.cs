using ThumbDock.Layout;

namespace ThumbDock.Core;

internal enum PlacementAction
{
    /// <summary>Leave position and size alone; only activate.</summary>
    None,
    /// <summary>Maximize on the monitor the window is on now.</summary>
    Maximize,
    /// <summary>Plain restore (un-minimize / un-maximize) where it is.</summary>
    Restore,
    /// <summary>Put the window back to its recorded home rectangle, not maximized.</summary>
    RestoreHome,
    /// <summary>Put the window back on its home monitor, maximized.</summary>
    MaximizeAtHome,
    /// <summary>Size the window to the operation area of its home monitor (current monitor if no home), not maximized.</summary>
    Fit,
    /// <summary>Move from the thumbnail monitor to the focus monitor, normal size.</summary>
    MoveToFocus,
    /// <summary>Move from the thumbnail monitor to the focus monitor and maximize there.</summary>
    MaximizeOnFocus,
    /// <summary>Move from the thumbnail monitor to the focus monitor, sized to its operation area.</summary>
    FitOnFocus,
}

/// <param name="Want">The slot's placement option.</param>
/// <param name="CurrentMonitor">Monitor device the window is on now.</param>
/// <param name="HomeMonitor">Monitor recorded when bound, if still connected.</param>
/// <param name="ThumbMonitor">Thumbnail monitor in dual mode; null in single mode.</param>
/// <param name="FocusMonitor">Focus monitor (equals the only monitor in single mode).</param>
internal readonly record struct PlacementInput(
    PlaceMode Want, bool IsMinimized, bool IsMaximized,
    string CurrentMonitor, string? HomeMonitor, string? ThumbMonitor, string FocusMonitor);

/// <summary>
/// Decides what to do with a source window when its thumbnail is clicked. Rules:
/// 1. A window stays on its home monitor (where it was when bound) — it is never pulled elsewhere.
/// 2. Exception, dual mode only: a window whose home is the thumbnail monitor goes to the focus monitor (FR-3).
/// </summary>
internal static class PlacementPlanner
{
    public static PlacementAction Decide(in PlacementInput p)
    {
        string home = p.HomeMonitor ?? p.CurrentMonitor;
        bool redirect = p.ThumbMonitor is not null && p.ThumbMonitor != p.FocusMonitor && home == p.ThumbMonitor;
        return redirect ? DecideRedirected(p) : DecideAtHome(p, home);
    }

    private static PlacementAction DecideAtHome(in PlacementInput p, string home)
    {
        // Fit is re-applied on every click: the window may have been resized since.
        if (p.Want == PlaceMode.Fit) return PlacementAction.Fit;
        if (p.CurrentMonitor != home) return p.Want == PlaceMode.Maximize ? PlacementAction.MaximizeAtHome : PlacementAction.RestoreHome;
        if (p.Want == PlaceMode.Maximize) return p.IsMaximized && !p.IsMinimized ? PlacementAction.None : PlacementAction.Maximize;
        return p.IsMaximized || p.IsMinimized ? PlacementAction.RestoreHome : PlacementAction.None;
    }

    private static PlacementAction DecideRedirected(in PlacementInput p)
    {
        if (p.Want == PlaceMode.Fit) return PlacementAction.FitOnFocus;
        if (p.CurrentMonitor != p.FocusMonitor) return p.Want == PlaceMode.Maximize ? PlacementAction.MaximizeOnFocus : PlacementAction.MoveToFocus;
        if (p.Want == PlaceMode.Maximize) return p.IsMaximized && !p.IsMinimized ? PlacementAction.None : PlacementAction.Maximize;
        return p.IsMaximized || p.IsMinimized ? PlacementAction.Restore : PlacementAction.None;
    }
}

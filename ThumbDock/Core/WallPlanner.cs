namespace ThumbDock.Core;

/// <summary>State of a source window before the wall maximized it for the first time.</summary>
/// <param name="NormalBounds">The window's restore rectangle (screen pixels); valid for normal, maximized and minimized windows.</param>
/// <param name="Monitor">Device name of the monitor the window was on.</param>
internal sealed record WindowBefore(bool Maximized, bool Minimized, Rectangle NormalBounds, string Monitor);

/// <param name="Monitor">Device name of the monitor the window is on now.</param>
internal sealed record WindowNow(bool Maximized, string Monitor);

internal enum RestoreKind { None, Normal, Maximized }

/// <param name="Bounds">Restore rectangle to apply (screen pixels); ignored for <see cref="RestoreKind.None"/>.</param>
internal readonly record struct RestoreAction(RestoreKind Kind, Rectangle Bounds);

internal enum WallToggleAction { RaiseWall, ShowLastSource }

/// <summary>The last thing the wall did, used to decide the next hotkey press while windows are still moving.</summary>
internal enum WallCommand { None, ShowSource, ReturnToWall }

/// <summary>
/// Pure decisions of the single-screen "wall" (fill zone on the only monitor), kept free of Win32 so they can be tested.
/// A browser covered by a maximized window stops painting, so a window the wall maximized is put back when returning to the wall.
/// </summary>
internal static class WallPlanner
{
    /// <summary>
    /// What to do with a source window when returning to the wall. Only windows that are maximized now are touched:
    /// a window the user already restored or moved is left alone. Minimized windows are restored to normal, never minimized again.
    /// </summary>
    public static RestoreAction Decide(WindowBefore before, WindowNow now, string wallMonitor)
    {
        var none = new RestoreAction(RestoreKind.None, Rectangle.Empty);
        if (!now.Maximized) return none;
        bool wasMaximized = before.Maximized && !before.Minimized;
        if (wasMaximized && before.Monitor == wallMonitor) return none; // it was a full-screen window already
        return new RestoreAction(wasMaximized ? RestoreKind.Maximized : RestoreKind.Normal, before.NormalBounds);
    }

    /// <summary>A restore this recent may not have been applied yet (placement changes are asynchronous).</summary>
    public const long RecentRestoreMs = 1500;

    /// <summary>After a command, the window order needs a moment to settle; the measured state is not trusted until then.</summary>
    public const long SettleMs = 700;

    /// <summary>
    /// The state to remember for a window about to be shown again. Reading it right after we restored it can return the
    /// old maximized state, which would make the next restore think the window was maximized all along.
    /// </summary>
    public static WindowBefore? PickBefore(WindowBefore? recentlyRestored, long ageMs, WindowBefore? current) =>
        recentlyRestored is not null && ageMs is >= 0 and < RecentRestoreMs ? recentlyRestored : current;

    /// <summary>
    /// Whether the wall counts as "on top" for the hotkey. Right after a command the answer is what that command
    /// intended, so pressing quickly alternates reliably instead of acting on a half-finished window change.
    /// </summary>
    public static bool EffectiveWallOnTop(bool measured, WallCommand last, long ageMs) =>
        ageMs is >= 0 and < SettleMs
            ? last switch { WallCommand.ShowSource => false, WallCommand.ReturnToWall => true, _ => measured }
            : measured;

    /// <summary>The return hotkey: when the wall is not on top, bring it up; when it already is, go back to the last source window.</summary>
    public static WallToggleAction Toggle(bool wallOnTop, bool lastSourceUsable) =>
        wallOnTop && lastSourceUsable ? WallToggleAction.ShowLastSource : WallToggleAction.RaiseWall;
}

namespace ThumbDock.Core;

/// <summary>Progress of the cursor in the hot zone. <see cref="EnteredAt"/> is -1 while outside; <see cref="Fired"/> stays true until it leaves.</summary>
internal readonly record struct HotEdgeState(long EnteredAt, bool Fired)
{
    public static HotEdgeState Idle => new(-1, false);
}

/// <summary>
/// Pure logic for the hot edge: the cursor resting at the left edge of the wall's monitor, in the middle third of
/// its height, for <see cref="DwellMs"/> returns to the wall. A pressed mouse button (dragging a window to the edge)
/// never counts, and after firing the cursor has to leave the zone before it can fire again.
/// </summary>
internal static class HotEdgePlanner
{
    public const int EdgePx = 2, DwellMs = 400;

    public static bool InZone(Point cursor, Rectangle monitor) =>
        cursor.X >= monitor.Left && cursor.X < monitor.Left + EdgePx &&
        cursor.Y >= monitor.Top + monitor.Height / 3 && cursor.Y < monitor.Top + monitor.Height * 2 / 3;

    public static (HotEdgeState State, bool Trigger) Step(HotEdgeState state, Point cursor, Rectangle monitor, bool buttonDown, long nowMs)
    {
        if (buttonDown || !InZone(cursor, monitor)) return (HotEdgeState.Idle, false);
        if (state.Fired) return (state, false);
        if (state.EnteredAt < 0) return (new HotEdgeState(nowMs, false), false);
        return nowMs - state.EnteredAt >= DwellMs ? (state with { Fired = true }, true) : (state, false);
    }
}

using ThumbDock.Layout;

namespace ThumbDock.Core;

/// <summary>FR-5: decides which waiting slots can be bound to which windows. Pure, so it is unit tested.</summary>
internal static class SlotBinder
{
    /// <param name="rules">Rule per slot (null = no rule).</param>
    /// <param name="bound">HWND currently bound per slot (0 = waiting).</param>
    /// <param name="windows">Watchable windows in z-order.</param>
    /// <returns>(slot index, hwnd) pairs; a window is never assigned to two slots.</returns>
    public static List<(int Slot, nint Hwnd)> Plan(IReadOnlyList<MatchRule?> rules, IReadOnlyList<nint> bound, IReadOnlyList<WindowInfo> windows)
    {
        var taken = bound.Where(h => h != 0).ToHashSet();
        var plan = new List<(int, nint)>();
        for (int i = 0; i < rules.Count; i++)
        {
            if (bound[i] != 0 || rules[i] is not { IsEmpty: false } rule) continue;
            nint hwnd = WindowMatcher.FindMatch(rule, windows, taken);
            if (hwnd == 0) continue;
            plan.Add((i, hwnd));
            taken.Add(hwnd);
        }
        return plan;
    }
}

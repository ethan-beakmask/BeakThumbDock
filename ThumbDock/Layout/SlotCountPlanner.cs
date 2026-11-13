namespace ThumbDock.Layout;

internal enum SlotCountAction { None, Add, Remove, Blocked }

/// <param name="RemoveIds">Slots to delete (Remove only).</param>
/// <param name="MissingUnbinds">How many more slots the user must unbind first (Blocked only).</param>
internal sealed record SlotCountPlan(SlotCountAction Action, IReadOnlyList<int> RemoveIds, int MissingUnbinds);

/// <summary>
/// Pure logic for changing the number of slots of a layout.
/// Growing puts new slots into free space without moving existing ones (falls back to a full re-arrange);
/// shrinking only deletes slots that are not bound (no rule and no live window).
/// </summary>
internal static class SlotCountPlanner
{
    public const int MinSlots = 1, MaxSlots = GridSpec.MaxCells;
    private const double Eps = 1e-9, Step = 0.005;

    /// <summary>A slot is bound when it has a match rule or currently shows a window.</summary>
    public static bool IsBound(SlotModel slot, IReadOnlySet<int> liveBoundIds) =>
        slot.Match is not null || liveBoundIds.Contains(slot.Id);

    public static SlotCountPlan Plan(IReadOnlyList<SlotModel> slots, IReadOnlySet<int> liveBoundIds, int target)
    {
        int delta = slots.Count - target;
        if (delta == 0) return new SlotCountPlan(SlotCountAction.None, [], 0);
        if (delta < 0) return new SlotCountPlan(SlotCountAction.Add, [], 0);
        var removable = slots.Where(s => !IsBound(s, liveBoundIds)).Reverse().Take(delta).Select(s => s.Id).ToList();
        return removable.Count == delta
            ? new SlotCountPlan(SlotCountAction.Remove, removable, 0)
            : new SlotCountPlan(SlotCountAction.Blocked, [], delta - removable.Count);
    }

    /// <summary>
    /// Appends slots until there are <paramref name="target"/>. Returns true when there was no free space
    /// and every slot had to be re-arranged (rules and ids are kept).
    /// </summary>
    public static bool AddSlots(List<SlotModel> slots, int target, ZoneType type, DockEdge edge)
    {
        int nextId = slots.Count == 0 ? 1 : slots.Max(s => s.Id) + 1;
        var (w, h) = DefaultLayouts.CellSize(target, type, edge);
        while (slots.Count < target)
        {
            if (FindFree(slots.Select(s => s.Rect).ToList(), w, h) is not { } rect)
            {
                while (slots.Count < target) slots.Add(new SlotModel { Id = nextId++ });
                DefaultLayouts.Arrange(slots, type, edge);
                return true;
            }
            slots.Add(new SlotModel { Id = nextId++, Rect = rect });
        }
        return false;
    }

    // First position (top to bottom, then left to right) where a w×h slot keeps the default gap to every other slot.
    private static RelRect? FindFree(List<RelRect> existing, double w, double h)
    {
        double gap = DefaultLayouts.SlotGap;
        for (double y = gap; y + h <= 1 - gap + Eps; y += Step)
            for (double x = gap; x + w <= 1 - gap + Eps; x += Step)
                if (existing.All(e => !Overlaps(e, x, y, w, h, gap))) return new RelRect(x, y, w, h);
        return null;
    }

    private static bool Overlaps(RelRect e, double x, double y, double w, double h, double gap) =>
        x < e.X + e.W + gap - Eps && x + w > e.X - gap + Eps &&
        y < e.Y + e.H + gap - Eps && y + h > e.Y - gap + Eps;
}

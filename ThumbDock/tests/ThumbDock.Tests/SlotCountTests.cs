using ThumbDock.Layout;

namespace ThumbDock.Tests;

public class SlotCountTests
{
    private static List<SlotModel> Slots(int n, params int[] withRule)
    {
        var slots = DefaultLayouts.Create("t", n).Slots;
        foreach (int id in withRule) slots.Single(s => s.Id == id).Match = new MatchRule("a.exe");
        return slots;
    }

    private static readonly HashSet<int> None = [];

    [Fact]
    public void Same_count_is_no_change() =>
        Assert.Equal(SlotCountAction.None, SlotCountPlanner.Plan(Slots(8), None, 8).Action);

    [Fact]
    public void Shrink_removes_unbound_slots_from_the_end()
    {
        var plan = SlotCountPlanner.Plan(Slots(8, 1, 2, 8), None, 6);
        Assert.Equal(SlotCountAction.Remove, plan.Action);
        Assert.Equal([7, 6], plan.RemoveIds);
    }

    [Fact]
    public void Slot_with_live_window_counts_as_bound()
    {
        var plan = SlotCountPlanner.Plan(Slots(4), new HashSet<int> { 3, 4 }, 3);
        Assert.Equal([2], plan.RemoveIds);
    }

    [Fact]
    public void Shrink_is_blocked_when_too_many_are_bound()
    {
        var plan = SlotCountPlanner.Plan(Slots(8, 1, 2, 3, 4, 5, 6, 7), None, 5);
        Assert.Equal(SlotCountAction.Blocked, plan.Action);
        Assert.Equal(2, plan.MissingUnbinds); // 3 to remove, only slot 8 is free
    }

    [Fact]
    public void Grow_uses_free_space_and_keeps_existing_slots()
    {
        var slots = Slots(2);
        slots[0].Rect = new RelRect(0.01, 0.01, 0.98, 0.1);
        slots[1].Rect = new RelRect(0.01, 0.12, 0.98, 0.1);
        var before = slots.Select(s => s.Rect).ToList();

        bool rearranged = SlotCountPlanner.AddSlots(slots, 3, ZoneType.Dock, DockEdge.Left);

        Assert.False(rearranged);
        Assert.Equal(3, slots.Count);
        Assert.Equal(before, slots.Take(2).Select(s => s.Rect).ToList());
        Assert.True(slots[2].Rect.Y >= 0.22); // below the existing slots
        Assert.Equal(3, slots[2].Id);
    }

    [Fact]
    public void Grow_without_free_space_rearranges_all_and_keeps_rules()
    {
        var slots = Slots(8, 2);
        bool rearranged = SlotCountPlanner.AddSlots(slots, 9, ZoneType.Dock, DockEdge.Left);

        Assert.True(rearranged);
        Assert.Equal(9, slots.Count);
        Assert.Equal(9, slots.Select(s => s.Id).Distinct().Count());
        Assert.NotNull(slots.Single(s => s.Id == 2).Match);
    }

    [Fact]
    public void New_slots_never_overlap()
    {
        var slots = new List<SlotModel>();
        SlotCountPlanner.AddSlots(slots, 8, ZoneType.Dock, DockEdge.Left);
        for (int i = 0; i < slots.Count; i++)
            for (int j = i + 1; j < slots.Count; j++)
            {
                RelRect a = slots[i].Rect, b = slots[j].Rect;
                bool overlap = a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y;
                Assert.False(overlap, $"slots {i + 1} and {j + 1} overlap");
            }
    }

    [Fact]
    public void Defaults_are_eight_slots_and_200px()
    {
        var layout = DefaultLayouts.Create();
        Assert.Equal(8, layout.Slots.Count);
        Assert.Equal(200, layout.ThumbZone.SizePx);
    }
}

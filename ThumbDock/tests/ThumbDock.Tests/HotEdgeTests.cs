using ThumbDock.Core;

namespace ThumbDock.Tests;

public class HotEdgeTests
{
    private static readonly Rectangle Mon = new(0, 0, 1920, 1080);
    private static readonly Point Middle = new(0, 540);

    [Theory]
    [InlineData(0, 540, true)]
    [InlineData(1, 360, true)]        // top of the middle third
    [InlineData(0, 359, false)]       // above it
    [InlineData(0, 720, false)]       // below it
    [InlineData(2, 540, false)]       // too far from the edge
    public void Zone_IsLeftEdgeMiddleThird(int x, int y, bool expected) =>
        Assert.Equal(expected, HotEdgePlanner.InZone(new Point(x, y), Mon));

    [Fact]
    public void Zone_FollowsMonitorOrigin() =>
        Assert.True(HotEdgePlanner.InZone(new Point(1920, 540), new Rectangle(1920, 0, 1920, 1080)));

    [Fact]
    public void Fires_AfterDwell_AndOnlyOnce()
    {
        var (s, t) = HotEdgePlanner.Step(HotEdgeState.Idle, Middle, Mon, false, 1000);
        Assert.False(t);
        (s, t) = HotEdgePlanner.Step(s, Middle, Mon, false, 1000 + HotEdgePlanner.DwellMs - 1);
        Assert.False(t);
        (s, t) = HotEdgePlanner.Step(s, Middle, Mon, false, 1000 + HotEdgePlanner.DwellMs);
        Assert.True(t);
        (s, t) = HotEdgePlanner.Step(s, Middle, Mon, false, 9000);
        Assert.False(t);                                   // must leave the zone first
    }

    [Fact]
    public void LeavingAndReturning_ArmsAgain()
    {
        var (s, _) = HotEdgePlanner.Step(HotEdgeState.Idle, Middle, Mon, false, 0);
        (s, _) = HotEdgePlanner.Step(s, Middle, Mon, false, 500);
        (s, _) = HotEdgePlanner.Step(s, new Point(500, 500), Mon, false, 600);
        Assert.Equal(HotEdgeState.Idle, s);
        (s, _) = HotEdgePlanner.Step(s, Middle, Mon, false, 700);
        var (_, t) = HotEdgePlanner.Step(s, Middle, Mon, false, 700 + HotEdgePlanner.DwellMs);
        Assert.True(t);
    }

    [Fact]
    public void ButtonDown_ResetsDwell()
    {
        var (s, _) = HotEdgePlanner.Step(HotEdgeState.Idle, Middle, Mon, false, 0);
        (s, var t) = HotEdgePlanner.Step(s, Middle, Mon, true, 1000);   // dragging a window to the edge
        Assert.False(t);
        Assert.Equal(HotEdgeState.Idle, s);
    }
}

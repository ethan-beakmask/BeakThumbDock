using System.Drawing;
using ThumbDock.Layout;
using Xunit;

namespace ThumbDock.Tests;

public class SlotSnapTests
{
    private static readonly Size Zone = new(300, 1040);

    // Two columns of the n×m arrangement: gap 4, cells 144 wide and 169 high.
    private static readonly Rectangle Left = new(4, 522, 144, 169);

    [Fact]
    public void Move_SticksToTheNeighboursTopAndToTheColumnBesideIt()
    {
        // Dropped 2 px too low and 3 px too far right of the free cell beside "Left".
        var dragged = new Rectangle(155, 524, 144, 169);
        var snapped = SlotSnap.Apply(dragged, DragKind.Move, [Left], Zone, gap: 4, distance: 8, minSize: 24);
        Assert.Equal(new Rectangle(152, 522, 144, 169), snapped);
    }

    [Fact]
    public void Move_FarFromEverything_FollowsTheCursorExactly()
    {
        var dragged = new Rectangle(77, 301, 100, 100);
        Assert.Equal(dragged, SlotSnap.Apply(dragged, DragKind.Move, [Left], Zone, 4, 8, 24));
    }

    [Fact]
    public void Move_SticksToTheZoneBorderOneGapIn()
    {
        var snapped = SlotSnap.Apply(new Rectangle(7, 9, 100, 100), DragKind.Move, [], Zone, 4, 8, 24);
        Assert.Equal(new Point(4, 4), snapped.Location);
    }

    [Fact]
    public void Move_PicksTheNearestCandidate()
    {
        var a = new Rectangle(0, 100, 50, 50);
        var b = new Rectangle(0, 105, 50, 50);
        var snapped = SlotSnap.Apply(new Rectangle(200, 104, 50, 50), DragKind.Move, [a, b], Zone, 0, 8, 24);
        Assert.Equal(105, snapped.Y);
    }

    [Fact]
    public void Resize_SticksToTheSameSizeAndKeepsTheTopLeftCorner()
    {
        var dragged = new Rectangle(152, 522, 140, 172); // corner drag: 4 px too narrow, 3 px too tall
        var snapped = SlotSnap.Apply(dragged, DragKind.ResizeCorner, [Left], Zone, 4, 8, 24);
        Assert.Equal(new Rectangle(152, 522, 144, 169), snapped);
    }

    [Fact]
    public void Resize_NeverGoesBelowTheMinimumSize()
    {
        var other = new Rectangle(0, 0, 10, 10); // "same width" candidate would make the slot 10 px wide
        var snapped = SlotSnap.Apply(new Rectangle(100, 100, 26, 26), DragKind.ResizeCorner, [other], Zone, 0, 20, minSize: 24);
        Assert.True(snapped.Width >= 24 && snapped.Height >= 24);
    }

    [Fact]
    public void ZeroDistanceTurnsStickingOff()
    {
        var dragged = new Rectangle(153, 523, 144, 169);
        Assert.Equal(dragged, SlotSnap.Apply(dragged, DragKind.Move, [Left], Zone, 4, 0, 24));
    }

    [Fact]
    public void Adopt_TakesTheExactRelativeValuesOfSlotsItLinesUpWith()
    {
        // The case from the field: slot 7 at y 0.5019, slot 8 dragged to the same pixel row.
        var slot7 = (new RelRect(0.0133, 0.5019, 0.48, 0.1621), new Rectangle(4, 522, 144, 169));
        var px = new Rectangle(152, 522, 144, 169);
        var rel = SlotSnap.Adopt(SlotGeometry.ToRelative(px, Zone), px, [slot7], size: false);
        Assert.Equal(0.5019, rel.Y);
        Assert.Equal(SlotGeometry.ToRelative(px, Zone).X, rel.X); // not lined up horizontally: untouched
        Assert.Equal(SlotGeometry.ToRelative(px, Zone).W, rel.W); // size only when resizing

        var resized = SlotSnap.Adopt(SlotGeometry.ToRelative(px, Zone), px, [slot7], size: true);
        Assert.Equal(0.48, resized.W);
        Assert.Equal(0.1621, resized.H);
    }
}

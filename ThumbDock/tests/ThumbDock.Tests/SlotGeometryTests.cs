using ThumbDock.Layout;

namespace ThumbDock.Tests;

public class SlotGeometryTests
{
    [Theory]
    [InlineData(1920, 1080)] // wide source in a tall-ish box: full width, centered vertically
    [InlineData(600, 1200)]  // tall source: full height, centered horizontally
    [InlineData(200, 100)]   // same aspect as the box: fills it
    public void Letterbox_KeepsAspectRatio_StaysInside_AndIsCentered(int sw, int sh)
    {
        var area = new Rectangle(10, 20, 200, 100);
        var box = SlotGeometry.Letterbox(area, new Size(sw, sh));

        Assert.True(area.Contains(box));
        Assert.True(box.Width == area.Width || box.Height == area.Height);
        Assert.Equal((double)sw / sh, (double)box.Width / box.Height, 1);
        Assert.InRange(box.Left - area.Left - (area.Right - box.Right), -1, 1);
        Assert.InRange(box.Top - area.Top - (area.Bottom - box.Bottom), -1, 1);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void Letterbox_InvalidSource_ReturnsEmpty(int sw, int sh) =>
        Assert.Equal(Rectangle.Empty, SlotGeometry.Letterbox(new Rectangle(0, 0, 100, 100), new Size(sw, sh)));

    [Fact]
    public void Letterbox_EmptyArea_ReturnsEmpty() =>
        Assert.Equal(Rectangle.Empty, SlotGeometry.Letterbox(Rectangle.Empty, new Size(100, 100)));

    [Fact]
    public void SplitTitle_PutsTitleStripUnderThumbnail()
    {
        var (thumb, title) = SlotGeometry.SplitTitle(new Rectangle(0, 0, 200, 120), titleHeight: 20, showTitle: true, padding: 4);

        Assert.Equal(new Rectangle(4, 4, 192, 92), thumb);
        Assert.Equal(new Rectangle(4, 96, 192, 20), title);
    }

    [Fact]
    public void SplitTitle_WithoutTitle_ThumbnailUsesWholeSlot()
    {
        var (thumb, title) = SlotGeometry.SplitTitle(new Rectangle(0, 0, 200, 120), 20, showTitle: false, padding: 4);

        Assert.Equal(new Rectangle(4, 4, 192, 112), thumb);
        Assert.True(title.IsEmpty);
    }

    [Theory]
    [InlineData(280, 96, 280)]
    [InlineData(280, 120, 350)]
    [InlineData(280, 144, 420)]
    public void DipToPx_ScalesByDpi(int dip, int dpi, int expected) =>
        Assert.Equal(expected, SlotGeometry.DipToPx(dip, dpi));

    [Fact]
    public void ToPixels_And_ToRelative_RoundTrip()
    {
        var zone = new Size(320, 1000);
        var rel = new RelRect(0.05, 0.02, 0.9, 0.18);
        var px = SlotGeometry.ToPixels(rel, zone);

        Assert.Equal(new Rectangle(16, 20, 288, 180), px);
        Assert.Equal(rel, SlotGeometry.ToRelative(px, zone));
    }

    [Fact]
    public void ToPixels_ScalesWithZone_ForDpiOrResolutionChange()
    {
        var rel = new RelRect(0.5, 0.5, 0.25, 0.25);
        Assert.Equal(new Rectangle(160, 500, 80, 250), SlotGeometry.ToPixels(rel, new Size(320, 1000)));
        Assert.Equal(new Rectangle(240, 750, 120, 375), SlotGeometry.ToPixels(rel, new Size(480, 1500)));
    }

    [Theory]
    [InlineData(-0.5, 2.0, 0.3, 0.3, 0.0, 0.7)]   // pushed back inside
    [InlineData(0.9, 0.9, 0.5, 0.5, 0.5, 0.5)]    // too far right/bottom
    public void ClampRelative_KeepsInsideZone(double x, double y, double w, double h, double ex, double ey)
    {
        var r = SlotGeometry.ClampRelative(new RelRect(x, y, w, h));
        Assert.Equal(ex, r.X, 4);
        Assert.Equal(ey, r.Y, 4);
        Assert.True(r.X + r.W <= 1.0001 && r.Y + r.H <= 1.0001);
    }

    [Fact]
    public void ClampRelative_EnforcesMinimumSize_AndRepairsNaN()
    {
        var r = SlotGeometry.ClampRelative(new RelRect(double.NaN, 0.1, 0, double.PositiveInfinity));
        Assert.Equal(0, r.X);
        Assert.Equal(SlotGeometry.MinRelative, r.W, 4);
        Assert.True(r.H > 0 && r.H <= 1);
    }

    [Fact]
    public void ApplyDrag_Move_SnapsToGrid_AndStaysInside()
    {
        var start = new Rectangle(10, 10, 100, 50);
        var moved = SlotGeometry.ApplyDrag(start, new Point(13, 21), DragKind.Move, new Size(300, 300), grid: 8, minSize: 20);
        Assert.Equal(new Rectangle(24, 32, 100, 50), moved);

        var clamped = SlotGeometry.ApplyDrag(start, new Point(1000, -1000), DragKind.Move, new Size(300, 300), 8, 20);
        Assert.Equal(new Rectangle(200, 0, 100, 50), clamped);
    }

    [Fact]
    public void ApplyDrag_ResizeCorner_RespectsMinimumAndZone()
    {
        var start = new Rectangle(10, 10, 100, 50);
        var tiny = SlotGeometry.ApplyDrag(start, new Point(-500, -500), DragKind.ResizeCorner, new Size(300, 300), 8, 20);
        Assert.Equal(new Rectangle(10, 10, 20, 20), tiny);

        var huge = SlotGeometry.ApplyDrag(start, new Point(5000, 5000), DragKind.ResizeCorner, new Size(300, 300), 8, 20);
        Assert.Equal(Rectangle.FromLTRB(10, 10, 300, 300), huge);
    }

    [Fact]
    public void ApplyDrag_ResizeRight_ChangesOnlyWidth()
    {
        var r = SlotGeometry.ApplyDrag(new Rectangle(0, 0, 100, 50), new Point(30, 99), DragKind.ResizeRight, new Size(300, 300), 1, 20);
        Assert.Equal(new Rectangle(0, 0, 130, 50), r);
    }

    [Fact]
    public void CenterIn_ShrinksAndCenters()
    {
        var area = new Rectangle(1920, 0, 1920, 1040);
        Assert.Equal(new Rectangle(2240, 160, 1280, 720), SlotGeometry.CenterIn(area, new Size(1280, 720)));
        Assert.Equal(area, SlotGeometry.CenterIn(area, new Size(5000, 5000)));
    }
}

public class AppBarEdgeTests
{
    [Theory]
    [InlineData((int)ThumbDock.Layout.DockEdge.Left, 0u)]   // ABE_LEFT
    [InlineData((int)ThumbDock.Layout.DockEdge.Top, 1u)]    // ABE_TOP
    [InlineData((int)ThumbDock.Layout.DockEdge.Right, 2u)]  // ABE_RIGHT
    [InlineData((int)ThumbDock.Layout.DockEdge.Bottom, 3u)] // ABE_BOTTOM
    public void FromDockEdge_MapsToShellConstants(int edge, uint abe) =>
        Assert.Equal(abe, (uint)ThumbDock.Native.AppBarNative.FromDockEdge((ThumbDock.Layout.DockEdge)edge));
}

public class OperationAreaTests
{
    private static readonly Rectangle Work = new(0, 0, 1920, 1032);

    [Fact]
    public void ZoneAlreadyReserved_WorkAreaUnchanged() =>
        Assert.Equal(new Rectangle(280, 0, 1640, 1032), SlotGeometry.OperationArea(new Rectangle(280, 0, 1640, 1032), new Rectangle(0, 0, 280, 1032)));

    [Fact]
    public void ZoneOnOtherMonitor_WorkAreaUnchanged() =>
        Assert.Equal(Work, SlotGeometry.OperationArea(Work, new Rectangle(1920, 0, 280, 1080)));

    [Fact]
    public void NoZone_WorkAreaUnchanged() =>
        Assert.Equal(Work, SlotGeometry.OperationArea(Work, Rectangle.Empty));

    [Theory]
    [InlineData(0, 0, 280, 1032, 280, 0, 1640, 1032)]       // left
    [InlineData(1640, 0, 280, 1032, 0, 0, 1640, 1032)]      // right
    [InlineData(0, 0, 1920, 280, 0, 280, 1920, 752)]        // top
    [InlineData(0, 752, 1920, 280, 0, 0, 1920, 752)]        // bottom
    public void ZoneNotReserved_IsSubtracted(int zx, int zy, int zw, int zh, int x, int y, int w, int h) =>
        Assert.Equal(new Rectangle(x, y, w, h), SlotGeometry.OperationArea(Work, new Rectangle(zx, zy, zw, zh)));

    [Fact]
    public void ZoneCoveringEverything_FallsBackToWorkArea() =>
        Assert.Equal(Work, SlotGeometry.OperationArea(Work, new Rectangle(-10, -10, 2000, 1100)));

    [Fact]
    public void CompensateFrame_GrowsByInvisibleBorders() =>
        Assert.Equal(Rectangle.FromLTRB(273, 0, 1927, 1039),
            SlotGeometry.CompensateFrame(new Rectangle(280, 0, 1640, 1032), new Padding(7, 0, 7, 7)));
}

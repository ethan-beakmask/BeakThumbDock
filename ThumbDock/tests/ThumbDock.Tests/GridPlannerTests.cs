using ThumbDock.Layout;

namespace ThumbDock.Tests;

public class GridPlannerTests
{
    private static readonly Size Zone = new(1920, 1080);

    private static List<Rectangle> Px(GridSpec spec, Size? zone = null, int dpi = 96) =>
        GridPlanner.Compute(spec, zone ?? Zone, dpi).Select(r => SlotGeometry.ToPixels(r, zone ?? Zone)).ToList();

    [Fact]
    public void EightByFour_AllEqualAlignedWithEqualGaps()
    {
        var cells = Px(new GridSpec { Cols = 8, Rows = 4, GapPx = 4 });
        Assert.Equal(32, cells.Count);
        Assert.InRange(cells.Select(c => c.Width).Distinct().Count(), 1, 2);   // rounding may differ by 1 px
        Assert.InRange(cells.Max(c => c.Width) - cells.Min(c => c.Width), 0, 1);
        Assert.InRange(cells.Max(c => c.Height) - cells.Min(c => c.Height), 0, 1);
        for (int r = 0; r < 4; r++) Assert.Equal(1, cells.Skip(r * 8).Take(8).Select(c => c.Y).Distinct().Count());
        for (int c = 0; c < 8; c++) Assert.Equal(1, Enumerable.Range(0, 4).Select(r => cells[r * 8 + c].X).Distinct().Count());
        Assert.InRange(cells[1].X - cells[0].Right, 3, 5);
        Assert.InRange(cells[8].Y - cells[0].Bottom, 3, 5);
    }

    [Fact]
    public void FillMode_UsesWholeZone()
    {
        var cells = Px(new GridSpec { Cols = 4, Rows = 2, GapPx = 10 });
        Assert.InRange(cells[0].X, 9, 11);
        Assert.InRange(Zone.Width - cells[3].Right, 9, 11);
        Assert.InRange(Zone.Height - cells[7].Bottom, 9, 11);
    }

    [Fact]
    public void Dpi_ScalesGap()
    {
        var cells = Px(new GridSpec { Cols = 2, Rows = 1, GapPx = 10 }, dpi: 192);
        Assert.InRange(cells[0].X, 19, 21);
    }

    [Fact]
    public void FixedSize_FitsAndIsCentered()
    {
        var cells = Px(new GridSpec { Cols = 2, Rows = 2, GapPx = 0, CellWidthPx = 400, CellHeightPx = 200 });
        Assert.All(cells, c => { Assert.Equal(400, c.Width); Assert.Equal(200, c.Height); });
        Assert.Equal((1920 - 800) / 2, cells[0].X);
        Assert.Equal((1080 - 400) / 2, cells[0].Y);
    }

    [Fact]
    public void FixedSize_TooBig_ShrinksKeepingRatio()
    {
        var cells = Px(new GridSpec { Cols = 4, Rows = 4, GapPx = 0, CellWidthPx = 1000, CellHeightPx = 500 });
        Assert.All(cells, c => Assert.InRange((double)c.Width / c.Height, 1.98, 2.02));
        Assert.All(cells, c => { Assert.True(c.Right <= Zone.Width); Assert.True(c.Bottom <= Zone.Height); });
    }

    [Fact]
    public void Arrange_KeepsIdsAndRules_AndRejectsCountMismatch()
    {
        var slots = DefaultLayouts.Create("t", 6).Slots;
        slots[2].Match = new MatchRule("a.exe");
        var spec = new GridSpec { Cols = 3, Rows = 2 };
        Assert.True(GridPlanner.Arrange(slots, spec, Zone, 96));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, slots.Select(s => s.Id));
        Assert.NotNull(slots[2].Match);
        Assert.True(slots[3].Rect.Y > slots[0].Rect.Y && slots[3].Rect.X == slots[0].Rect.X);
        Assert.False(GridPlanner.Arrange(slots, new GridSpec { Cols = 4, Rows = 2 }, Zone, 96));
    }

    [Fact]
    public void Normalize_ClampsTo4000AndMaxCells()
    {
        var g = new GridSpec { Cols = 50, Rows = 50, GapPx = 999, CellWidthPx = 9999, CellHeightPx = 0 };
        g.Normalize();
        Assert.True(g.Count <= GridSpec.MaxCells);
        Assert.Equal(GridSpec.MaxGapPx, g.GapPx);
        Assert.Equal(0, g.CellWidthPx); // half-set fixed size is dropped
    }

    [Fact]
    public void LayoutStore_NormalizeClampsZoneSizeTo4000()
    {
        var l = new LayoutModel { Name = "其他", ThumbZone = new ThumbZone { SizePx = 99999 } };
        Assert.Equal(4000, LayoutStore.Normalize(l).ThumbZone.SizePx);
    }

    [Theory]
    [InlineData((int)ZoneType.Dock, (int)DockEdge.Left, 8, 1, 8)]
    [InlineData((int)ZoneType.Dock, (int)DockEdge.Right, 8, 1, 8)]
    [InlineData((int)ZoneType.Dock, (int)DockEdge.Top, 8, 8, 1)]
    [InlineData((int)ZoneType.Fill, (int)DockEdge.Left, 8, 3, 3)]
    public void Guess_MatchesDefaultShapes(int type, int edge, int n, int cols, int rows)
    {
        var g = GridPlanner.Guess(n, (ZoneType)type, (DockEdge)edge);
        Assert.Equal((cols, rows), (g.Cols, g.Rows));
    }

    [Fact]
    public void DockStrip_TwoByEight_FitsNarrowZone()
    {
        var zone = new Size(400, 1040);
        var cells = Px(new GridSpec { Cols = 2, Rows = 8, GapPx = 4 }, zone);
        Assert.Equal(16, cells.Count);
        Assert.All(cells, c => { Assert.True(c.Right <= zone.Width); Assert.True(c.Bottom <= zone.Height); });
        Assert.Equal(2, cells.Select(c => c.X).Distinct().Count());
    }

    [Theory]
    [InlineData(200, 96, 1920, 200)]     // normal strip
    [InlineData(4000, 96, 1920, 1344)]   // 70 % of 1920
    [InlineData(200, 192, 1920, 400)]    // 200 % scaling
    public void DockThickness_IsLimitedToShareOfMonitor(int sizeDip, int dpi, int monitor, int expected) =>
        Assert.Equal(expected, SlotGeometry.DockThickness(sizeDip, dpi, monitor));

    [Fact]
    public void MaxDockDip_RoundTripsWithThickness() =>
        Assert.Equal(1344, SlotGeometry.MaxDockDip(1920, 96));
}

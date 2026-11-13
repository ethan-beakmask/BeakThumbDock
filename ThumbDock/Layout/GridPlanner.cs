namespace ThumbDock.Layout;

/// <summary>n×m grid settings of a fill zone. Zero cell size = fill the zone.</summary>
internal sealed class GridSpec
{
    public const int MaxPx = 4000, MaxCells = 100, MaxGapPx = 200;

    public int Cols { get; set; } = 4;
    public int Rows { get; set; } = 2;

    /// <summary>Space between slots and around the grid, in pixels at 100 % scaling.</summary>
    public int GapPx { get; set; } = 4;

    /// <summary>Fixed slot size in pixels at 100 % scaling; 0 = computed from the zone.</summary>
    public int CellWidthPx { get; set; }
    public int CellHeightPx { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int Count => Cols * Rows;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFixedSize => CellWidthPx > 0 && CellHeightPx > 0;

    public GridSpec Clone() => (GridSpec)MemberwiseClone();

    public bool SameAs(GridSpec? o) => o is not null && Cols == o.Cols && Rows == o.Rows && GapPx == o.GapPx &&
        CellWidthPx == o.CellWidthPx && CellHeightPx == o.CellHeightPx;

    /// <summary>Repairs hand-edited values.</summary>
    public void Normalize()
    {
        Cols = Math.Clamp(Cols, 1, MaxCells);
        Rows = Math.Clamp(Rows, 1, MaxCells);
        if (Cols * Rows > MaxCells) Rows = Math.Max(1, MaxCells / Cols);
        GapPx = Math.Clamp(GapPx, 0, MaxGapPx);
        CellWidthPx = Math.Clamp(CellWidthPx, 0, MaxPx);
        CellHeightPx = Math.Clamp(CellHeightPx, 0, MaxPx);
        if (CellWidthPx == 0 || CellHeightPx == 0) CellWidthPx = CellHeightPx = 0;
    }
}

/// <summary>Pure computation of an n×m grid: equal slots, aligned, equal gaps (row-major order).</summary>
internal static class GridPlanner
{
    /// <summary>
    /// Relative slot rectangles for <paramref name="spec"/> inside a zone of <paramref name="zone"/> pixels.
    /// Fixed-size cells that do not fit are shrunk uniformly; a grid smaller than the zone is centered.
    /// </summary>
    public static List<RelRect> Compute(GridSpec spec, Size zone, int dpi)
    {
        int cols = spec.Cols, rows = spec.Rows;
        double zw = Math.Max(1, zone.Width), zh = Math.Max(1, zone.Height);
        double gap = SlotGeometry.DipToPx(spec.GapPx, dpi);
        double cw, ch;
        if (spec.IsFixedSize)
        {
            cw = SlotGeometry.DipToPx(spec.CellWidthPx, dpi);
            ch = SlotGeometry.DipToPx(spec.CellHeightPx, dpi);
            double fit = Math.Min(1, Math.Min(
                (zw - (cols + 1) * gap) / (cols * cw), (zh - (rows + 1) * gap) / (rows * ch)));
            if (fit < 1) { fit = Math.Max(fit, 0.01); cw *= fit; ch *= fit; }
        }
        else
        {
            cw = (zw - (cols + 1) * gap) / cols;
            ch = (zh - (rows + 1) * gap) / rows;
        }
        cw = Math.Max(cw, 1); ch = Math.Max(ch, 1);
        double totalW = cols * cw + (cols + 1) * gap, totalH = rows * ch + (rows + 1) * gap;
        double x0 = Math.Max(0, (zw - totalW) / 2), y0 = Math.Max(0, (zh - totalH) / 2);
        var list = new List<RelRect>(cols * rows);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                list.Add(SlotGeometry.ClampRelative(new RelRect(
                    (x0 + gap + c * (cw + gap)) / zw, (y0 + gap + r * (ch + gap)) / zh, cw / zw, ch / zh)));
        return list;
    }

    /// <summary>The grid shape a layout without grid settings effectively has: a column or row for docks, a square-ish grid for fill.</summary>
    public static GridSpec Guess(int n, ZoneType type, DockEdge edge)
    {
        n = Math.Max(n, 1);
        if (type == ZoneType.Dock) return edge is DockEdge.Left or DockEdge.Right ? new GridSpec { Cols = 1, Rows = n } : new GridSpec { Cols = n, Rows = 1 };
        int cols = (int)Math.Ceiling(Math.Sqrt(n));
        return new GridSpec { Cols = cols, Rows = (int)Math.Ceiling((double)n / cols) };
    }

    /// <summary>Re-positions <paramref name="slots"/> in list order (ids, rules and bindings are kept). Returns false if the count does not match.</summary>
    public static bool Arrange(List<SlotModel> slots, GridSpec spec, Size zone, int dpi)
    {
        if (slots.Count != spec.Count) return false;
        var rects = Compute(spec, zone, dpi);
        for (int i = 0; i < slots.Count; i++) slots[i].Rect = rects[i];
        return true;
    }
}

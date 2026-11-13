namespace ThumbDock.Layout;

/// <summary>Factory for new layouts and for re-arranging slots when the zone shape changes.</summary>
internal static class DefaultLayouts
{
    private const double Gap = 0.01;

    public const int DefaultSlotCount = 8;

    /// <summary>The fixed layout used by safe mode: left strip, 1 column x 8 rows, 200 px. Its zone cannot be changed.</summary>
    public const string ReservedName = "default";

    // Case-insensitive: layout files are named after the layout and Windows file names ignore case.
    public static bool IsReserved(string? name) => string.Equals(name, ReservedName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Forces the reserved layout back to its fixed shape (single monitor, left strip, 200 px, 8 slots in a column).
    /// Slot order, ids, rules and bindings are kept, so swapping slots still works. Other layouts are untouched.
    /// </summary>
    public static void Enforce(LayoutModel layout)
    {
        if (!IsReserved(layout.Name)) return;
        layout.Mode = LayoutMode.Single;
        layout.FocusMonitor = layout.ThumbMonitor;
        layout.ThumbZone = new ThumbZone { Type = ZoneType.Dock, Edge = DockEdge.Left, SizePx = ThumbZone.DefaultSizePx };
        layout.Grid = null;
        if (layout.Slots.Count > DefaultSlotCount) layout.Slots.RemoveRange(DefaultSlotCount, layout.Slots.Count - DefaultSlotCount);
        int nextId = layout.Slots.Count == 0 ? 1 : layout.Slots.Max(s => s.Id) + 1;
        while (layout.Slots.Count < DefaultSlotCount) layout.Slots.Add(new SlotModel { Id = nextId++ });
        Arrange(layout.Slots, ZoneType.Dock, DockEdge.Left);
    }

    public static LayoutModel Create(string name = "default", int slotCount = DefaultSlotCount) => new()
    {
        Name = name,
        Slots = Arrange(Enumerable.Range(1, slotCount).Select(id => new SlotModel { Id = id }).ToList(),
                        ZoneType.Dock, DockEdge.Left),
    };

    /// <summary>
    /// Re-positions the given slots for a zone shape: a column for left/right docks,
    /// a row for top/bottom docks, a grid for fill zones. Rules and options are kept.
    /// </summary>
    public static List<SlotModel> Arrange(List<SlotModel> slots, ZoneType type, DockEdge edge)
    {
        int n = slots.Count;
        if (n == 0) return slots;
        (int cols, int rows) = Shape(n, type, edge);
        (double w, double h) = CellSize(n, type, edge);
        for (int i = 0; i < n; i++)
        {
            int c = i % cols, r = i / cols;
            slots[i].Rect = SlotGeometry.ClampRelative(new RelRect(Gap + c * (w + Gap), Gap + r * (h + Gap), w, h));
        }
        return slots;
    }

    /// <summary>Relative size of one slot when <paramref name="n"/> slots are arranged by <see cref="Arrange"/>.</summary>
    public static (double W, double H) CellSize(int n, ZoneType type, DockEdge edge)
    {
        (int cols, int rows) = Shape(Math.Max(n, 1), type, edge);
        return ((1 - Gap * (cols + 1)) / cols, (1 - Gap * (rows + 1)) / rows);
    }

    /// <summary>Distance between slots (relative) used by the default arrangement.</summary>
    public static double SlotGap => Gap;

    private static (int Cols, int Rows) Shape(int n, ZoneType type, DockEdge edge) =>
        type == ZoneType.Fill ? GridShape(n)
            : edge is DockEdge.Left or DockEdge.Right ? (1, n) : (n, 1);

    /// <summary>Shape class of a zone; slots are re-arranged when it changes.</summary>
    public static string ShapeOf(ZoneType type, DockEdge edge) =>
        type == ZoneType.Fill ? "grid" : edge is DockEdge.Left or DockEdge.Right ? "column" : "row";

    private static (int Cols, int Rows) GridShape(int n)
    {
        int cols = (int)Math.Ceiling(Math.Sqrt(n));
        return (cols, (int)Math.Ceiling((double)n / cols));
    }
}

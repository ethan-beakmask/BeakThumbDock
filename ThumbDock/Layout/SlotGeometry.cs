namespace ThumbDock.Layout;

internal enum DragKind { Move, ResizeRight, ResizeBottom, ResizeCorner }

/// <summary>Pure geometry for slots and thumbnails. Pixel values unless the name says relative.</summary>
internal static class SlotGeometry
{
    /// <summary>Smallest slot edge, as a fraction of the zone.</summary>
    public const double MinRelative = 0.02;

    /// <summary>Relative (0–1) rectangle → pixels inside a zone of <paramref name="zone"/> size.</summary>
    public static Rectangle ToPixels(RelRect r, Size zone)
    {
        int left = (int)Math.Round(r.X * zone.Width), top = (int)Math.Round(r.Y * zone.Height);
        int right = (int)Math.Round((r.X + r.W) * zone.Width), bottom = (int)Math.Round((r.Y + r.H) * zone.Height);
        return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    /// <summary>Pixels → relative rectangle; inverse of <see cref="ToPixels"/>.</summary>
    public static RelRect ToRelative(Rectangle px, Size zone)
    {
        if (zone.Width <= 0 || zone.Height <= 0) return new RelRect(0, 0, 1, 1);
        return ClampRelative(new RelRect(
            (double)px.X / zone.Width, (double)px.Y / zone.Height,
            (double)px.Width / zone.Width, (double)px.Height / zone.Height));
    }

    /// <summary>Keeps a relative rectangle inside the zone with a minimum size; NaN becomes a sane default.</summary>
    public static RelRect ClampRelative(RelRect r)
    {
        double w = Math.Clamp(Sane(r.W, 0.25), MinRelative, 1);
        double h = Math.Clamp(Sane(r.H, 0.25), MinRelative, 1);
        double x = Math.Clamp(Sane(r.X, 0), 0, 1 - w);
        double y = Math.Clamp(Sane(r.Y, 0), 0, 1 - h);
        return new RelRect(Math.Round(x, 4), Math.Round(y, 4), Math.Round(w, 4), Math.Round(h, 4));
    }

    /// <summary>
    /// New slot bounds while dragging (FR-4): moves or resizes <paramref name="start"/> by <paramref name="delta"/>,
    /// snaps edges to <paramref name="grid"/>, keeps at least <paramref name="minSize"/> and stays inside <paramref name="zone"/>.
    /// </summary>
    public static Rectangle ApplyDrag(Rectangle start, Point delta, DragKind kind, Size zone, int grid, int minSize)
    {
        if (kind == DragKind.Move)
        {
            int x = Math.Clamp(Snap(start.X + delta.X, grid), 0, Math.Max(0, zone.Width - start.Width));
            int y = Math.Clamp(Snap(start.Y + delta.Y, grid), 0, Math.Max(0, zone.Height - start.Height));
            return new Rectangle(x, y, start.Width, start.Height);
        }
        int right = start.Right, bottom = start.Bottom;
        if (kind is DragKind.ResizeRight or DragKind.ResizeCorner)
            right = Math.Clamp(Snap(start.Right + delta.X, grid), start.Left + minSize, zone.Width);
        if (kind is DragKind.ResizeBottom or DragKind.ResizeCorner)
            bottom = Math.Clamp(Snap(start.Bottom + delta.Y, grid), start.Top + minSize, zone.Height);
        return Rectangle.FromLTRB(start.Left, start.Top, Math.Max(right, start.Left + 1), Math.Max(bottom, start.Top + 1));
    }

    public static int Snap(int value, int grid) => grid <= 1 ? value : (int)Math.Round((double)value / grid) * grid;

    /// <summary>
    /// Splits a slot into the thumbnail area and the title strip at the bottom.
    /// Without a title the thumbnail uses the whole slot (minus padding).
    /// </summary>
    public static (Rectangle Thumb, Rectangle Title) SplitTitle(Rectangle slot, int titleHeight, bool showTitle, int padding)
    {
        var inner = Rectangle.Inflate(slot, -padding, -padding);
        if (!showTitle || inner.Height <= titleHeight) return (Clamp(inner), Rectangle.Empty);
        var thumb = new Rectangle(inner.X, inner.Y, inner.Width, inner.Height - titleHeight);
        var title = new Rectangle(inner.X, thumb.Bottom, inner.Width, titleHeight);
        return (thumb, title);
    }

    /// <summary>Largest rectangle with the source's aspect ratio, centered in <paramref name="area"/>.</summary>
    public static Rectangle Letterbox(Rectangle area, Size source)
    {
        if (area.Width <= 0 || area.Height <= 0 || source.Width <= 0 || source.Height <= 0) return Rectangle.Empty;
        double scale = Math.Min((double)area.Width / source.Width, (double)area.Height / source.Height);
        int w = Math.Max(1, (int)Math.Round(source.Width * scale));
        int h = Math.Max(1, (int)Math.Round(source.Height * scale));
        return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    /// <summary>A window of <paramref name="size"/> centered in <paramref name="area"/>, shrunk to fit if needed.</summary>
    public static Rectangle CenterIn(Rectangle area, Size size)
    {
        int w = Math.Min(Math.Max(1, size.Width), area.Width), h = Math.Min(Math.Max(1, size.Height), area.Height);
        return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    /// <summary>
    /// Operation area of a monitor ("fit" target): its work area minus the thumbnail zone, if the zone overlaps it.
    /// Normally the AppBar already removed the zone from the work area; this also covers machines where the
    /// shell did not reserve it. Of the four pieces left around the zone, the largest wins.
    /// </summary>
    public static Rectangle OperationArea(Rectangle workArea, Rectangle zone)
    {
        var overlap = Rectangle.Intersect(workArea, zone);
        if (overlap.Width <= 0 || overlap.Height <= 0) return workArea;
        Rectangle[] pieces =
        [
            Rectangle.FromLTRB(workArea.Left, workArea.Top, overlap.Left, workArea.Bottom),
            Rectangle.FromLTRB(overlap.Right, workArea.Top, workArea.Right, workArea.Bottom),
            Rectangle.FromLTRB(workArea.Left, workArea.Top, workArea.Right, overlap.Top),
            Rectangle.FromLTRB(workArea.Left, overlap.Bottom, workArea.Right, workArea.Bottom),
        ];
        var best = pieces.MaxBy(r => (long)Math.Max(0, r.Width) * Math.Max(0, r.Height));
        return best.Width > 0 && best.Height > 0 ? best : workArea;
    }

    /// <summary>
    /// Window rectangle whose visible frame covers <paramref name="target"/>, given the invisible resize borders
    /// (window rectangle minus DWM extended frame bounds) on each side.
    /// </summary>
    public static Rectangle CompensateFrame(Rectangle target, Padding invisibleBorders) =>
        Rectangle.FromLTRB(target.Left - invisibleBorders.Left, target.Top - invisibleBorders.Top,
            target.Right + invisibleBorders.Right, target.Bottom + invisibleBorders.Bottom);

    /// <summary>Converts device-independent pixels to physical pixels.</summary>
    /// <summary>Largest share of a monitor a docked strip may take, so menus and the picker window stay reachable.</summary>
    public const double MaxDockShare = 0.7;

    /// <summary>Strip thickness in pixels: the configured size, limited to <see cref="MaxDockShare"/> of the monitor dimension.</summary>
    public static int DockThickness(int sizeDip, int dpi, int monitorDimensionPx) =>
        Math.Max(1, Math.Min(DipToPx(sizeDip, dpi), (int)(monitorDimensionPx * MaxDockShare)));

    /// <summary>The same limit expressed in 100 %-scale pixels, for the settings dialog.</summary>
    public static int MaxDockDip(int monitorDimensionPx, int dpi) => (int)(monitorDimensionPx * MaxDockShare * 96.0 / Math.Max(1, dpi));

    public static int DipToPx(int dip, int dpi) => (int)Math.Round(dip * dpi / 96.0);

    private static double Sane(double v, double fallback) => double.IsFinite(v) ? v : fallback;

    private static Rectangle Clamp(Rectangle r) =>
        new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));
}

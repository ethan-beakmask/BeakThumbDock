namespace ThumbDock.Layout;

/// <summary>
/// Edit-mode alignment: while a slot is dragged, its edges stick to the other slots and to the zone border
/// (flush, or one grid gap away), so hand-placed slots can line up with the ones the n×m arrangement placed.
/// Away from any of those the slot follows the cursor pixel by pixel. Pure: pixels in, pixels out.
/// </summary>
internal static class SlotSnap
{
    /// <summary>
    /// Pulls <paramref name="r"/> (the slot as dragged so far) onto the nearest alignment within
    /// <paramref name="distance"/> pixels; each axis snaps on its own. <paramref name="gap"/> is the spacing
    /// between neighbouring slots.
    /// </summary>
    public static Rectangle Apply(Rectangle r, DragKind kind, IReadOnlyList<Rectangle> others, Size zone, int gap, int distance, int minSize)
    {
        if (distance <= 0) return r;
        var columns = others.Select(o => (o.Left, o.Right)).ToList();
        var rows = others.Select(o => (o.Top, o.Bottom)).ToList();
        if (kind == DragKind.Move)
        {
            int x = Nearest(r.X, distance, 0, Math.Max(0, zone.Width - r.Width), MoveCandidates(columns, r.Width, zone.Width, gap));
            int y = Nearest(r.Y, distance, 0, Math.Max(0, zone.Height - r.Height), MoveCandidates(rows, r.Height, zone.Height, gap));
            return new Rectangle(x, y, r.Width, r.Height);
        }
        int right = r.Right, bottom = r.Bottom;
        if (kind is DragKind.ResizeRight or DragKind.ResizeCorner)
            right = Nearest(r.Right, distance, r.Left + minSize, zone.Width, EdgeCandidates(columns, r.Left, zone.Width, gap));
        if (kind is DragKind.ResizeBottom or DragKind.ResizeCorner)
            bottom = Nearest(r.Bottom, distance, r.Top + minSize, zone.Height, EdgeCandidates(rows, r.Top, zone.Height, gap));
        return Rectangle.FromLTRB(r.Left, r.Top, Math.Max(right, r.Left + 1), Math.Max(bottom, r.Top + 1));
    }

    /// <summary>
    /// Takes over the exact relative values of slots the dragged slot lines up with in pixels, so they stay lined up
    /// at every zone size (two relative values that round to the same pixel here may differ by one elsewhere).
    /// </summary>
    public static RelRect Adopt(RelRect rel, Rectangle px, IEnumerable<(RelRect Rel, Rectangle Px)> others, bool size)
    {
        foreach (var (otherRel, otherPx) in others)
        {
            if (otherPx.X == px.X) rel = rel with { X = otherRel.X };
            if (otherPx.Y == px.Y) rel = rel with { Y = otherRel.Y };
            if (size && otherPx.Width == px.Width) rel = rel with { W = otherRel.W };
            if (size && otherPx.Height == px.Height) rel = rel with { H = otherRel.H };
        }
        return rel;
    }

    // Positions for the near (left/top) edge of a slot of the given size.
    private static IEnumerable<int> MoveCandidates(List<(int Near, int Far)> others, int size, int zone, int gap)
    {
        foreach (var (near, far) in others)
        {
            yield return near;              // same left/top
            yield return far - size;        // same right/bottom
            yield return far + gap;         // next to it, after
            yield return near - gap - size; // next to it, before
        }
        // Zone border, one gap in (flush when there is no gap). The flush position is still reachable: it is the clamp limit.
        yield return gap;
        yield return zone - gap - size;
    }

    // Positions for the far (right/bottom) edge of a slot whose near edge is at start.
    private static IEnumerable<int> EdgeCandidates(List<(int Near, int Far)> others, int start, int zone, int gap)
    {
        foreach (var (near, far) in others)
        {
            yield return far;                  // same right/bottom
            yield return near - gap;           // stop one gap before it
            yield return start + (far - near); // same width/height
        }
        yield return zone - gap;
    }

    private static int Nearest(int value, int distance, int min, int max, IEnumerable<int> candidates)
    {
        int best = value, bestDistance = distance + 1;
        foreach (int c in candidates)
        {
            if (c < min || c > max) continue;
            int d = Math.Abs(c - value);
            if (d < bestDistance) (best, bestDistance) = (c, d);
        }
        return best;
    }
}

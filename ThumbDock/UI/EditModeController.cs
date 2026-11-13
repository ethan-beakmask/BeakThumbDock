using ThumbDock.Layout;

namespace ThumbDock.UI;

/// <summary>
/// FR-4 edit mode: drag slots to move them, drag the right/bottom edge or the corner grip to resize,
/// add and delete slots (Del removes the slot clicked last). Edges stick to the other slots (SlotSnap);
/// positions are stored as relative rectangles.
/// Outside edit mode slots are locked and clicks only activate windows.
/// </summary>
internal sealed class EditModeController
{
    private const double NewSlotWidth = 0.4, NewSlotHeight = 0.15;
    private DockForm? _dock;
    private SlotControl? _dragging;
    private SlotControl? _selected;
    private DragKind _kind;
    private Point _startCursor;
    private Rectangle _startBounds;

    public bool IsEditing => _dock is not null;

    /// <summary>Raised after a slot was moved, resized, added or removed.</summary>
    public event Action? Changed;

    public void Begin(DockForm dock)
    {
        End();
        _dock = dock;
        foreach (var s in dock.Slots) Attach(s);
        dock.DeletePressed += RemoveSelected;
        dock.SetEditing(true);
    }

    public void End()
    {
        if (_dock is null) return;
        foreach (var s in _dock.Slots) Detach(s);
        _dock.DeletePressed -= RemoveSelected;
        if (!_dock.IsDisposed) _dock.SetEditing(false);
        _dock = null;
        _dragging = null;
        _selected = null;
    }

    private void Select(SlotControl? slot)
    {
        if (_selected == slot) return;
        if (_selected is { IsDisposed: false }) _selected.Selected = false;
        _selected = slot;
        if (slot is not null) slot.Selected = true;
    }

    // Del: removes the slot that was clicked last.
    private void RemoveSelected()
    {
        if (_selected is { IsDisposed: false } slot && _dragging is null) Remove(slot);
    }

    /// <summary>Adds a slot centred on <paramref name="clientPoint"/> (dock client coordinates).</summary>
    public void AddSlotAt(Point clientPoint)
    {
        if (_dock is null) return;
        var zone = _dock.ClientSize;
        double x = (double)clientPoint.X / Math.Max(1, zone.Width) - NewSlotWidth / 2;
        double y = (double)clientPoint.Y / Math.Max(1, zone.Height) - NewSlotHeight / 2;
        var model = new SlotModel
        {
            Id = _dock.Slots.Select(s => s.Model.Id).DefaultIfEmpty(0).Max() + 1,
            Rect = SlotGeometry.ClampRelative(new RelRect(x, y, NewSlotWidth, NewSlotHeight)),
        };
        Attach(_dock.AddSlot(model));
        Changed?.Invoke();
    }

    public void Remove(SlotControl slot)
    {
        if (_dock is null) return;
        if (_selected == slot) _selected = null;
        Detach(slot);
        _dock.RemoveSlot(slot);
        Changed?.Invoke();
    }

    private void Attach(SlotControl s)
    {
        s.MouseDown += OnMouseDown;
        s.MouseMove += OnMouseMove;
        s.MouseUp += OnMouseUp;
    }

    private void Detach(SlotControl s)
    {
        s.MouseDown -= OnMouseDown;
        s.MouseMove -= OnMouseMove;
        s.MouseUp -= OnMouseUp;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not SlotControl slot) return;
        _dragging = slot;
        _kind = HitTest(slot, e.Location);
        _startCursor = Cursor.Position;
        _startBounds = slot.Bounds;
        slot.BringToFront();
        Select(slot);
        // The zone ignores activation by mouse (MA_NOACTIVATE); take the keyboard explicitly so Del arrives.
        if (_dock is { IsDisposed: false } dock && Form.ActiveForm != dock) dock.Activate();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not SlotControl slot || _dock is null) return;
        if (_dragging != slot)
        {
            slot.Cursor = CursorFor(HitTest(slot, e.Location));
            return;
        }
        var delta = new Point(Cursor.Position.X - _startCursor.X, Cursor.Position.Y - _startCursor.Y);
        int minSize = Math.Max(slot.GripSize * 3, 24);
        // Pixel-precise; edges stick to the other slots and the zone border. Holding Alt turns the sticking off.
        var bounds = SlotGeometry.ApplyDrag(_startBounds, delta, _kind, _dock.ClientSize, grid: 1, minSize);
        if (!Control.ModifierKeys.HasFlag(Keys.Alt))
        {
            var others = _dock.Slots.Where(s => s != slot).Select(s => s.Bounds).ToList();
            bounds = SlotSnap.Apply(bounds, _kind, others, _dock.ClientSize, _dock.SlotGapPx, _dock.SnapDistancePx, minSize);
        }
        slot.Bounds = bounds;
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (_dragging is null || _dock is null) return;
        bool moved = _dragging.Bounds != _startBounds;
        _dock.CommitSlotBounds(_dragging, resized: _dragging.Size != _startBounds.Size);
        _dragging = null;
        if (moved) Changed?.Invoke();
    }

    private static DragKind HitTest(SlotControl slot, Point p)
    {
        int grip = slot.GripSize, edge = Math.Max(4, grip / 2);
        var size = slot.ClientSize;
        bool nearRight = p.X >= size.Width - edge, nearBottom = p.Y >= size.Height - edge;
        bool inGrip = p.X >= size.Width - grip && p.Y >= size.Height - grip;
        if (inGrip || (nearRight && nearBottom)) return DragKind.ResizeCorner;
        if (nearRight) return DragKind.ResizeRight;
        return nearBottom ? DragKind.ResizeBottom : DragKind.Move;
    }

    private static Cursor CursorFor(DragKind kind) => kind switch
    {
        DragKind.ResizeCorner => Cursors.SizeNWSE,
        DragKind.ResizeRight => Cursors.SizeWE,
        DragKind.ResizeBottom => Cursors.SizeNS,
        _ => Cursors.SizeAll,
    };
}

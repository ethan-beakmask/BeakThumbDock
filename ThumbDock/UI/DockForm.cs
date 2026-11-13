using ThumbDock.Core;
using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.UI;

/// <summary>
/// The thumbnail zone (section 3): an AppBar strip on one edge of the thumbnail monitor ("dock"),
/// or the whole thumbnail monitor ("fill"). Slots are placed from their relative rectangles.
/// It never takes activation (WS_EX_NOACTIVATE + MA_NOACTIVATE, spike S-7).
/// </summary>
internal sealed class DockForm : Form
{
    private const int WM_MOUSEACTIVATE = 0x0021, WM_ACTIVATE = 0x0006, WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0, MA_NOACTIVATE = 3;
    private const int GridDip = 8, SnapDip = 8;
    private static readonly Color GridColor = Color.FromArgb(52, 52, 52);

    private readonly LayoutModel _layout;
    private readonly List<SlotControl> _slots = [];
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 500 };
    private ContextMenuStrip? _slotMenu;
    private uint _callbackMessage;
    private nint _appBarHwnd; // kept outside Handle so emergency cleanup works from any thread
    private bool _editing;

    public DockForm(LayoutModel layout, ZoneResolution zone)
    {
        _layout = layout;
        Zone = zone;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(24, 24, 24);
        DoubleBuffered = true;
        Text = "ThumbDock";
        foreach (var model in layout.Slots) AddSlotControl(model);
        _refreshTimer.Tick += (_, _) => RefreshSlots();
    }

    public ZoneResolution Zone { get; }

    /// <summary>
    /// Single-monitor fill: the zone covers the only monitor. It is not topmost, so a source window brought forward
    /// is shown above it, and <see cref="RaiseWall"/> puts it back on top.
    /// </summary>
    // Null-safe on purpose: the Form base constructor reads CreateParams (and so IsWall) before our constructor assigns Zone.
    public bool IsWall => Zone is { Type: ZoneType.Fill, IsDual: false };

    /// <summary>Brings the wall above normal windows without activating it (topmost, then back to normal).</summary>
    public void RaiseWall()
    {
        if (!IsHandleCreated) return;
        User32.SetTopmost(Handle, true);
        User32.SetTopmost(Handle, false);
    }

    /// <summary>True when the wall is what the user sees at the center of its monitor.</summary>
    public bool WallIsOnTop()
    {
        if (!IsHandleCreated || !Visible) return false;
        return User32.TopLevelAt(new Point(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2)) == Handle;
    }

    public IReadOnlyList<SlotControl> Slots => _slots;

    public bool Editing => _editing;

    /// <summary>Spacing of the background grid drawn in edit mode, in pixels (a visual cue only; slots do not snap to it).</summary>
    public int GridPx => SlotGeometry.DipToPx(GridDip, DeviceDpi);

    /// <summary>How close (pixels) a dragged edge must be to another slot's edge to stick to it.</summary>
    public int SnapDistancePx => SlotGeometry.DipToPx(SnapDip, DeviceDpi);

    /// <summary>Spacing between neighbouring slots of the n×m arrangement, in pixels; 0 when the layout has none.</summary>
    public int SlotGapPx => SlotGeometry.DipToPx(_layout.Grid?.GapPx ?? 0, DeviceDpi);

    /// <summary>Del was pressed while the zone has the keyboard (edit mode only).</summary>
    public event Action? DeletePressed;

    public event Action<SlotControl>? SlotClicked;

    /// <summary>A bound source window was minimized.</summary>
    public event Action<SlotControl>? SourceMinimized;

    /// <summary>Raised whenever a slot is bound, cleared, or its source disappears.</summary>
    public event Action? BindingsChanged;

    /// <summary>Slots traded places (drag and drop); the layout needs saving.</summary>
    public event Action? SlotsReordered;

    /// <summary>Raised on WM_DISPLAYCHANGE (monitors added/removed, resolution changed).</summary>
    public event Action? DisplayChanged;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= User32.WsExToolWindow | User32.WsExNoActivate;
            if (!IsWall) cp.ExStyle |= User32.WsExTopmost;
            return cp;
        }
    }

    public void SetSlotMenu(ContextMenuStrip menu)
    {
        _slotMenu = menu;
        foreach (var s in _slots) s.ContextMenuStrip = menu;
    }

    public void SetEditing(bool editing)
    {
        _editing = editing;
        foreach (var s in _slots) s.SetEditing(editing);
        Invalidate();
    }

    public bool Bind(int index, nint source)
    {
        bool ok = _slots[index].Bind(source);
        AppLog.Info($"slot {index + 1}: bind 0x{source:X} -> {(ok ? "ok" : "failed")}");
        BindingsChanged?.Invoke();
        return ok;
    }

    public void Unbind(int index)
    {
        _slots[index].Unbind();
        BindingsChanged?.Invoke();
    }

    public SlotControl? SlotFor(nint source) => source == 0 ? null : _slots.FirstOrDefault(s => s.Source == source);

    public bool IsBound(nint source) => SlotFor(source) is not null;

    public void HighlightForeground(nint foreground)
    {
        foreach (var s in _slots) s.SetActive(s.Source != 0 && s.Source == foreground);
    }

    /// <summary>Adds a slot to the layout and the zone (edit mode).</summary>
    public SlotControl AddSlot(SlotModel model)
    {
        _layout.Slots.Add(model);
        var control = AddSlotControl(model);
        PlaceSlot(control);
        control.SetEditing(_editing);
        BindingsChanged?.Invoke();
        return control;
    }

    /// <summary>
    /// Drag-and-drop swap: the two slots (with their windows and rules) trade places. The list order follows,
    /// so slot numbers, hotkeys and later n×m re-arranging stay in step with what is on screen.
    /// </summary>
    private void SwapSlots(SlotControl a, SlotControl b)
    {
        int ia = _slots.IndexOf(a), ib = _slots.IndexOf(b);
        if (ia < 0 || ib < 0 || ia == ib) return;
        (a.Model.Rect, b.Model.Rect) = (b.Model.Rect, a.Model.Rect);
        (_slots[ia], _slots[ib]) = (b, a);
        int ma = _layout.Slots.IndexOf(a.Model), mb = _layout.Slots.IndexOf(b.Model);
        if (ma >= 0 && mb >= 0) (_layout.Slots[ma], _layout.Slots[mb]) = (b.Model, a.Model);
        for (int i = 0; i < _slots.Count; i++) _slots[i].Number = i + 1;
        LayoutSlots();
        BindingsChanged?.Invoke();
        SlotsReordered?.Invoke();
    }

    /// <summary>Removes a slot from the layout and the zone (edit mode).</summary>
    public void RemoveSlot(SlotControl slot)
    {
        _layout.Slots.Remove(slot.Model);
        _slots.Remove(slot);
        Controls.Remove(slot);
        slot.Dispose();
        for (int i = 0; i < _slots.Count; i++) _slots[i].Number = i + 1;
        BindingsChanged?.Invoke();
    }

    /// <summary>
    /// Stores a slot's current pixel bounds back into its relative rectangle.
    /// A pure move keeps the relative size exactly, so repeated moves never drift the size by rounding.
    /// </summary>
    public void CommitSlotBounds(SlotControl slot, bool resized)
    {
        var rel = SlotGeometry.ToRelative(slot.Bounds, ClientSize);
        rel = SlotSnap.Adopt(rel, slot.Bounds, _slots.Where(s => s != slot).Select(s => (s.Model.Rect, s.Bounds)), resized);
        var old = slot.Model.Rect;
        slot.Model.Rect = resized ? rel : SlotGeometry.ClampRelative(old with { X = rel.X, Y = rel.Y });
        PlaceSlot(slot);
    }

    /// <summary>Shows or hides the zone; hiding also releases the AppBar space.</summary>
    public void SetZoneVisible(bool visible)
    {
        if (visible == Visible) return;
        if (visible)
        {
            Show();
            RegisterAppBar();
            Reposition();
            if (IsWall) RaiseWall();
        }
        else
        {
            RemoveAppBar();
            Hide();
        }
    }

    /// <summary>Releases the AppBar reservation. Safe to call more than once and from any thread.</summary>
    public void RemoveAppBar()
    {
        nint hwnd = Interlocked.Exchange(ref _appBarHwnd, 0);
        if (hwnd == 0) return;
        AppBarNative.Remove(hwnd);
        AppLog.Info("appbar removed");
    }

    private SlotControl AddSlotControl(SlotModel model)
    {
        var slot = new SlotControl(model, _slots.Count + 1, _layout.ShowTitles) { ContextMenuStrip = _slotMenu };
        slot.SlotClicked += s => SlotClicked?.Invoke(s);
        slot.SourceMinimized += s => SourceMinimized?.Invoke(s);
        slot.SlotDroppedOn += SwapSlots;
        _slots.Add(slot);
        Controls.Add(slot);
        return slot;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _callbackMessage = User32.RegisterWindowMessage("ThumbDock.AppBarCallback");
        RegisterAppBar();
        Reposition();
        _refreshTimer.Start();
    }

    private void RegisterAppBar()
    {
        if (Zone.Type != ZoneType.Dock || _appBarHwnd != 0) return;
        if (AppBarNative.Register(Handle, _callbackMessage)) _appBarHwnd = Handle;
        AppLog.Info($"appbar register on {Zone.ThumbDevice} {Zone.Edge} -> {_appBarHwnd != 0}");
    }

    private void Reposition()
    {
        var screen = MonitorService.ScreenFor(Zone.ThumbDevice) ?? MonitorService.Primary;
        User32.MoveWindow(Handle, ZoneBounds(screen));
        LayoutSlots();
    }

    private Rectangle ZoneBounds(Screen screen)
    {
        if (Zone.Type == ZoneType.Fill) return screen.Bounds;
        var center = new Point(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y + screen.Bounds.Height / 2);
        bool horizontal = Zone.Edge is DockEdge.Top or DockEdge.Bottom;
        int thickness = SlotGeometry.DockThickness(_layout.ThumbZone.SizePx, User32.GetDpiAt(center), horizontal ? screen.Bounds.Height : screen.Bounds.Width);
        var edge = AppBarNative.FromDockEdge(Zone.Edge);
        return _appBarHwnd != 0
            ? AppBarNative.QueryAndSetPos(Handle, edge, screen.Bounds, thickness)
            : StripOf(screen.WorkingArea, Zone.Edge, thickness);
    }

    // Used only when the AppBar could not be registered: a plain strip that does not reserve space.
    private static Rectangle StripOf(Rectangle area, DockEdge edge, int thickness) => edge switch
    {
        DockEdge.Left => new Rectangle(area.Left, area.Top, thickness, area.Height),
        DockEdge.Right => new Rectangle(area.Right - thickness, area.Top, thickness, area.Height),
        DockEdge.Top => new Rectangle(area.Left, area.Top, area.Width, thickness),
        _ => new Rectangle(area.Left, area.Bottom - thickness, area.Width, thickness),
    };

    private void LayoutSlots()
    {
        foreach (var s in _slots) PlaceSlot(s);
    }

    private void PlaceSlot(SlotControl s) => s.Bounds = SlotGeometry.ToPixels(s.Model.Rect, ClientSize);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutSlots();
    }

    private void RefreshSlots()
    {
        bool changed = false;
        foreach (var s in _slots) changed |= !s.RefreshState();
        if (changed) BindingsChanged?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_editing) DrawGrid(e.Graphics);
    }

    private void DrawGrid(Graphics g)
    {
        int step = Math.Max(4, GridPx * 2);
        using var pen = new Pen(GridColor);
        for (int x = 0; x < ClientSize.Width; x += step) g.DrawLine(pen, x, 0, x, ClientSize.Height);
        for (int y = 0; y < ClientSize.Height; y += step) g.DrawLine(pen, 0, y, ClientSize.Width, y);
    }

    // The zone never takes the keyboard by itself; edit mode activates it on a slot click so Del can reach it.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_editing && keyData == Keys.Delete)
        {
            DeletePressed?.Invoke();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_DISPLAYCHANGE) DisplayChanged?.Invoke();
        else if (m.Msg == WM_DPICHANGED) BeginInvoke(Reposition);
        HandleAppBarMessages(m);
    }

    private void HandleAppBarMessages(Message m)
    {
        if (_appBarHwnd == 0) return;
        if (m.Msg == WM_ACTIVATE) AppBarNative.NotifyActivate(Handle);
        else if (m.Msg == WM_WINDOWPOSCHANGED) AppBarNative.NotifyWindowPosChanged(Handle);
        else if (m.Msg == (int)_callbackMessage) HandleAppBarNotification(m);
    }

    private void HandleAppBarNotification(Message m)
    {
        switch ((int)m.WParam)
        {
            case AppBarNative.NotifyPosChanged:
                Reposition();
                break;
            case AppBarNative.NotifyFullScreenApp:
                // Step out of the topmost band while a full-screen app is open on this monitor (R-4).
                User32.SetTopmost(Handle, m.LParam == 0);
                break;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Dispose();
        foreach (var s in _slots) s.Unbind();
        RemoveAppBar();
        base.OnFormClosed(e);
    }
}

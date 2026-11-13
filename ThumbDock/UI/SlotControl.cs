using System.Drawing.Drawing2D;
using ThumbDock.Core;
using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.UI;

/// <summary>
/// One slot: draws frame, title and state, owns the DWM thumbnail drawn on top of it, and reports clicks.
/// The thumbnail is registered on the top-level dock window, so its rectangle is in dock client coordinates.
/// </summary>
internal sealed class SlotControl : Control
{
    private const int InnerPadding = 4;
    private static readonly Color Back = Color.FromArgb(38, 38, 38);
    private static readonly Color Frame = Color.FromArgb(70, 70, 70);
    private static readonly Color ActiveFrame = Color.FromArgb(255, 160, 40);
    private static readonly Color EditFrame = Color.FromArgb(90, 160, 255);
    private static readonly Color BadgeBack = Color.FromArgb(120, 90, 0);

    private readonly bool _showTitle;
    private ThumbnailHandle? _thumb;
    private string _title = "";
    private bool _minimized;
    private bool _elevated; // source runs as administrator while ThumbDock does not: it cannot be moved (UIPI)
    private bool _active;
    private bool _editing;
    private bool _selected;
    private int _number;

    public SlotControl(SlotModel model, int number, bool showTitle)
    {
        Model = model;
        _number = number;
        _showTitle = showTitle;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Back;
        ForeColor = Color.Gainsboro;
    }

    /// <summary>The slot's layout data (position, rule, options). Shared with the layout file model.</summary>
    public SlotModel Model { get; }

    /// <summary>1-based slot number shown to the user and used by hotkeys.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Number
    {
        get => _number;
        set { _number = value; Invalidate(); }
    }

    public nint Source => _thumb?.Source ?? 0;

    /// <summary>Size of the resize grip in the bottom-right corner, in pixels.</summary>
    public int GripSize => SlotGeometry.DipToPx(14, DeviceDpi);

    public event Action<SlotControl>? SlotClicked;

    /// <summary>The slot was dragged (outside edit mode) and dropped on another slot: swap their places.</summary>
    public event Action<SlotControl, SlotControl>? SlotDroppedOn;

    private Point _pressAt;
    private bool _pressed, _dragSwap;

    /// <summary>The bound source window was just minimized (also raised when binding an already minimized window).</summary>
    public event Action<SlotControl>? SourceMinimized;

    public bool SourceIsMinimized => _thumb is not null && _minimized;

    /// <summary>Edit mode: the slot that was clicked last (Del removes it). Drawn with a solid, thicker frame.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); } }
    }

    public void SetEditing(bool editing)
    {
        _editing = editing;
        if (!editing) _selected = false;
        Cursor = editing ? Cursors.SizeAll : Cursors.Default;
        Invalidate();
    }

    public bool Bind(nint source)
    {
        Unbind();
        var host = FindForm();
        if (host is null) return false;
        _thumb = ThumbnailHandle.Register(host.Handle, source);
        if (_thumb is null) return false;
        _elevated = ProcessElevation.BlocksMoving(source);
        RefreshState();
        return true;
    }

    public void Unbind()
    {
        _thumb?.Dispose();
        _thumb = null;
        _title = "";
        _minimized = false;
        _elevated = false;
        _active = false;
        Invalidate();
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        Invalidate();
    }

    /// <summary>Refreshes title/minimized state and the thumbnail rectangle. Returns false if the source window disappeared.</summary>
    public bool RefreshState()
    {
        if (_thumb is null) return true;
        if (!_thumb.IsSourceAlive)
        {
            AppLog.Info($"slot {Number}: source closed, waiting");
            Unbind();
            return false;
        }
        UpdateText(User32.GetTitle(_thumb.Source), User32.IsMinimized(_thumb.Source));
        UpdateThumbnail();
        return true;
    }

    private void UpdateText(string title, bool minimized)
    {
        if (title == _title && minimized == _minimized) return;
        bool justMinimized = minimized && !_minimized;
        _title = title;
        _minimized = minimized;
        Invalidate();
        if (justMinimized) SourceMinimized?.Invoke(this);
    }

    private void UpdateThumbnail()
    {
        var host = FindForm();
        if (_thumb is null || host is null) return;
        var box = SlotGeometry.Letterbox(SplitArea().Thumb, _thumb.SourceSize ?? Size.Empty);
        box.Offset(host.PointToClient(PointToScreen(Point.Empty)));
        _thumb.Update(box, Visible && !box.IsEmpty);
    }

    /// <summary>
    /// Thumbnail, title and minimized-badge areas. DWM draws the thumbnail above anything this control paints,
    /// so the badge (R-1: a minimized window's thumbnail is frozen) gets its own strip above the thumbnail.
    /// </summary>
    private (Rectangle Thumb, Rectangle Title, Rectangle Badge) SplitArea()
    {
        var (thumb, title) = SlotGeometry.SplitTitle(ClientRectangle, Font.Height + 6, _showTitle, InnerPadding);
        if (!SourceIsMinimized || thumb.Height < Font.Height * 3) return (thumb, title, Rectangle.Empty);
        int h = Font.Height + 6;
        return (Rectangle.FromLTRB(thumb.Left, thumb.Top + h, thumb.Right, thumb.Bottom), title,
                new Rectangle(thumb.Left, thumb.Top, thumb.Width, h));
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        UpdateThumbnail();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateThumbnail();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_editing || e.Button != MouseButtons.Left) return;
        _pressed = true;
        _dragSwap = false;
        _pressAt = e.Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_pressed || _dragSwap) return;
        var drag = SystemInformation.DragSize;
        if (Math.Abs(e.X - _pressAt.X) < drag.Width && Math.Abs(e.Y - _pressAt.Y) < drag.Height) return;
        _dragSwap = true;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        bool wasDrag = _dragSwap;
        _pressed = _dragSwap = false;
        if (_editing || e.Button != MouseButtons.Left) return;
        Cursor = Cursors.Default;
        if (wasDrag)
        {
            // The mouse is captured by this control, so find the slot under the cursor through the parent.
            if (Parent?.GetChildAtPoint(Parent.PointToClient(Cursor.Position)) is SlotControl target && target != this)
                SlotDroppedOn?.Invoke(this, target);
            return;
        }
        if (ClientRectangle.Contains(e.Location)) SlotClicked?.Invoke(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var (thumb, title, badge) = SplitArea();
        if (_thumb is null) DrawWaiting(e.Graphics, thumb.IsEmpty ? ClientRectangle : thumb);
        if (!badge.IsEmpty) DrawBadge(e.Graphics, badge);
        if (!title.IsEmpty) DrawTitle(e.Graphics, title);
        DrawFrame(e.Graphics);
        if (_editing) DrawGrip(e.Graphics);
    }

    private void DrawWaiting(Graphics g, Rectangle area)
    {
        string text = Model.Match is { } rule
            ? Loc.T("等待中\n{0}", Describe(rule))
            : Loc.T("等待中\n點一下選擇視窗");
        TextRenderer.DrawText(g, text, Font, area, Color.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    private static string Describe(MatchRule rule) =>
        string.Join(" ", new[] { rule.Process, string.IsNullOrEmpty(rule.TitleRegex) ? "" : Loc.T("「{0}」", rule.TitleRegex) }
            .Where(s => !string.IsNullOrEmpty(s)));

    private void DrawBadge(Graphics g, Rectangle area)
    {
        using var brush = new SolidBrush(BadgeBack);
        g.FillRectangle(brush, area);
        TextRenderer.DrawText(g, Loc.T("已最小化・縮圖暫停（點一下還原）"), Font, area, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void DrawTitle(Graphics g, Rectangle area)
    {
        if (_elevated)
        {
            // Shield at the right end of the title strip (the DWM thumbnail covers anything drawn over it).
            int size = Math.Min(area.Height - 4, SlotGeometry.DipToPx(16, DeviceDpi));
            if (size >= 8 && area.Width > size * 3)
            {
                DrawShield(g, new Rectangle(area.Right - size - 2, area.Top + (area.Height - size) / 2, size, size));
                area = new Rectangle(area.Left, area.Top, area.Width - size - 4, area.Height);
            }
        }
        string text = _thumb is null ? $"{Number}." : $"{Number}. {(_minimized ? Loc.T("[已最小化] ") : "")}{_title}";
        var color = _minimized ? Color.Khaki : ForeColor;
        TextRenderer.DrawText(g, text, Font, area, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>Small shield: marks a window that runs as administrator and so cannot be moved by ThumbDock.</summary>
    private static void DrawShield(Graphics g, Rectangle r)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        PointF[] shield =
        [
            new(r.Left + r.Width * 0.1f, r.Top + r.Height * 0.1f), new(r.Left + r.Width * 0.9f, r.Top + r.Height * 0.1f),
            new(r.Left + r.Width * 0.9f, r.Top + r.Height * 0.5f), new(r.Left + r.Width * 0.5f, r.Bottom - 1),
            new(r.Left + r.Width * 0.1f, r.Top + r.Height * 0.5f),
        ];
        using var fill = new SolidBrush(Color.FromArgb(40, 110, 220));
        using var edge = new Pen(Color.White, 1.2f);
        g.FillPolygon(fill, shield);
        g.DrawPolygon(edge, shield);
        g.SmoothingMode = old;
    }

    private void DrawFrame(Graphics g)
    {
        int width = _active || _editing ? 2 : 1;
        if (_active && !_editing) width = 3;
        if (_editing && _selected) width = 4;
        using var pen = new Pen(_editing ? EditFrame : _active ? ActiveFrame : Frame, width);
        if (_editing && !_selected) pen.DashStyle = DashStyle.Dash;
        var r = ClientRectangle;
        g.DrawRectangle(pen, r.X + width / 2, r.Y + width / 2, r.Width - width, r.Height - width);
    }

    private void DrawGrip(Graphics g)
    {
        int s = GripSize;
        var r = ClientRectangle;
        Point[] triangle = [new(r.Right, r.Bottom - s), new(r.Right, r.Bottom), new(r.Right - s, r.Bottom)];
        using var brush = new SolidBrush(EditFrame);
        g.FillPolygon(brush, triangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _thumb?.Dispose();
        base.Dispose(disposing);
    }
}

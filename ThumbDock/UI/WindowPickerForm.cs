using ThumbDock.Native;
using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.UI;

/// <summary>A top-level menu of the picker window; <see cref="Fill"/> repopulates its items whenever it opens.</summary>
internal sealed record PickerMenuSpec(string Title, Action<ToolStripMenuItem> Fill);

/// <summary>
/// Window list with one checkbox column per slot: ticking column N binds that window to slot N,
/// unticking clears the slot. The list refreshes itself while visible.
/// </summary>
internal sealed class WindowPickerForm : Form
{
    private const int RefreshMs = 1500;
    private const string PlaceCol = "place", HomeCol = "home";
    private const string IconCol = "icon", TitleCol = "title", ProcessCol = "process", DesktopCol = "desktop", ElevCol = "elevation";

    private readonly DockForm _dock;
    private readonly WindowEnumerator _enumerator;
    private readonly IWindowOptions _options;
    private List<WindowInfo> _windows = [];
    private readonly WindowIcons _icons = new();
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RefreshMs };

    public WindowPickerForm(DockForm dock, WindowEnumerator enumerator, IWindowOptions options,
        IReadOnlyList<PickerMenuSpec> menus, Action openSettings)
    {
        _dock = dock;
        _enumerator = enumerator;
        _options = options;
        Text = Loc.T("ThumbDock {0} － 選擇視窗", AppInfo.Version);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1080, 540);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true; // a docked strip is TOPMOST; without this the picker can open behind it
        BuildGrid();
        Controls.Add(_grid);
        Controls.Add(_status);
        Controls.Add(new Label
        {
            Dock = DockStyle.Top, Height = 48, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0),
            Text = Loc.T("勾選欄位 1–{0} 即可把視窗放進對應的縮圖槽位，再點一次取消。清單會自動更新。\r\n「點縮圖時」點一下切換：不變／最大化／填滿操作區（不最大化，大小設成螢幕扣掉縮圖區）。「原位置」：綁定時記錄的位置。", dock.Slots.Count),
        });
        Controls.Add(BuildMenuBar(menus, openSettings)); // added last so it docks above everything else
        _timer.Tick += (_, _) => RefreshList();
        _grid.CellMouseClick += OnCellClick;
        _grid.MouseMove += (_, _) => UpdateHover();
        _grid.MouseLeave += (_, _) => SetHover(-1, -1);
        _grid.Scroll += (_, _) => UpdateHover();
        _grid.CellPainting += PaintCrosshair;
        _dock.BindingsChanged += UpdateChecks;
    }

    private int _slotColumns;

    // ---- crosshair: with many slots and windows, shows which window (row) and which slot (column) the cursor is on ----

    private static readonly Color CrossBand = Color.FromArgb(56, 255, 160, 40), CrossCell = Color.FromArgb(130, 255, 160, 40);
    private int _hoverRow = -1, _hoverCol = -1;

    private void UpdateHover()
    {
        var hit = _grid.HitTest(_grid.PointToClient(Cursor.Position).X, _grid.PointToClient(Cursor.Position).Y);
        // Only over the checkbox cells: that is where a wrong tick happens.
        bool onSlotCell = hit.Type == DataGridViewHitTestType.Cell && hit.ColumnIndex < _slotColumns;
        SetHover(onSlotCell ? hit.RowIndex : -1, onSlotCell ? hit.ColumnIndex : -1);
    }

    private void SetHover(int row, int col)
    {
        if (row == _hoverRow && col == _hoverCol) return;
        int oldRow = _hoverRow, oldCol = _hoverCol;
        (_hoverRow, _hoverCol) = (row, col);
        foreach (int r in new[] { oldRow, row })
            if (r >= 0 && r < _grid.Rows.Count) _grid.InvalidateRow(r);
        foreach (int c in new[] { oldCol, col })
            if (c >= 0 && c < _grid.Columns.Count) _grid.InvalidateColumn(c);
    }

    private void PaintCrosshair(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (_hoverRow < 0 || _hoverCol < 0) return;
        bool inRow = e.RowIndex == _hoverRow, inCol = e.ColumnIndex == _hoverCol;
        if (!inRow && !inCol) return;
        e.Paint(e.CellBounds, DataGridViewPaintParts.All);
        if (e.Graphics is { } g)
        {
            using var brush = new SolidBrush(inRow && inCol ? CrossCell : CrossBand);
            g.FillRectangle(brush, e.CellBounds);
        }
        e.Handled = true;
    }

    // Drop-downs are refilled each time they open so checkmarks, layout names and launch state are current.
    private MenuStrip BuildMenuBar(IReadOnlyList<PickerMenuSpec> menus, Action openSettings)
    {
        var bar = new MenuStrip { Dock = DockStyle.Top };
        for (int i = 0; i < menus.Count; i++)
        {
            var spec = menus[i];
            var root = new ToolStripMenuItem(spec.Title);
            root.DropDownItems.Add("…"); // placeholder so the drop-down can open
            root.DropDownOpening += (_, _) =>
            {
                root.DropDownItems.Clear();
                spec.Fill(root);
            };
            bar.Items.Add(root);
            // Order: 佈局, 設定, 檢視, 啟動時執行 (settings sits right after the layout menu).
            if (i == 0) bar.Items.Add(new ToolStripMenuItem(Loc.T("設定…"), null, (_, _) => openSettings()));
        }
        if (menus.Count == 0) bar.Items.Add(new ToolStripMenuItem(Loc.T("設定…"), null, (_, _) => openSettings()));
        MainMenuStrip = bar;
        return bar;
    }

    private void BuildGrid()
    {
        _slotColumns = _dock.Slots.Count;
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        for (int i = 0; i < _dock.Slots.Count; i++)
        {
            _grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = (i + 1).ToString(), Width = 32, SortMode = DataGridViewColumnSortMode.NotSortable,
                ToolTipText = Loc.T("放進槽位 {0}", i + 1),
            });
        }
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = PlaceCol, HeaderText = Loc.T("點縮圖時"), Width = 90, SortMode = DataGridViewColumnSortMode.NotSortable,
            ToolTipText = Loc.T("點一下切換：不變 → 最大化 → 填滿操作區"),
            DefaultCellStyle = new DataGridViewCellStyle { ForeColor = SystemColors.HotTrack },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = HomeCol, HeaderText = Loc.T("原位置"), Width = 230 });
        _grid.Columns.Add(new DataGridViewImageColumn { Name = IconCol, HeaderText = "", Width = 28, ImageLayout = DataGridViewImageCellLayout.Zoom });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = TitleCol, HeaderText = Loc.T("標題"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = ProcessCol, HeaderText = Loc.T("行程"), Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = DesktopCol, HeaderText = Loc.T("虛擬桌面"), Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = ElevCol, HeaderText = Loc.T("權限"), Width = 110 });
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) { RefreshList(); _timer.Start(); }
        else _timer.Stop();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the picker only hides it; the app keeps running in the tray.
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    private void RefreshList()
    {
        var windows = _windows = _enumerator.Enumerate();
        var alive = windows.Select(w => w.Hwnd).ToHashSet();
        RemoveVanishedRows(alive);
        var rowsByHwnd = _grid.Rows.Cast<DataGridViewRow>().ToDictionary(r => (nint)r.Tag!);
        foreach (var w in windows)
        {
            var row = rowsByHwnd.TryGetValue(w.Hwnd, out var existing) ? existing : AddRow(w);
            UpdateRow(row, w);
        }
        _icons.Prune(alive);
        UpdateChecks();
        if (_hoverRow >= 0) UpdateHover(); // rows may have moved under a resting cursor
        _status.Text = Loc.T("  共 {0} 個視窗", windows.Count);
    }

    private void RemoveVanishedRows(IReadOnlySet<nint> alive)
    {
        for (int i = _grid.Rows.Count - 1; i >= 0; i--)
            if (!alive.Contains((nint)_grid.Rows[i].Tag!)) _grid.Rows.RemoveAt(i);
    }

    private DataGridViewRow AddRow(WindowInfo w)
    {
        int index = _grid.Rows.Add();
        var row = _grid.Rows[index];
        row.Tag = w.Hwnd;
        row.Cells[IconCol].Value = _icons.Get(w.Hwnd, w.ProcessId);
        // A window's elevation never changes, so it is read once per row. When ThumbDock is not elevated such a window
        // cannot be moved (UIPI); the thumbnail still works.
        row.Cells[ElevCol].Value = ProcessElevation.OfWindow(w.Hwnd) == true
            ? (ProcessElevation.AppIsElevated ? Loc.T("管理員") : Loc.T("管理員（無法移動）")) : "";
        return row;
    }

    private void UpdateRow(DataGridViewRow row, WindowInfo w)
    {
        SetIfChanged(row.Cells[PlaceCol], WindowPreferences.Label(_options.PlaceFor(w.Hwnd)));
        SetIfChanged(row.Cells[HomeCol], _options.LocationText(w.Hwnd));
        SetIfChanged(row.Cells[TitleCol], w.Title);
        SetIfChanged(row.Cells[ProcessCol], w.ProcessName);
        SetIfChanged(row.Cells[DesktopCol], w.DesktopLabel);
    }

    private static void SetIfChanged(DataGridViewCell cell, object value)
    {
        if (!Equals(cell.Value, value)) cell.Value = value;
    }

    private void UpdateChecks()
    {
        if (IsDisposed) return;
        if (_slotColumns != _dock.Slots.Count) { RebuildGrid(); return; }
        foreach (DataGridViewRow row in _grid.Rows)
        {
            nint hwnd = (nint)row.Tag!;
            for (int i = 0; i < _dock.Slots.Count; i++)
                SetIfChanged(row.Cells[i], _dock.Slots[i].Source == hwnd);
        }
    }

    // Slots were added or removed in edit mode: rebuild the checkbox columns.
    private void RebuildGrid()
    {
        (_hoverRow, _hoverCol) = (-1, -1);
        _grid.Rows.Clear();
        _grid.Columns.Clear();
        BuildGrid();
        if (Visible) RefreshList();
    }

    private void OnCellClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        nint hwnd = (nint)row.Tag!;
        if (_grid.Columns[e.ColumnIndex].Name == PlaceCol) { CyclePlace(row, hwnd); return; }
        if (e.ColumnIndex >= _slotColumns || e.ColumnIndex >= _dock.Slots.Count) return;
        int slot = e.ColumnIndex;
        if (_dock.Slots[slot].Source == hwnd) { _options.ClearSlot(slot); return; }
        if (_windows.FirstOrDefault(w => w.Hwnd == hwnd) is not { } window) return;
        bool ok = _options.BindManually(slot, window, _windows);
        SetIfChanged(row.Cells[HomeCol], _options.LocationText(hwnd));
        _status.Text = ok ? Loc.T("  已放進槽位 {0}", slot + 1) : Loc.T("  無法建立縮圖（視窗可能已關閉）");
    }

    private void CyclePlace(DataGridViewRow row, nint hwnd)
    {
        var place = WindowPreferences.Next(_options.PlaceFor(hwnd));
        _options.SetPlace(hwnd, place);
        SetIfChanged(row.Cells[PlaceCol], WindowPreferences.Label(place));
        _status.Text = place switch
        {
            PlaceMode.Maximize => Loc.T("  點縮圖時會在原本的螢幕上最大化"),
            PlaceMode.Fit => Loc.T("  點縮圖時會調成操作區大小（不最大化）"),
            _ => Loc.T("  點縮圖時保持原位置與大小"),
        };
    }

    /// <summary>Shows the picker, optionally preselecting a slot number in the status bar hint.</summary>
    public void ShowFor(int? slotNumber)
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        if (slotNumber is { } n) _status.Text = Loc.T("  請在欄位 {0} 勾選要放進槽位 {0} 的視窗", n);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dock.BindingsChanged -= UpdateChecks;
            _timer.Dispose();
            _icons.Dispose();
        }
        base.Dispose(disposing);
    }
}

using ThumbDock.Core;
using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.UI;

/// <summary>
/// Runs one layout on screen: owns the dock window, binds slots (manually and by rule, FR-5),
/// activates windows (FR-3), edit mode (FR-4), slot menus (FR-7) and reacts to display changes (FR-6).
/// </summary>
internal sealed class DockSession : IWindowOptions, IDisposable
{
    private const int AutoBindMs = 1500, DisplaySettleMs = 1000;

    private readonly WindowEnumerator _enumerator;
    private readonly WindowActivator _activator;
    private readonly WallController _wall;
    private readonly WindowPreferences _unbound = new();
    private readonly EditModeController _edit = new();
    private readonly ForegroundTracker _foreground = new();
    private readonly System.Windows.Forms.Timer _bindTimer = new() { Interval = AutoBindMs };
    private readonly System.Windows.Forms.Timer _displayTimer = new() { Interval = DisplaySettleMs };
    private readonly ContextMenuStrip _slotMenu = new();
    private DockForm _dock = null!;
    private string? _lastFallback;
    private Point _dockMenuPoint;

    public DockSession(WindowEnumerator enumerator, LayoutModel layout)
    {
        _enumerator = enumerator;
        _activator = new WindowActivator(new VirtualDesktopPull(enumerator.Desktops));
        _activator.PullFellBack += m => Notice?.Invoke(m);
        _activator.MoveBlocked += OnMoveBlocked;
        _wall = new WallController(() => _dock, _activator);
        Layout = layout;
        _slotMenu.Opening += (_, e) => e.Cancel = !BuildSlotMenu();
        _edit.Changed += () => LayoutChanged?.Invoke();
        _foreground.Changed += h => _dock.HighlightForeground(h);
        _bindTimer.Tick += (_, _) => AutoBind();
        _displayTimer.Tick += (_, _) => { _displayTimer.Stop(); OnDisplaySettled(); };
        Build(preserve: null);
        _bindTimer.Start();
    }

    public LayoutModel Layout { get; private set; }

    public DockForm Dock => _dock;

    public bool Editing => _edit.IsEditing;

    public bool ZoneVisible => _dock.Visible;

    /// <summary>The layout changed and should be saved.</summary>
    public event Action? LayoutChanged;

    /// <summary>A message for the user (tray balloon).</summary>
    public event Action<string>? Notice;

    /// <summary>The user wants the window picker, optionally for a slot number.</summary>
    public event Action<int?>? PickerRequested;

    /// <summary>The dock window was recreated (slot count/zone changed); dependants must re-attach.</summary>
    public event Action? DockRebuilt;

    public event Action? SettingsRequested;

    /// <summary>Switches to another layout.</summary>
    public void Load(LayoutModel layout)
    {
        _wall.RestoreAll(Layout); // windows the old layout's wall maximized go back before the new layout takes over
        Layout = layout;
        Build(preserve: null);
    }

    /// <summary>Recreates the dock for the current layout (zone settings changed), keeping bound windows.</summary>
    public void Rebuild() => Build(_dock.Slots.ToDictionary(s => s.Model.Id, s => s.Source));

    /// <summary>True while the settings window is open: entering edit mode would be overwritten by a re-arrange on OK.</summary>
    public bool EditLocked { get; set; }

    /// <summary>Why "edit slot positions" is unavailable (shown on the disabled menu item), or null when it is allowed.</summary>
    public string? EditBlockedText =>
        Editing ? null
        : EditLocked ? Loc.T("編輯槽位位置（請先關閉設定視窗）")
        : DefaultLayouts.IsReserved(Layout.Name) ? Loc.T("編輯槽位位置（{0} 為固定佈局，請先另存為新佈局）", DefaultLayouts.ReservedName)
        : null;

    public void SetEditing(bool editing)
    {
        if (editing == Editing) return;
        if (editing && EditLocked)
        {
            Notice?.Invoke(Loc.T("設定視窗開啟中，請先關閉設定視窗才能編輯槽位位置。"));
            return;
        }
        if (editing && DefaultLayouts.IsReserved(Layout.Name))
        {
            Notice?.Invoke(Loc.T("「{0}」是固定佈局（安全啟動使用），不能編輯槽位位置。要自訂請先「另存為」新佈局。", DefaultLayouts.ReservedName));
            return;
        }
        if (editing) _edit.Begin(_dock);
        else { _edit.End(); LayoutChanged?.Invoke(); }
    }

    public void SetZoneVisible(bool visible) => _dock.SetZoneVisible(visible);

    /// <summary>
    /// The toggle hotkey. Dock zones show or hide; the wall comes to the front, and when it already is in front
    /// goes back to the window that was shown last. A hidden wall is shown first.
    /// </summary>
    public void ToggleByHotkey()
    {
        if (!_dock.IsWall) { SetZoneVisible(!ZoneVisible); return; }
        if (!_dock.Visible) { SetZoneVisible(true); return; }
        _wall.Toggle(Layout);
    }

    /// <summary>
    /// Hot edge: raise the wall (restoring the windows it maximized). Unlike the toggle hotkey it never goes back to the
    /// last window, and it leaves a hidden wall alone.
    /// </summary>
    public void ReturnToWallFromEdge()
    {
        if (!_dock.IsWall || !_dock.Visible || _dock.WallIsOnTop()) return;
        _wall.ReturnToWall(Layout);
    }

    /// <summary>Bounds of the wall's monitor, or null when the zone is not a visible wall.</summary>
    public Rectangle? WallMonitorBounds() =>
        _dock.IsWall && _dock.Visible ? (MonitorService.ScreenFor(_dock.Zone.ThumbDevice) ?? MonitorService.Primary).Bounds : null;

    /// <summary>True when the thumbnail zone is the single-monitor wall.</summary>
    public bool IsWall => _dock.IsWall;

    /// <summary>Hotkey: activate slot N (1-based).</summary>
    public void ActivateSlot(int number)
    {
        if (number >= 1 && number <= _dock.Slots.Count && _dock.Slots[number - 1].Source != 0) Activate(_dock.Slots[number - 1]);
    }

    /// <summary>Best-effort AppBar removal for crashes; safe from any thread.</summary>
    public void EmergencyCleanup() => _dock?.RemoveAppBar();

    private void Build(Dictionary<int, nint>? preserve)
    {
        bool wasEditing = Editing;
        _edit.End();
        var old = _dock;
        var zone = MonitorService.Resolve(Layout, MonitorService.Current());
        ReportFallback(zone.FallbackReason);
        _dock = new DockForm(Layout, zone);
        _dock.SetSlotMenu(_slotMenu);
        _dock.ContextMenuStrip = BuildDockMenu();
        _dock.SlotClicked += OnSlotClicked;
        _dock.SlotsReordered += () => LayoutChanged?.Invoke();
        _dock.SourceMinimized += OnSourceMinimized;
        _dock.DisplayChanged += () => { _displayTimer.Stop(); _displayTimer.Start(); };
        old?.Close();
        old?.Dispose();
        _dock.Show();
        if (_dock.IsWall) _dock.RaiseWall();
        RestoreBindings(preserve);
        AutoBind();
        if (wasEditing) _edit.Begin(_dock);
        AppLog.Info($"layout '{Layout.Name}' on {zone}");
        DockRebuilt?.Invoke();
    }

    private void ReportFallback(string? reason)
    {
        if (reason is not null && reason != _lastFallback) Notice?.Invoke(reason);
        else if (reason is null && _lastFallback is not null) Notice?.Invoke(Loc.T("螢幕配置已恢復，使用佈局原本的設定。"));
        _lastFallback = reason;
    }

    private void RestoreBindings(Dictionary<int, nint>? preserve)
    {
        if (preserve is null) return;
        for (int i = 0; i < _dock.Slots.Count; i++)
            if (preserve.TryGetValue(_dock.Slots[i].Model.Id, out nint hwnd) && hwnd != 0) _dock.Bind(i, hwnd);
    }

    // FR-5: waiting slots with a rule get the first matching window that no other slot uses.
    private void AutoBind()
    {
        if (!_dock.Slots.Any(s => s.Source == 0 && s.Model.Match is not null)) return;
        var windows = _enumerator.Enumerate();
        var plan = SlotBinder.Plan(_dock.Slots.Select(s => s.Model.Match).ToList(), _dock.Slots.Select(s => s.Source).ToList(), windows);
        foreach (var (index, hwnd) in plan)
        {
            if (!_dock.Bind(index, hwnd)) continue;
            var model = _dock.Slots[index].Model;
            if (model.Home is null) { model.Home = WindowPreferences.Capture(hwnd); LayoutChanged?.Invoke(); }
            AppLog.Info($"slot {index + 1}: auto-bound by rule {model.Match}");
        }
    }

    private void OnDisplaySettled()
    {
        AppLog.Info("display changed");
        Rebuild();
    }

    private readonly HashSet<nint> _blockedNotified = [];

    // Once per window per run: UIPI silently refuses to move an elevated window, so say why it stayed where it is.
    private void OnMoveBlocked(nint hwnd)
    {
        if (!_blockedNotified.Add(hwnd)) return;
        Notice?.Invoke(Loc.T("「{0}」以系統管理員身分執行，ThumbDock 無法移動或調整它的位置與大小（縮圖與切換不受影響）。請自行移動它，或以系統管理員身分啟動 ThumbDock。", User32.GetTitle(hwnd)));
    }

    private void OnSlotClicked(SlotControl slot)
    {
        if (Editing) return;
        if (slot.Source == 0) PickerRequested?.Invoke(slot.Number);
        else Activate(slot);
    }

    // R-1 option: a minimized window's thumbnail is frozen, so put it back (behind everything, not activated).
    private void OnSourceMinimized(SlotControl slot)
    {
        if (!Layout.AutoRestoreMinimized || slot.Source == 0) return;
        nint hwnd = slot.Source;
        bool ok = User32.IsWindow(hwnd) && User32.RestoreBehind(hwnd);
        AppLog.Info($"slot {slot.Number}: source 0x{hwnd:X} minimized -> restore behind {(ok ? "ok" : "failed")}");
    }

    /// <summary>Applies the auto-restore option to sources that are minimized right now (after the option was turned on).</summary>
    public void RestoreMinimizedNow()
    {
        if (!Layout.AutoRestoreMinimized) return;
        foreach (var s in _dock.Slots.Where(s => s.SourceIsMinimized)) OnSourceMinimized(s);
    }

    private void Activate(SlotControl slot)
    {
        if (_dock.IsWall)
        {
            if (_wall.Show(slot, Layout) == ActivationResult.SourceGone) _dock.Unbind(slot.Number - 1);
            return;
        }
        var zone = _dock.Zone;
        var ctx = new ActivationContext(
            zone.IsDual ? zone.ThumbDevice : null,
            MonitorService.ScreenFor(zone.FocusDevice) ?? MonitorService.Primary,
            _dock.Visible ? _dock.Bounds : Rectangle.Empty,
            Layout.CrossDesktop);
        var result = _activator.Activate(slot.Source, slot.Model.Place, slot.Model.Home, ctx);
        if (result == ActivationResult.SourceGone) _dock.Unbind(slot.Number - 1); // rule kept: waits for the window to return
    }

    // ---- IWindowOptions (window picker) ----

    public bool BindManually(int slotIndex, WindowInfo window, IReadOnlyList<WindowInfo> allWindows)
    {
        if (!_dock.Bind(slotIndex, window.Hwnd)) return false;
        var model = _dock.Slots[slotIndex].Model;
        model.Match = WindowMatcher.CreateRule(window, allWindows);
        model.Place = _unbound.PlaceFor(window.Hwnd);
        model.Home = WindowPreferences.Capture(window.Hwnd);
        LayoutChanged?.Invoke();
        return true;
    }

    /// <summary>Binds waiting slots to matching windows right now (the same work the timer does).</summary>
    public void AutoBindNow() => AutoBind();

    /// <summary>Sets a slot's match rule (launch configs).</summary>
    public void SetRule(int slotIndex, MatchRule rule)
    {
        var model = _dock.Slots[slotIndex].Model;
        if (Equals(model.Match, rule)) return;
        model.Match = rule;
        LayoutChanged?.Invoke();
    }

    public void ClearSlot(int slotIndex)
    {
        _dock.Unbind(slotIndex);
        var model = _dock.Slots[slotIndex].Model;
        model.Match = null;
        model.Home = null;
        LayoutChanged?.Invoke();
    }

    public PlaceMode PlaceFor(nint hwnd) => _dock.SlotFor(hwnd)?.Model.Place ?? _unbound.PlaceFor(hwnd);

    public void SetPlace(nint hwnd, PlaceMode place)
    {
        _unbound.SetPlace(hwnd, place);
        var bound = _dock.Slots.Where(s => s.Source == hwnd).ToList();
        foreach (var s in bound) s.Model.Place = place;
        if (bound.Count > 0) LayoutChanged?.Invoke();
    }

    public string LocationText(nint hwnd) =>
        _dock.SlotFor(hwnd)?.Model.Home is { } home ? WindowPreferences.DescribeSaved(home) : WindowPreferences.DescribeLive(hwnd);

    // ---- menus ----

    // Menus are built on opening; WinForms cancels opening an empty menu unless told otherwise.
    private bool BuildSlotMenu()
    {
        _slotMenu.Items.Clear();
        if (_slotMenu.SourceControl is not SlotControl slot) return false;
        _slotMenu.Items.Add(Loc.T("槽位 {0}", slot.Number)).Enabled = false;
        _slotMenu.Items.Add(new ToolStripSeparator());
        _slotMenu.Items.Add(Loc.T("選擇視窗…"), null, (_, _) => PickerRequested?.Invoke(slot.Number));
        _slotMenu.Items.Add(Loc.T("編輯比對規則…"), null, (_, _) => EditRule(slot));
        _slotMenu.Items.Add(Loc.T("清除槽位"), null, (_, _) => ClearSlot(slot.Number - 1)).Enabled = slot.Source != 0 || slot.Model.Match is not null;
        _slotMenu.Items.Add(new ToolStripSeparator());
        if (Editing)
            _slotMenu.Items.Add(new ToolStripMenuItem(Loc.T("刪除這個槽位"), null, (_, _) => _edit.Remove(slot)) { ShortcutKeyDisplayString = "Del" });
        _slotMenu.Items.Add(EditToggleItem());
        return true;
    }

    private ToolStripMenuItem EditToggleItem()
    {
        string? blocked = EditBlockedText;
        return new ToolStripMenuItem(blocked ?? (Editing ? Loc.T("完成編輯") : Loc.T("編輯槽位位置…")), null, (_, _) => SetEditing(!Editing))
            { Enabled = blocked is null };
    }

    private ContextMenuStrip BuildDockMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, e) =>
        {
            e.Cancel = false;
            _dockMenuPoint = _dock.PointToClient(Cursor.Position);
            menu.Items.Clear();
            if (Editing) menu.Items.Add(Loc.T("在這裡新增槽位"), null, (_, _) => _edit.AddSlotAt(_dockMenuPoint));
            menu.Items.Add(EditToggleItem());
            menu.Items.Add(Loc.T("選擇視窗…"), null, (_, _) => PickerRequested?.Invoke(null));
            menu.Items.Add(Loc.T("設定…"), null, (_, _) => SettingsRequested?.Invoke());
        };
        return menu;
    }

    private void EditRule(SlotControl slot)
    {
        var windows = _enumerator.Enumerate();
        using var form = new MatchRuleForm(slot.Number, slot.Model.Match, windows);
        if (form.ShowDialog() != DialogResult.OK) return;
        slot.Model.Match = form.Rule;
        bool stillMatches = slot.Source == 0 || form.Rule is null
            || windows.FirstOrDefault(w => w.Hwnd == slot.Source) is not { } bound || WindowMatcher.IsMatch(form.Rule, bound);
        if (!stillMatches) _dock.Unbind(slot.Number - 1); // the new rule names another window: re-bind by rule
        LayoutChanged?.Invoke();
        AutoBind();
    }

    public void Dispose()
    {
        _bindTimer.Dispose();
        _displayTimer.Dispose();
        _foreground.Dispose();
        _edit.End();
        _wall.RestoreAll(Layout);
        _wall.Dispose();
        _dock.Close();
        _dock.Dispose();
        _slotMenu.Dispose();
    }
}

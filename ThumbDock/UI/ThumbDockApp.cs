using ThumbDock.Core;
using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.UI;

/// <summary>
/// App lifetime: loads settings and the last layout, owns the tray menu (settings, exit), the picker window's
/// menu bar (layouts, view, launch, settings), hotkeys and the window picker, and saves layouts as they change (FR-5, FR-7).
/// </summary>
internal sealed class ThumbDockApp : ApplicationContext
{
    private const int SaveDelayMs = 800, LaunchStartDelayMs = 1500;

    private readonly LayoutStore _store;
    private readonly InstanceGuard _guard;
    private readonly WindowEnumerator _enumerator = new(new VirtualDesktop(), new DesktopNames());
    private readonly AppSettings _settings;
    private readonly DockSession _session;
    private readonly TrayIcon _tray;
    private readonly HotkeyService _hotkeys = new();
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = SaveDelayMs };
    private readonly System.Windows.Forms.Timer _launchTimer = new() { Interval = LaunchStartDelayMs };
    private readonly LaunchRunner _launch;
    private readonly bool _noLaunch;
    private readonly bool _safe;
    private readonly HotEdgeWatcher _hotEdge;
    private WindowPickerForm? _picker;

    public ThumbDockApp(StartupPlan plan, LayoutStore store, InstanceGuard guard)
    {
        _store = store;
        _guard = guard;
        _settings = plan.Settings;
        var layout = plan.Layout;
        bool firstRun = plan.FirstRun;
        _safe = plan.Safe;
        _tray = new TrayIcon((Screen.AllScreens.Length > 1 ? $"ThumbDock · {plan.Label}" : "ThumbDock") + (_safe ? Loc.T("（安全模式）") : ""));
        _session = new DockSession(_enumerator, layout);
        _session.LayoutChanged += MarkDirty;
        _session.Notice += text => _tray.Notify(text);
        _session.PickerRequested += ShowPicker;
        _session.SettingsRequested += ShowSettings;
        _session.DockRebuilt += ResetPicker;
        _saveTimer.Tick += (_, _) => SaveNow();
        _tray.MenuOpening += BuildTrayMenu;
        _tray.DoubleClicked += () => ShowPicker(null);
        _hotEdge = new HotEdgeWatcher(_session.WallMonitorBounds) { Enabled = _settings.HotEdgeWall };
        _hotEdge.Triggered += _session.ReturnToWallFromEdge;
        _hotkeys.ToggleDockPressed += _session.ToggleByHotkey;
        _hotkeys.SlotPressed += _session.ActivateSlot;
        ApplyHotkeys();
        SaveNow();
        AppLog.Info($"started with layout '{layout.Name}'");
        _noLaunch = plan.NoLaunch;
        _launch = new LaunchRunner(_session, _enumerator);
        _launch.Notice += text => _tray.Notify(text);
        _launchTimer.Tick += (_, _) => { _launchTimer.Stop(); StartLaunch(manual: false); };
        if (_settings.AllowLaunch && !_noLaunch && !firstRun) _launchTimer.Start(); // after the dock is up and auto-bind has run
        else if (_noLaunch) AppLog.Info("launch disabled by --no-launch");
        if (_safe) _tray.Notify(Loc.T("安全模式：使用內建「{0}」佈局（左邊 1×8、寬 200px），本次不呼叫程式。", DefaultLayouts.ReservedName));
        if (firstRun) ShowPicker(null);
        if (!_settings.HideChromeTip && !_safe) ScheduleChromeTip();
    }

    // First-use reminder about Chrome's occlusion flags (thumbnails of covered / other-desktop Chrome windows freeze).
    private void ScheduleChromeTip()
    {
        void Show(object? s, EventArgs e)
        {
            Application.Idle -= Show;
            using var dlg = new ChromeTipForm();
            dlg.ShowDialog();
            if (dlg.DontShowAgain) { _settings.HideChromeTip = true; _store.SaveSettings(_settings); }
        }
        Application.Idle += Show;
    }

    // ---- launching programs (config/launch/<layout>.json) ----

    private bool LaunchAllowed => _settings.AllowLaunch && !_noLaunch;

    // Fire and forget on the UI thread; every failure ends up in the log, never as an unhandled exception.
    private async void StartLaunch(bool manual)
    {
        try
        {
            if (!LaunchAllowed || _launch.Running) return;
            var (config, warnings, error) = _store.LoadLaunch(_session.Layout.Name);
            foreach (string w in warnings) AppLog.Error("launch config: " + w);
            if (error is not null) { AppLog.Error(error); _tray.Notify(error, ToolTipIcon.Warning); return; }
            if (warnings.Count > 0) _tray.Notify(string.Join("\r\n", warnings.Take(3)), ToolTipIcon.Warning);
            if (config is null) { if (manual) _tray.Notify(Loc.T("這個佈局還沒有呼叫設定檔。")); return; }
            if (config.Launch.Count == 0) { if (manual) _tray.Notify(Loc.T("呼叫設定檔裡沒有任何程式（launch 是空的）。")); return; }
            await _launch.RunAsync(config);
        }
        catch (Exception ex)
        {
            AppLog.Error("launch crashed", ex);
        }
    }

    private void FillLaunchMenu(ToolStripMenuItem root)
    {
        bool hasFile = _store.LaunchFileExists(_session.Layout.Name);
        string state = _noLaunch ? Loc.T("（本次以 --no-launch 啟動）")
            : !_settings.AllowLaunch ? Loc.T("（請先在「設定」啟用）")
            : !hasFile ? Loc.T("（沒有設定檔）")
            : _launch.Running ? Loc.T("（進行中）") : "";
        root.DropDownItems.Add(new ToolStripMenuItem(Loc.T("現在呼叫這個佈局的程式") + state, null, (_, _) => StartLaunch(manual: true))
            { Enabled = LaunchAllowed && hasFile && !_launch.Running });
        root.DropDownItems.Add(Loc.T("編輯呼叫設定檔…"), null, (_, _) => EditLaunchConfig());
    }

    // Field-based editor first; "edit in Notepad" stays available from inside it (and for files the editor cannot parse).
    private void EditLaunchConfig()
    {
        var (config, _, error) = _store.LoadLaunch(_session.Layout.Name);
        if (error is not null)
        {
            var answer = MessageBox.Show(Loc.T("{0}\r\n\r\n無法用表單編輯，要改用記事本修正嗎？", error), Loc.T("呼叫設定檔"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes) EditLaunchFile();
            return;
        }
        config ??= new LaunchConfig();
        int slotCount = _session.Layout.Slots.Count;
        using var form = new LaunchEditorForm(_session.Layout.Name, config, slotCount, _enumerator.Enumerate);
        var result = form.ShowDialog();
        if (form.WantNotepad) { EditLaunchFile(); return; }
        if (result != DialogResult.OK) return;
        var edited = form.Result;
        edited.Normalize();
        if (_store.SaveLaunch(_session.Layout.Name, edited))
            _tray.Notify(LaunchAllowed ? Loc.T("呼叫設定檔已儲存。") : Loc.T("呼叫設定檔已儲存（目前未啟用「允許依呼叫設定檔呼叫程式」）。"));
        else
            _tray.Notify(Loc.T("無法儲存呼叫設定檔，詳情見 logs\\thumbdock.log。"), ToolTipIcon.Warning);
    }

    private void FillViewMenu(ToolStripMenuItem root)
    {
        string? blocked = _session.EditBlockedText;
        root.DropDownItems.Add(new ToolStripMenuItem(blocked ?? Loc.T("編輯槽位位置"), null, (_, _) => _session.SetEditing(!_session.Editing))
            { Checked = _session.Editing, Enabled = blocked is null });
        root.DropDownItems.Add(new ToolStripMenuItem(Loc.T("顯示縮圖區"), null, (_, _) => _session.SetZoneVisible(!_session.ZoneVisible)) { Checked = _session.ZoneVisible });
    }

    // Opens the layout's launch file in Notepad, creating a starter file first.
    private void EditLaunchFile()
    {
        if (_store.EnsureLaunchTemplate(_session.Layout.Name) is not { } path)
        {
            _tray.Notify(Loc.T("無法建立呼叫設定檔，詳情見 logs\\thumbdock.log。"),ToolTipIcon.Warning);
            return;
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Error("open launch file failed", ex);
            _tray.Notify(Loc.T("無法開啟記事本，請手動編輯：{0}", path),ToolTipIcon.Warning);
        }
    }

    /// <summary>Best-effort cleanup for crashes and unexpected exits; safe from any thread.</summary>
    public void EmergencyCleanup() => _session.EmergencyCleanup();

    // ---- saving ----

    private void MarkDirty()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        var layout = _session.Layout;
        if (!_guard.TryClaimLayout(layout.Name)) AppLog.Error($"layout '{layout.Name}' is also used by another instance");
        layout.Monitors = MonitorService.Signatures();
        _store.Save(layout);
        // A safe start must not make "預設" the layout the next normal start opens.
        if (!(_safe && DefaultLayouts.IsReserved(layout.Name))) _settings.LastLayout = layout.Name;
        _store.SaveSettings(_settings);
    }

    // ---- tray menu ----

    // The tray menu is deliberately minimal; everything else lives in the picker window's menu bar.
    private void BuildTrayMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add(Loc.T("選擇視窗"), null, (_, _) => ShowPicker(null));
        menu.Items.Add(Loc.T("設定…"), null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("結束"), null, (_, _) => Exit());
        // A last, inert item: it makes the menu taller so 設定／結束 are lifted clear of the taskbar edge.
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem($"ThumbDock {AppInfo.Version}"+"\n"+"Ethan Yu（大E）") { Enabled = false });
    }

    private void FillLayoutsMenu(ToolStripMenuItem root)
    {
        foreach (string name in _store.ListNames())
        {
            string n = name;
            bool free = _guard.IsLayoutAvailable(n);
            root.DropDownItems.Add(new ToolStripMenuItem(free ? n : Loc.T("{0}（其他實例使用中）", n), null, (_, _) => SwitchTo(n))
                { Checked = n == _session.Layout.Name, Enabled = free && !SettingsOpen });
        }
        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add(Loc.T("新增空白佈局…"), null, (_, _) => NewLayout());
        root.DropDownItems.Add(Loc.T("另存為新佈局…"), null, (_, _) => SaveAs());
        root.DropDownItems.Add(Loc.T("重新命名目前佈局…"), null, (_, _) => Rename());
        root.DropDownItems.Add(Loc.T("刪除目前佈局…"), null, (_, _) => DeleteCurrent());
        if (SettingsOpen)
        {
            foreach (ToolStripItem item in root.DropDownItems) item.Enabled = false;
            root.DropDownItems.Add(new ToolStripSeparator());
            root.DropDownItems.Add(new ToolStripMenuItem(Loc.T("設定視窗開啟中，請先關閉才能切換或管理佈局")) { Enabled = false });
        }
    }

    // ---- layout management (FR-5) ----

    private void SwitchTo(string name)
    {
        if (LayoutLockedNotice()) return;
        if (name == _session.Layout.Name) return;
        if (!_guard.IsLayoutAvailable(name)) { _tray.Notify(Loc.T("佈局「{0}」正被另一個 ThumbDock 使用。", name), ToolTipIcon.Warning); return; }
        SaveNow();
        if (_store.Load(name) is not { } layout) { _tray.Notify(Loc.T("無法讀取佈局「{0}」。", name), ToolTipIcon.Warning); return; }
        _launch.Cancel(); // a run for the old layout stops by itself; the new layout's run starts below
        _session.Load(layout);
        SaveNow();
        // Work-mode switching: bring up the programs of the new layout. The timer gives auto-bind time to re-attach
        // windows that are already open, so only the missing ones get started.
        if (LaunchAllowed && _settings.LaunchOnSwitch && _store.LaunchFileExists(name)) { _launchTimer.Stop(); _launchTimer.Start(); }
    }

    private void NewLayout()
    {
        if (LayoutLockedNotice()) return;
        if (AskNewName(Loc.T("新增空白佈局"), "") is not { } name) return;
        SaveNow();
        var cur = _session.Layout;
        int count = cur.Grid is { } g && g.Count == cur.Slots.Count ? g.Count : DefaultLayouts.DefaultSlotCount;
        var layout = DefaultLayouts.Create(name, count);
        CopyZoneSettings(cur, layout);
        _session.Load(layout);
        SaveNow();
    }

    // The current layout file stays as last saved; work continues under the new name.
    private void SaveAs()
    {
        if (LayoutLockedNotice()) return;
        if (AskNewName(Loc.T("另存為新佈局"), Loc.T("{0} 複本", _session.Layout.Name)) is not { } name) return;
        SaveNow();
        _session.Layout.Name = name;
        SaveNow();
    }

    private void Rename()
    {
        if (LayoutLockedNotice()) return;
        string old = _session.Layout.Name;
        if (DefaultLayouts.IsReserved(old)) { _tray.Notify(Loc.T("「{0}」是固定佈局，不能重新命名。", DefaultLayouts.ReservedName), ToolTipIcon.Warning); return; }
        if (AskNewName(Loc.T("重新命名佈局"), old) is not { } name) return;
        _session.Layout.Name = name;
        SaveNow();
        if (LayoutStore.FileNameFor(old) != LayoutStore.FileNameFor(name)) _store.Delete(old);
    }

    private void DeleteCurrent()
    {
        if (LayoutLockedNotice()) return;
        if (DefaultLayouts.IsReserved(_session.Layout.Name)) { _tray.Notify(Loc.T("「{0}」是固定佈局，不能刪除。", DefaultLayouts.ReservedName), ToolTipIcon.Warning); return; }
        var others = _store.ListNames().Where(n => n != _session.Layout.Name && _guard.IsLayoutAvailable(n)).ToList();
        if (others.Count == 0) { MessageBox.Show(Loc.T("沒有其他可切換的佈局（至少要保留一個，且不能是其他實例使用中的）。"), "ThumbDock"); return; }
        string name = _session.Layout.Name;
        if (MessageBox.Show(Loc.T("確定要刪除佈局「{0}」嗎？", name),"ThumbDock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _saveTimer.Stop();
        _store.Delete(name);
        _session.Load(_store.Load(others[0]) ?? DefaultLayouts.Create(others[0]));
        SaveNow();
    }

    private string? AskNewName(string title, string initial)
    {
        string? name = InputDialog.Ask(title, Loc.T("佈局名稱："), initial);
        if (name is null || name == _session.Layout.Name) return null;
        if (DefaultLayouts.IsReserved(name)) { _tray.Notify(Loc.T("「{0}」是保留名稱（安全啟動用的固定佈局），請改用其他名稱。", DefaultLayouts.ReservedName), ToolTipIcon.Warning); return null; }
        if (!_guard.IsLayoutAvailable(name)) { _tray.Notify(Loc.T("佈局「{0}」正被另一個 ThumbDock 使用。", name), ToolTipIcon.Warning); return null; }
        if (_store.Exists(name) &&
            MessageBox.Show(Loc.T("佈局「{0}」已存在，要覆蓋嗎？", name), "ThumbDock", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return null;
        return name;
    }

    private static void CopyZoneSettings(LayoutModel from, LayoutModel to)
    {
        to.Mode = from.Mode;
        to.FocusMonitor = from.FocusMonitor;
        to.ThumbMonitor = from.ThumbMonitor;
        to.ThumbZone = new ThumbZone { Type = from.ThumbZone.Type, Edge = from.ThumbZone.Edge, SizePx = from.ThumbZone.SizePx };
        to.ShowTitles = from.ShowTitles;
        to.CrossDesktop = from.CrossDesktop;
        to.AutoRestoreMinimized = from.AutoRestoreMinimized;
        to.RestoreOnReturn = from.RestoreOnReturn;
        to.Grid = from.Grid?.Clone();
        DefaultLayouts.Arrange(to.Slots, to.ThumbZone.Type, to.ThumbZone.Edge);
        ArrangeGrid(to);
    }

    // Applies the layout's n×m grid to its slots (list order). False when it has no grid or the slot count differs.
    private static bool ArrangeGrid(LayoutModel layout)
    {
        if (layout.Grid is not { } grid) return false;
        var screen = MonitorService.ScreenFor(layout.ThumbMonitor) ?? MonitorService.Primary;
        var center = new Point(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y + screen.Bounds.Height / 2);
        int dpi = User32.GetDpiAt(center);
        return GridPlanner.Arrange(layout.Slots, grid, ZonePixels(layout.ThumbZone, screen, dpi), dpi);
    }

    // Approximate pixel size of the thumbnail zone: the whole monitor for fill, a strip along the work area for docks.
    private static Size ZonePixels(ThumbZone zone, Screen screen, int dpi)
    {
        if (zone.Type == ZoneType.Fill) return screen.Bounds.Size;
        var area = screen.WorkingArea;
        bool vertical = zone.Edge is DockEdge.Left or DockEdge.Right;
        int thickness = SlotGeometry.DockThickness(zone.SizePx, dpi, vertical ? screen.Bounds.Width : screen.Bounds.Height);
        return vertical ? new Size(thickness, area.Height) : new Size(area.Width, thickness);
    }

    // ---- settings (FR-6, FR-7) ----

    private SettingsForm? _settingsForm;

    /// <summary>The settings window is open: layout switching and slot-position editing are locked until it closes.</summary>
    private bool SettingsOpen => _settingsForm is not null;

    // Layout-changing actions must not run while the settings window is open: OK would apply to the wrong layout.
    private bool LayoutLockedNotice()
    {
        if (!SettingsOpen) return false;
        _tray.Notify(Loc.T("設定視窗開啟中，請先關閉設定視窗才能切換或管理佈局。"),ToolTipIcon.Warning);
        return true;
    }

    // Non-modal, one at a time: the picker stays usable beside it. Applied when the window closes with OK.
    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false } open) { open.Activate(); open.BringToFront(); return; }
        _session.SetEditing(false); // a re-arrange on OK would overwrite manual positions
        _session.EditLocked = true;
        var layout = _session.Layout;
        var form = new SettingsForm(layout, _settings.Hotkeys, LiveBoundIds, _settings.AllowLaunch, _settings.LaunchOnSwitch, _noLaunch, _settings.HotEdgeWall);
        _settingsForm = form;
        form.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            _session.EditLocked = false;
            var result = form.DialogResult == DialogResult.OK ? form.Result : null;
            bool languageChanged = form.LanguageChanged;
            form.Dispose();
            if (languageChanged) _tray.Notify(Loc.T("語言已變更，請結束並重新啟動 ThumbDock 後生效。"));
            if (result is not null) ApplySettings(layout.Name, result);
        };
        form.Show();
    }

    private void ApplySettings(string openedFor, SettingsResult r)
    {
        // Second line of defence behind the menu locks: never apply to a different layout than the one the window was opened for.
        if (_session.Layout.Name != openedFor)
        {
            _tray.Notify(Loc.T("佈局已從「{0}」變更，設定未套用。", openedFor),ToolTipIcon.Warning);
            return;
        }
        if (!_guard.TryClaimMonitors(MonitorsFor(r), out string? conflict))
        {
            _tray.Notify(Loc.T("螢幕 {0} 已有另一個 ThumbDock 在使用，設定未變更。", conflict),ToolTipIcon.Warning);
            return;
        }
        var layout = _session.Layout;
        var oldZone = layout.ThumbZone;
        string oldMonitor = layout.ThumbMonitor ?? r.ThumbMonitor; // null = primary, not a change
        string oldShape = DefaultLayouts.ShapeOf(layout.ThumbZone.Type, layout.ThumbZone.Edge);
        layout.Mode = r.Mode;
        layout.ThumbMonitor = r.ThumbMonitor;
        layout.FocusMonitor = r.FocusMonitor;
        layout.ThumbZone = new ThumbZone { Type = r.Type, Edge = r.Edge, SizePx = r.SizePx };
        layout.ShowTitles = r.ShowTitles;
        layout.CrossDesktop = r.CrossDesktop;
        layout.AutoRestoreMinimized = r.AutoRestoreMinimized;
        layout.RestoreOnReturn = r.RestoreOnReturn;
        int oldCount = layout.Slots.Count;
        // A layout without grid settings counts as its current shape, so opening the dialog and pressing OK never rearranges it.
        var oldGrid = layout.Grid is { } og && og.Count == oldCount ? og : GridPlanner.Guess(oldCount, oldZone.Type, oldZone.Edge);
        layout.Grid = r.Grid;
        bool shapeChanged = DefaultLayouts.ShapeOf(r.Type, r.Edge) != oldShape;
        if (shapeChanged) DefaultLayouts.Arrange(layout.Slots, r.Type, r.Edge);
        _settings.Hotkeys = r.Hotkeys;
        _settings.HotEdgeWall = r.HotEdge;
        _hotEdge.Enabled = r.HotEdge;
        if (!_noLaunch) { _settings.AllowLaunch = r.AllowLaunch; _settings.LaunchOnSwitch = r.LaunchOnSwitch; }
        if (!ApplySlotCount(r))
        {
            if (layout.Grid is { } grid)
            {
                // Re-arrange only when the n×m settings, the count or the zone changed, so manual fine-tuning survives an OK click.
                bool changed = !grid.SameAs(oldGrid) || layout.Slots.Count != oldCount || shapeChanged ||
                               oldZone.Type != r.Type || oldZone.SizePx != r.SizePx || oldMonitor != r.ThumbMonitor;
                bool blocked = layout.Slots.Count != grid.Count;
                bool arranged = false;
                if (blocked) layout.Grid = oldGrid; // shrink was blocked
                else if (changed) arranged = ArrangeGrid(layout);
                AppLog.Info($"grid: old {oldGrid.Cols}x{oldGrid.Rows} new {grid.Cols}x{grid.Rows} slots {oldCount}->{layout.Slots.Count} changed={changed} blocked={blocked} arranged={arranged}");
            }
            _session.Rebuild();
        }
        _session.RestoreMinimizedNow();
        ApplyHotkeys();
        SaveNow();
    }

    // Monitors this instance needs after the dialog: dual mode takes every monitor, single mode just its own.
    private static List<string> MonitorsFor(SettingsResult r) =>
        r.Mode == LayoutMode.Dual ? Screen.AllScreens.Select(s => s.DeviceName).ToList() : [r.ThumbMonitor];

    private HashSet<int> LiveBoundIds() =>
        _session.Dock.Slots.Where(s => s.Source != 0).Select(s => s.Model.Id).ToHashSet();

    /// <summary>
    /// Applies the slot count from the settings dialog (growing, shrinking, or reset-all-bindings).
    /// Returns true when the dock was already rebuilt by it.
    /// </summary>
    private bool ApplySlotCount(SettingsResult r)
    {
        var layout = _session.Layout;
        int before = layout.Slots.Count;
        if (r.ResetBindings && r.SlotCount < before)
        {
            var fresh = DefaultLayouts.Create(layout.Name, r.SlotCount);
            CopyZoneSettings(layout, fresh);
            _session.Load(fresh);
            AppLog.Info($"slot count {before} -> {r.SlotCount}: all bindings cleared");
            return true;
        }
        var plan = SlotCountPlanner.Plan(layout.Slots, LiveBoundIds(), r.SlotCount);
        switch (plan.Action)
        {
            case SlotCountAction.Add:
                if (SlotCountPlanner.AddSlots(layout.Slots, r.SlotCount, layout.ThumbZone.Type, layout.ThumbZone.Edge))
                    _tray.Notify(Loc.T("縮圖區沒有足夠的空位，已重新排列所有槽位（綁定不變）。"));
                break;
            case SlotCountAction.Remove:
                layout.Slots.RemoveAll(s => plan.RemoveIds.Contains(s.Id));
                break;
            case SlotCountAction.Blocked:
                _tray.Notify(Loc.T("槽位數量未變更：請先解除至少 {0} 個槽位的綁定。", plan.MissingUnbinds),ToolTipIcon.Warning);
                return false;
        }
        if (plan.Action is SlotCountAction.Add or SlotCountAction.Remove)
            AppLog.Info($"slot count {before} -> {layout.Slots.Count}");
        return false;
    }

    private void ApplyHotkeys()
    {
        if (!_guard.IsMain)
        {
            AppLog.Info("hotkeys skipped: another instance owns them");
            return;
        }
        var failed = _hotkeys.Apply(_settings.Hotkeys);
        if (failed.Count > 0) _tray.Notify(Loc.T("以下快速鍵已被其他程式使用，無法註冊：{0}", string.Join(Loc.T("、"), failed)),ToolTipIcon.Warning);
    }

    // ---- picker ----

    private void ShowPicker(int? slotNumber)
    {
        _picker ??= new WindowPickerForm(_session.Dock, _enumerator, _session, BuildPickerMenus(), ShowSettings);
        _picker.ShowFor(slotNumber);
    }

    private IReadOnlyList<PickerMenuSpec> BuildPickerMenus() =>
    [
        new(Loc.T("佈局"), FillLayoutsMenu),
        new(Loc.T("檢視"), FillViewMenu),
        new(Loc.T("啟動時執行"), FillLaunchMenu),
    ];

    // The picker's columns follow the dock's slots, so recreate it when the dock is rebuilt.
    // A layout switch comes from the picker's own menu, so the old form is disposed after its handler returns.
    private void ResetPicker()
    {
        if (_picker is not { } old) return;
        bool wasVisible = old.Visible;
        _picker = null;
        void Replace()
        {
            old.Dispose();
            if (wasVisible) ShowPicker(null);
        }
        if (old.IsHandleCreated) old.BeginInvoke(Replace);
        else Replace();
    }

    private void Exit()
    {
        AppLog.Info("exit requested");
        _launch.Cancel();
        _launchTimer.Dispose();
        _session.SetEditing(false);
        SaveNow();
        _hotkeys.Dispose();
        _hotEdge.Dispose();
        _tray.Dispose();
        _picker?.Dispose();
        _session.Dispose();
        _saveTimer.Dispose();
        ExitThread();
    }
}

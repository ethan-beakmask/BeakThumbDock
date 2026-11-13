using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.UI;

internal sealed record SettingsResult(
    LayoutMode Mode, string ThumbMonitor, string FocusMonitor, ZoneType Type, DockEdge Edge,
    int SizePx, bool ShowTitles, CrossDesktop CrossDesktop, bool AutoRestoreMinimized, HotkeySettings Hotkeys,
    int SlotCount, bool ResetBindings, bool AllowLaunch, bool LaunchOnSwitch, bool RestoreOnReturn,
    GridSpec? Grid, bool HotEdge);

/// <summary>Settings dialog: monitor/zone settings of the current layout plus app-wide hotkeys.</summary>
internal sealed class SettingsForm : Form
{
    private static readonly string[] ModifierTokens = ["Ctrl", "Alt", "Shift", "Win"];

    private readonly ComboBox _mode = Combo(Loc.T("單螢幕"), Loc.T("雙螢幕"));
    private readonly ComboBox _thumb = Combo();
    private readonly ComboBox _focus = Combo();
    private readonly CheckBox _restoreOnReturn = new() { Text = Loc.T("回到縮圖牆時，把縮圖牆最大化的視窗還原成原本的大小與位置（避免被蓋住的瀏覽器停止更新）"), AutoSize = true };
    private readonly ComboBox _edge = Combo(Loc.T("左（靠邊，像工作列）"), Loc.T("右（靠邊）"), Loc.T("上（靠邊）"), Loc.T("下（靠邊）"), Loc.T("全螢幕（縮圖牆 / fill）"));
    private readonly NumericUpDown _size = new() { Minimum = 80, Maximum = GridSpec.MaxPx, Increment = 10, Width = 90 };
    private readonly NumericUpDown _cols = new() { Minimum = 1, Maximum = GridSpec.MaxCells, Width = 60 };
    private readonly NumericUpDown _rows = new() { Minimum = 1, Maximum = GridSpec.MaxCells, Width = 60 };
    private readonly NumericUpDown _gap = new() { Minimum = 0, Maximum = GridSpec.MaxGapPx, Width = 60 };
    private readonly CheckBox _fixedSize = new() { Text = Loc.T("固定縮圖大小（放不下時等比例縮小）"), AutoSize = true };
    private readonly NumericUpDown _cellW = new() { Minimum = 1, Maximum = GridSpec.MaxPx, Value = 320, Increment = 10, Width = 70 };
    private readonly NumericUpDown _cellH = new() { Minimum = 1, Maximum = GridSpec.MaxPx, Value = 180, Increment = 10, Width = 70 };
    private readonly CheckBox _resetBindings = new() { Text = Loc.T("縮減時：全部解除綁定並重新排列（等同同名的全新佈局）"), AutoSize = true };
    private readonly CheckBox _titles = new() { Text = Loc.T("槽位下方顯示視窗標題"), AutoSize = true };
    private readonly ComboBox _crossDesktop = Combo(Loc.T("切換到視窗所在的桌面（switch）"), Loc.T("把視窗搬到目前的桌面（pull）"));
    private readonly CheckBox _autoRestore = new() { Text = Loc.T("來源視窗被最小化時自動還原到最底層（縮圖持續更新，不會蓋住其他視窗）"), AutoSize = true };
    private readonly CheckBox _hotkeysOn = new() { Text = Loc.T("啟用全域快速鍵"), AutoSize = true };
    private readonly HotkeyBox _toggleKey = new() { Width = 180 };
    // Slot hotkey modifiers: first box is required, the other two may be "(none)". Stored as e.g. "Alt+Shift".
    private readonly ComboBox[] _slotMods = [SlotModCombo(false), SlotModCombo(true), SlotModCombo(true)];
    private readonly FlowLayoutPanel _slotModsPanel = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    private readonly Label _slotModsWarn = new() { Text = Loc.T("只用一個修飾鍵，容易與其他程式的快速鍵衝突。"), AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(420, 0) };
    private bool _slotModsSync;
    private readonly List<Screen> _screens = Screen.AllScreens.ToList();
    private Panel _content = null!;
    private TableLayoutPanel _table = null!;
    private readonly LayoutModel _layout;
    private readonly bool _launchForcedOff;
    private readonly CheckBox _allowLaunch = new() { Text = Loc.T("允許 ThumbDock 啟動時依「呼叫設定檔」呼叫程式（含系統匣選單）"), AutoSize = true };
    private readonly CheckBox _launchOnSwitch = new() { Text = Loc.T("切換佈局時，也依該佈局的呼叫設定檔備妥程式（已開啟的不會關閉或重複開啟）"), AutoSize = true };
    private readonly Func<IReadOnlySet<int>> _boundIds;
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly List<string> _languageCodes = [];
    private readonly CheckBox _hotEdge = new() { Text = Loc.T("滑鼠停在螢幕左緣中間約 0.4 秒時，回到縮圖牆（只對單螢幕的縮圖牆有效；拖曳視窗時不觸發）"), AutoSize = true };

    /// <param name="boundIds">Ids of slots currently showing a window (slots with a rule count as bound too); read when needed, because bindings may change while this non-modal window is open.</param>
    public SettingsForm(LayoutModel layout, HotkeySettings hotkeys, Func<IReadOnlySet<int>> boundIds, bool allowLaunch, bool launchOnSwitch, bool launchForcedOff, bool hotEdge)
    {
        _hotEdge.Checked = hotEdge;
        _layout = layout;
        _launchForcedOff = launchForcedOff;
        _allowLaunch.Checked = allowLaunch && !launchForcedOff;
        _launchOnSwitch.Checked = launchOnSwitch;
        _boundIds = boundIds;
        Text = Loc.T("ThumbDock {0} 設定", AppInfo.Version);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = MaximizeBox = false;
        TopMost = true;
        foreach (var s in _screens) { _thumb.Items.Add(MonitorService.Describe(s)); _focus.Items.Add(MonitorService.Describe(s)); }
        _thumb.Width = _focus.Width = _edge.Width = _crossDesktop.Width = 300;
        LoadValues(layout, hotkeys);
        LoadLanguages();
        // The content scrolls and the buttons stay visible, so the dialog fits small screens and high DPI scaling.
        _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _table = BuildLayout(layout.Name);
        _content.Controls.Add(_table);
        Controls.Add(_content); // Fill first, then the bottom bar (docking is applied from the last control backwards)
        Controls.Add(Buttons());
        _lastEdgeHorizontal = _edge.SelectedIndex is 2 or 3;
        _edge.SelectedIndexChanged += (_, _) => { FlipGridWithEdge(); UpdateSizeLimit(); UpdateEnabled(); };
        _thumb.SelectedIndexChanged += (_, _) => UpdateSizeLimit();
        _cols.ValueChanged += (_, _) => UpdateEnabled();
        _rows.ValueChanged += (_, _) => UpdateEnabled();
        _fixedSize.CheckedChanged += (_, _) => UpdateEnabled();
        _mode.SelectedIndexChanged += (_, _) => UpdateEnabled();
        _hotkeysOn.CheckedChanged += (_, _) => UpdateEnabled();
        UpdateSizeLimit();
        UpdateEnabled();
    }

    public SettingsResult? Result { get; private set; }

    // Fit the content, but never taller than the screen's working area.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var content = _table.GetPreferredSize(Size.Empty);
        int buttons = Controls.OfType<FlowLayoutPanel>().First().Height;
        int chrome = Height - ClientSize.Height;
        int width = content.Width + SystemInformation.VerticalScrollBarWidth + 4;
        int height = Math.Min(content.Height + buttons, area.Height - chrome - 20);
        ClientSize = new Size(Math.Min(width, area.Width - 40), height);
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + Math.Max(0, (area.Height - Height) / 2));
    }

    private void LoadValues(LayoutModel layout, HotkeySettings hotkeys)
    {
        _mode.SelectedIndex = layout.Mode == LayoutMode.Dual ? 1 : 0;
        _thumb.SelectedIndex = IndexOf(layout.ThumbMonitor, primaryFallback: true);
        _focus.SelectedIndex = IndexOf(layout.FocusMonitor, primaryFallback: false);
        _restoreOnReturn.Checked = layout.RestoreOnReturn;
        _lastDockEdge = layout.ThumbZone.Edge;
        _edge.SelectedIndex = layout.ThumbZone.Type == ZoneType.Fill ? FillIndex : (int)layout.ThumbZone.Edge;
        _size.Value = Math.Clamp(layout.ThumbZone.SizePx, (int)_size.Minimum, (int)_size.Maximum);
        // Edit mode may have added or removed slots since the grid was set; then the grid no longer describes the layout.
        var grid = layout.Grid is { } g && g.Count == layout.Slots.Count ? g : GridPlanner.Guess(layout.Slots.Count, layout.ThumbZone.Type, layout.ThumbZone.Edge);
        _cols.Value = Math.Clamp(grid.Cols, 1, GridSpec.MaxCells);
        _rows.Value = Math.Clamp(grid.Rows, 1, GridSpec.MaxCells);
        _gap.Value = Math.Clamp(grid.GapPx, 0, GridSpec.MaxGapPx);
        _fixedSize.Checked = grid.IsFixedSize;
        if (grid.IsFixedSize) { _cellW.Value = grid.CellWidthPx; _cellH.Value = grid.CellHeightPx; }
        _titles.Checked = layout.ShowTitles;
        _crossDesktop.SelectedIndex = layout.CrossDesktop == CrossDesktop.Pull ? 1 : 0;
        _autoRestore.Checked = layout.AutoRestoreMinimized;
        _hotkeysOn.Checked = hotkeys.Enabled;
        _toggleKey.Text = hotkeys.ToggleDock;
        LoadSlotModifiers(hotkeys.SlotModifiers);
    }

    private void LoadSlotModifiers(string? text)
    {
        uint flags = HotkeyParser.ParseModifiers(text) ?? (HotkeyParser.Alt | HotkeyParser.Shift);
        var tokens = HotkeyParser.FormatModifiers(flags).Split('+');
        _slotModsSync = true;
        for (int i = 0; i < _slotMods.Length; i++)
        {
            int idx = i < tokens.Length ? Array.IndexOf(ModifierTokens, tokens[i]) : -1;
            _slotMods[i].SelectedIndex = i == 0 ? Math.Max(idx, 0) : idx + 1; // boxes 1–2: item 0 is "(none)"
        }
        _slotModsSync = false;
        UpdateSlotModsWarn();
    }

    private string? SlotModToken(int i) => i == 0 ? ModifierTokens[_slotMods[0].SelectedIndex] : _slotMods[i].SelectedIndex > 0 ? ModifierTokens[_slotMods[i].SelectedIndex - 1] : null;

    // Canonical string ("Ctrl+Alt+Shift+Win" order, duplicates collapsed), e.g. "Alt+Shift".
    private string SlotModifiersText()
    {
        uint flags = HotkeyParser.ParseModifiers(string.Join("+", Enumerable.Range(0, _slotMods.Length).Select(SlotModToken).OfType<string>())) ?? (HotkeyParser.Alt | HotkeyParser.Shift);
        return HotkeyParser.FormatModifiers(flags);
    }

    private void OnSlotModChanged(int changed)
    {
        if (_slotModsSync) return;
        _slotModsSync = true;
        string? token = SlotModToken(changed);
        for (int j = 0; token is not null && j < _slotMods.Length; j++)
        {
            if (j == changed || SlotModToken(j) != token) continue;
            // Same modifier twice: clear the optional box (when box 0 duplicates, clear the one just changed).
            _slotMods[j == 0 ? changed : j].SelectedIndex = 0;
        }
        _slotModsSync = false;
        UpdateSlotModsWarn();
    }

    private void UpdateSlotModsWarn() => _slotModsWarn.Visible = Enumerable.Range(0, _slotMods.Length).Count(i => SlotModToken(i) is not null) == 1;

    private static ComboBox SlotModCombo(bool optional)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Margin = new Padding(0, 0, 4, 0) };
        if (optional) c.Items.Add(Loc.T("（無）"));
        c.Items.AddRange(ModifierTokens);
        return c;
    }

    // Unknown or missing device: primary for the thumb monitor, first non-primary for the focus monitor.
    private int IndexOf(string? device, bool primaryFallback)
    {
        int i = _screens.FindIndex(s => s.DeviceName == device);
        if (i >= 0) return i;
        i = _screens.FindIndex(s => s.Primary == primaryFallback);
        return Math.Max(0, i);
    }

    private TableLayoutPanel BuildLayout(string layoutName)
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12), Dock = DockStyle.Top };
        Header(t, Loc.T("佈局「{0}」", layoutName));
        if (DefaultLayouts.IsReserved(layoutName))
            Row(t, "", new Label { Text = Loc.T("「default」是固定佈局（安全啟動使用）：模式、縮圖區位置與大小、欄×列都不可修改。要自訂請先「另存為」新佈局。"), AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(420, 0) });
        Row(t, Loc.T("模式"), _mode);
        Row(t, Loc.T("縮圖螢幕"), _thumb);
        Row(t, Loc.T("焦點螢幕"), _focus);
        Row(t, "", new Label { Text = Loc.T("單螢幕選 fill 會變成「縮圖牆」：縮圖蓋滿整個螢幕，點縮圖後該視窗顯示在牆之上，按快速鍵回到牆。雙螢幕的 fill 只佔縮圖螢幕。"), AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0) });
        Row(t, "", _restoreOnReturn);
        Row(t, "", _hotEdge);
        Row(t, Loc.T("縮圖區位置"), _edge);
        Row(t, Loc.T("寬度／高度（100% 縮放時的像素）"), _size);
        Row(t, Loc.T("欄數 n × 列數 m"), Flow(_cols, new Label { Text = "×", AutoSize = true, Margin = new Padding(3, 6, 3, 0) }, _rows,
            new Label { Text = Loc.T("＝ 槽位數"), AutoSize = true, Margin = new Padding(8, 6, 3, 0) }));
        Row(t, Loc.T("間距（100% 縮放時的像素）"), _gap);
        Row(t, "", _fixedSize);
        Row(t, Loc.T("固定寬 × 高（像素）"), Flow(_cellW, new Label { Text = "×", AutoSize = true, Margin = new Padding(3, 6, 3, 0) }, _cellH));
        Row(t, "", new Label { Text = Loc.T("欄 × 列即槽位數量，縮圖會排成大小一致、對齊、間距一致的格子（依槽位順序由左到右、由上到下，綁定保留）。靠邊型態的縮圖區較窄，例如 2 欄 × 8 列。\n只在這裡的設定（或縮圖區位置、大小）改變時重排；之後可在編輯模式手動拖曳微調，或直接拖曳縮圖互換位置。"), AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0) });
        Row(t, "", _resetBindings);
        Row(t, "", new Label { Text = Loc.T("增加：新槽位放在空位，不動既有槽位。縮減：只刪除未綁定的槽位（沒有比對規則、沒有視窗）；\n不夠刪時請先到槽位右鍵「清除槽位」解除綁定，或勾選上面的選項一次重設。"), AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0) });
        Row(t, "", _titles);
        Row(t, Loc.T("其他虛擬桌面的視窗"), _crossDesktop);
        Row(t, "", new Label { Text = Loc.T("pull 用到 Windows 未公開的介面；這個 Windows 版本不支援時，會自動改用 switch。"), AutoSize = true, ForeColor = SystemColors.GrayText });
        Row(t, "", _autoRestore);
        Header(t, Loc.T("啟動時呼叫程式"));
        _allowLaunch.Enabled = !_launchForcedOff;
        Row(t, "", _allowLaunch);
        _launchOnSwitch.Enabled = !_launchForcedOff;
        Row(t, "", _launchOnSwitch);
        Row(t, "", new Label { Text = _launchForcedOff ? Loc.T("本次以 --no-launch 啟動，已強制停用。") : Loc.T("預設關閉。設定檔在 config\\launch\\佈局名.json，可從系統匣「啟動時呼叫程式」編輯。"), AutoSize = true, ForeColor = SystemColors.GrayText });
        Header(t, Loc.T("快速鍵（所有佈局共用）"));
        Row(t, "", _hotkeysOn);
        Row(t, Loc.T("顯示／隱藏縮圖區"), _toggleKey);
        Row(t, "", new Label { Text = Loc.T("縮圖牆（單螢幕 fill）時改為：回到牆；牆已在最上層時，回到上一個視窗。"), AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0) });
        for (int i = 0; i < _slotMods.Length; i++)
        {
            int k = i;
            _slotMods[i].SelectedIndexChanged += (_, _) => OnSlotModChanged(k);
            _slotModsPanel.Controls.Add(_slotMods[i]);
        }
        _slotModsPanel.Controls.Add(new Label { Text = "+ 1–9", AutoSize = true, Anchor = AnchorStyles.Left });
        Row(t, Loc.T("切換到槽位 1–9"), _slotModsPanel);
        Row(t, "", _slotModsWarn);
        Row(t, "", new Label { Text = Loc.T("例如選 Ctrl+Alt 時，Ctrl+Alt+3 會切到槽位 3。"), AutoSize = true, ForeColor = SystemColors.GrayText });
        Header(t, Loc.T("語言"));
        Row(t, Loc.T("介面語言"), _language);
        Row(t, "", new Label { Text = Loc.T("更改語言後，重新啟動 ThumbDock 才會生效。新增語言的方法見 docs/i18n.md。"), AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0) });
        if (Loc.Warnings.Count > 0)
            Row(t, "", new Label { Text = string.Join(Environment.NewLine, Loc.Warnings), AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(420, 0) });
        return t;
    }

    /// <summary>True when OK saved a different interface language; it takes effect after a restart.</summary>
    public bool LanguageChanged { get; private set; }

    private void LoadLanguages()
    {
        string baseDir = AppContext.BaseDirectory;
        _languageCodes.Add(Loc.Auto);
        _language.Items.Add(Loc.T("自動（跟隨 Windows）"));
        foreach (var l in Loc.Available(baseDir))
        {
            _languageCodes.Add(l.Code);
            _language.Items.Add(l.Status.Length > 0 ? $"{l.Name}（{l.Status}）" : l.Name);
        }
        int index = _languageCodes.FindIndex(c => c.Equals(Loc.ReadChoice(baseDir), StringComparison.OrdinalIgnoreCase));
        _language.SelectedIndex = index >= 0 ? index : 0;
    }

    private void SaveLanguage()
    {
        string baseDir = AppContext.BaseDirectory;
        string choice = _languageCodes[Math.Max(_language.SelectedIndex, 0)];
        if (choice.Equals(Loc.ReadChoice(baseDir), StringComparison.OrdinalIgnoreCase)) return;
        Loc.SaveChoice(baseDir, choice);
        LanguageChanged = true;
    }

    private static FlowLayoutPanel Flow(params Control[] items)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        p.Controls.AddRange(items);
        return p;
    }

    private static void Header(TableLayoutPanel t, string text)
    {
        var label = new Label { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), Margin = new Padding(3, 10, 3, 4) };
        t.Controls.Add(label, 0, t.RowCount);
        t.SetColumnSpan(label, 2);
        t.RowCount++;
    }

    private static void Row(TableLayoutPanel t, string label, Control field)
    {
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, t.RowCount);
        t.Controls.Add(field, 1, t.RowCount);
        t.RowCount++;
    }

    private FlowLayoutPanel Buttons()
    {
        var ok = new Button { Text = Loc.T("確定"), AutoSize = true };
        var cancel = new Button { Text = Loc.T("取消"), DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(); // non-modal: a DialogResult button does not close the form by itself
        AcceptButton = ok;
        CancelButton = cancel;
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Bottom, Padding = new Padding(12, 6, 12, 8) };
        panel.Controls.AddRange([ok, cancel]);
        return panel;
    }

    private void UpdateEnabled()
    {
        bool dual = _mode.SelectedIndex == 1;
        _focus.Enabled = dual;
        bool dock = !IsFill;
        _size.Enabled = dock;
        _restoreOnReturn.Enabled = _hotEdge.Enabled = !dock && !dual; // only the single-monitor wall maximizes windows over itself
        _toggleKey.Enabled = _slotModsPanel.Enabled = _hotkeysOn.Checked;
        _cellW.Enabled = _cellH.Enabled = _fixedSize.Checked;
        if (DefaultLayouts.IsReserved(_layout.Name))
            _mode.Enabled = _focus.Enabled = _edge.Enabled = _size.Enabled = _cols.Enabled = _rows.Enabled = _gap.Enabled =
                _fixedSize.Enabled = _cellW.Enabled = _cellH.Enabled = false; // fixed layout used by safe mode
        int target = TargetCount();
        bool shrinking = target < _layout.Slots.Count;
        _resetBindings.Enabled = shrinking;
        if (!shrinking) _resetBindings.Checked = false;
    }

    private const int FillIndex = 4, SlowSlotCount = 24;
    private bool IsFill => _edge.SelectedIndex == FillIndex;
    private DockEdge _lastDockEdge;
    private bool _lastEdgeHorizontal;

    // A docked strip may take at most this share of the monitor (see SlotGeometry.DockThickness); the wall covers all of it.
    private void UpdateSizeLimit()
    {
        if (_screens.Count == 0 || _thumb.SelectedIndex < 0) return;
        var screen = _screens[_thumb.SelectedIndex];
        var center = new Point(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y + screen.Bounds.Height / 2);
        int dpi = Native.User32.GetDpiAt(center);
        bool horizontal = _edge.SelectedIndex is 2 or 3;
        int max = SlotGeometry.MaxDockDip(horizontal ? screen.Bounds.Height : screen.Bounds.Width, dpi);
        _size.Maximum = Math.Clamp(max, (int)_size.Minimum, GridSpec.MaxPx);
        if (_size.Value > _size.Maximum) _size.Value = _size.Maximum;
    }

    // Shrinking only deletes unbound slots; tell the user how many to unbind when there are not enough.
    // Left/right -> top/bottom turns a strip of 2 columns x 8 rows into 8 columns x 2 rows (same slot count).
    private void FlipGridWithEdge()
    {
        if (IsFill) return;
        _lastDockEdge = (DockEdge)_edge.SelectedIndex;
        bool horizontal = _edge.SelectedIndex is 2 or 3;
        if (horizontal == _lastEdgeHorizontal) return;
        _lastEdgeHorizontal = horizontal;
        (_cols.Value, _rows.Value) = (_rows.Value, _cols.Value);
    }

    private int TargetCount() => (int)(_cols.Value * _rows.Value);

    private bool SlotCountAllowed()
    {
        int target = TargetCount();
        if (target >= _layout.Slots.Count || _resetBindings.Checked) return true;
        var plan = SlotCountPlanner.Plan(_layout.Slots, _boundIds(), target);
        if (plan.Action != SlotCountAction.Blocked) return true;
        MessageBox.Show(this,
            Loc.T("要縮減到 {0} 個槽位，還需要先解除至少 {1} 個槽位的綁定。\n\n請在槽位上按右鍵選「清除槽位」，或勾選「全部解除綁定並重新排列」一次重設。", target, plan.MissingUnbinds),
            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void Accept()
    {
        bool dual = _mode.SelectedIndex == 1;
        string thumb = _screens[_thumb.SelectedIndex].DeviceName;
        string focus = dual ? _screens[_focus.SelectedIndex].DeviceName : thumb;
        if (dual && thumb == focus)
        {
            MessageBox.Show(this, Loc.T("雙螢幕模式下，焦點螢幕必須和縮圖螢幕不同。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_cols.Value * _rows.Value > GridSpec.MaxCells)
        {
            MessageBox.Show(this, Loc.T("欄數 × 列數不可超過 {0}。", GridSpec.MaxCells), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!SlotCountAllowed()) return;
        if (TargetCount() >= SlowSlotCount && TargetCount() != _layout.Slots.Count &&
            MessageBox.Show(this, Loc.T("縮圖數量 {0} 個（{1} × {2}）。{3} 個以上時，畫面更新與操作會明顯變慢。\r\n\r\n仍要套用嗎？", TargetCount(), _cols.Value, _rows.Value, SlowSlotCount),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        var hotkeys = new HotkeySettings { Enabled = _hotkeysOn.Checked, ToggleDock = _toggleKey.Text, SlotModifiers = SlotModifiersText() };
        Result = new SettingsResult(
            dual ? LayoutMode.Dual : LayoutMode.Single, thumb, focus,
            IsFill ? ZoneType.Fill : ZoneType.Dock,
            IsFill ? _lastDockEdge : (DockEdge)_edge.SelectedIndex, (int)_size.Value, _titles.Checked,
            _crossDesktop.SelectedIndex == 1 ? CrossDesktop.Pull : CrossDesktop.Switch, _autoRestore.Checked, hotkeys,
            TargetCount(), _resetBindings.Checked, _allowLaunch.Checked, _launchOnSwitch.Checked, _restoreOnReturn.Checked,
            new GridSpec
            {
                Cols = (int)_cols.Value, Rows = (int)_rows.Value, GapPx = (int)_gap.Value,
                CellWidthPx = _fixedSize.Checked ? (int)_cellW.Value : 0, CellHeightPx = _fixedSize.Checked ? (int)_cellH.Value : 0,
            }, _hotEdge.Checked);
        SaveLanguage();
        DialogResult = DialogResult.OK;
        Close();
    }

    private static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        c.Items.AddRange(items);
        return c;
    }
}

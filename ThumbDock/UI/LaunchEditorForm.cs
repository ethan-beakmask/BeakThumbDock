using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.UI;

/// <summary>
/// Field-based editor for a layout's launch config: pick a program type, fill fields, and the form builds the
/// <c>args</c> string. Entries are edited in place (<see cref="Result"/>); the caller saves them.
/// </summary>
internal sealed class LaunchEditorForm : Form
{
    private static string[] KindNames => [Loc.T("一般程式／捷徑／批次檔"), "Chrome", "Edge", Loc.T("命令提示字元 (CMD)")]; // index = (int)LaunchKind
    private static string[] CmdModes => [Loc.T("/k　執行後保持視窗開啟"), Loc.T("/c　執行後關閉視窗")];

    private readonly List<LaunchEntry> _entries;
    private readonly Dictionary<LaunchEntry, LaunchKind> _kinds = [];
    private readonly int _slotCount;
    private readonly Func<List<WindowInfo>> _windows;
    private bool _loading, _dirty;
    private List<BrowserProfile> _profiles = [];

    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Panel _fieldsHost = new() { Dock = DockStyle.Fill, AutoScroll = true, Enabled = false };
    private readonly NumericUpDown _slot = new() { Minimum = 1, Maximum = 99, Width = 70 };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _path = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly TextBox _url = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _noOcclusion = new() { Text = Loc.T("被蓋住或在其他虛擬桌面時仍持續更新（--disable-features=CalculateNativeWinOcclusion）"), AutoSize = true };
    private readonly CheckBox _noBackgrounding = new() { Text = Loc.T("不要降低被蓋住視窗的優先權（--disable-backgrounding-occluded-windows）"), AutoSize = true };
    private readonly TextBox _extraFlags = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _cmdMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _cmdCommand = new() { Dock = DockStyle.Fill };
    private readonly TextBox _args = new() { Dock = DockStyle.Fill };
    private readonly TextBox _workDir = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _delay = new() { Minimum = 0, Maximum = LaunchConfig.MaxDelayMs, Increment = 500, Width = 80 };
    private readonly NumericUpDown _wait = new() { Minimum = 1, Maximum = LaunchConfig.MaxWaitSec, Width = 60 };
    private readonly Label _matchText = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly TextBox _note = new() { Dock = DockStyle.Fill };
    private readonly TextBox _preview = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Firebrick };
    private readonly Label _testResult = new() { AutoSize = true, Anchor = AnchorStyles.Left };

    private readonly List<Control> _browserRows = [], _cmdRows = [], _generalRows = [];
    private TableLayoutPanel _table = null!;
    private int _row;

    public LaunchEditorForm(string layoutName, LaunchConfig? config, int slotCount, Func<List<WindowInfo>> windows)
    {
        _entries = config?.Launch ?? [];
        _slotCount = slotCount;
        _windows = windows;
        foreach (var e in _entries) _kinds[e] = LaunchArgs.DetectKind(e.Path, e.Args);
        Text = Loc.T("編輯呼叫設定檔 — {0}", layoutName);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(960, 640);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        _kind.Items.AddRange(KindNames);
        _cmdMode.Items.AddRange(CmdModes);
        Controls.Add(BuildRoot());
        WireEvents();
        RefreshList();
        if (_entries.Count > 0) _list.SelectedIndex = 0;
    }

    /// <summary>True when the user chose "edit in Notepad"; nothing was saved.</summary>
    public bool WantNotepad { get; private set; }

    /// <summary>The edited config (the same entry objects the form was given).</summary>
    public LaunchConfig Result => new() { Launch = _entries };

    private LaunchEntry? Current => _list.SelectedIndex >= 0 && _list.SelectedIndex < _entries.Count ? _entries[_list.SelectedIndex] : null;

    // ---- layout ----

    private Control BuildRoot()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(_list, 0, 0);
        left.Controls.Add(BuildListButtons(), 0, 1);
        root.Controls.Add(left, 0, 0);
        _fieldsHost.Controls.Add(BuildFields());
        root.Controls.Add(_fieldsHost, 1, 0);
        var bottom = BuildBottomButtons();
        root.Controls.Add(bottom, 0, 1);
        root.SetColumnSpan(bottom, 2);
        return root;
    }

    private FlowLayoutPanel BuildListButtons()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        panel.Controls.Add(Btn(Loc.T("新增"), (_, _) => AddEntry(copy: false)));
        panel.Controls.Add(Btn(Loc.T("複製"), (_, _) => AddEntry(copy: true)));
        panel.Controls.Add(Btn(Loc.T("刪除"), (_, _) => DeleteEntry()));
        panel.Controls.Add(Btn("↑", (_, _) => MoveEntry(-1)));
        panel.Controls.Add(Btn("↓", (_, _) => MoveEntry(1)));
        return panel;
    }

    private FlowLayoutPanel BuildBottomButtons()
    {
        var save = Btn(Loc.T("儲存"), (_, _) => TrySave());
        var cancel = new Button { Text = Loc.T("取消"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var notepad = Btn(Loc.T("改用記事本編輯（進階）…"), (_, _) => UseNotepad());
        CancelButton = cancel;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        panel.Controls.AddRange([cancel, save, notepad]);
        return panel;
    }

    private static Button Btn(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += click;
        return b;
    }

    private Control BuildFields()
    {
        _table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(Loc.T("槽位"), _slot);
        AddRow(Loc.T("程式類型"), _kind);
        AddRow(Loc.T("程式路徑"), WithButton(_path, Loc.T("瀏覽…"), BrowsePath));
        AddRow(Loc.T("帳號設定檔"), _profile, _browserRows);
        AddRow("", Hint(Loc.T("下拉選單來自瀏覽器的設定檔清單；讀不到時直接輸入資料夾名稱，如 Default、Profile 3。")), _browserRows);
        AddRow(Loc.T("網址"), _url, _browserRows);
        AddRow(Loc.T("旗標"), _noOcclusion, _browserRows);
        AddRow("", _noBackgrounding, _browserRows);
        AddRow(Loc.T("其他旗標"), _extraFlags, _browserRows);
        AddRow(Loc.T("模式"), _cmdMode, _cmdRows);
        AddRow(Loc.T("指令"), _cmdCommand, _cmdRows);
        AddRow(Loc.T("參數"), _args, _generalRows);
        AddRow(Loc.T("工作目錄"), WithButton(_workDir, Loc.T("瀏覽…"), BrowseWorkDir));
        AddRow(Loc.T("時間"), Timing());
        AddRow(Loc.T("視窗比對規則"), WithButton(_matchText, Loc.T("編輯…"), EditMatch));
        AddRow(Loc.T("備註"), _note);
        AddRow(Loc.T("實際內容"), _preview);
        AddRow("", _status);
        AddRow("", WithButton(_testResult, Loc.T("測試呼叫這一項"), TestLaunch));
        return _table;
    }

    private void AddRow(string label, Control field, List<Control>? group = null)
    {
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) };
        _table.Controls.Add(caption, 0, _row);
        _table.Controls.Add(field, 1, _row);
        _row++;
        group?.Add(caption);
        group?.Add(field);
    }

    private static Label Hint(string text) => new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText };

    private static Control WithButton(Control field, string text, EventHandler click)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(field, 0, 0);
        panel.Controls.Add(Btn(text, click), 1, 0);
        return panel;
    }

    private Control Timing()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        panel.Controls.Add(new Label { Text = Loc.T("這項完成後等"), AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        panel.Controls.Add(_delay);
        panel.Controls.Add(new Label { Text = Loc.T("毫秒再呼叫下一項；等視窗出現最多"), AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        panel.Controls.Add(_wait);
        panel.Controls.Add(new Label { Text = Loc.T("秒"), AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        return panel;
    }

    // ---- events ----

    private void WireEvents()
    {
        _list.SelectedIndexChanged += (_, _) => { if (!_loading) LoadFields(); };
        _kind.SelectedIndexChanged += (_, _) => KindChanged();
        EventHandler changed = (_, _) => FieldChanged();
        foreach (var tb in new[] { _path, _url, _extraFlags, _cmdCommand, _args, _workDir, _note }) tb.TextChanged += changed;
        foreach (var cb in new[] { _noOcclusion, _noBackgrounding }) cb.CheckedChanged += changed;
        _profile.TextChanged += changed;
        _cmdMode.SelectedIndexChanged += changed;
        foreach (var n in new[] { _slot, _delay, _wait }) n.ValueChanged += changed;
        FormClosing += (_, e) => { if (DialogResult != DialogResult.OK && _dirty && !WantNotepad && !ConfirmDiscard()) e.Cancel = true; };
    }

    private bool ConfirmDiscard() =>
        MessageBox.Show(this, Loc.T("有尚未儲存的修改，要放棄嗎？"), Loc.T("編輯呼叫設定檔"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void FieldChanged()
    {
        if (_loading || Current is null) return;
        Commit();
        _dirty = true;
        RefreshListItem();
        RefreshPreview();
    }

    private void KindChanged()
    {
        if (_loading || Current is not { } entry || _kind.SelectedIndex < 0) return;
        Commit();
        var kind = (LaunchKind)_kind.SelectedIndex;
        _kinds[entry] = kind;
        if (kind != LaunchKind.General && LaunchArgs.DetectKind(entry.Path, "") != kind) entry.Path = DefaultPath(kind);
        if (kind == LaunchKind.Cmd && LaunchArgs.ParseCmd(entry.Args) is null) entry.Args = "";
        _dirty = true;
        LoadFields();
        RefreshListItem();
    }

    // ---- entry <-> fields ----

    private void LoadFields()
    {
        var entry = Current;
        _fieldsHost.Enabled = entry is not null;
        if (entry is null) { SetVisibility(LaunchKind.General, hideAll: true); RefreshPreview(); return; }
        _loading = true;
        var kind = _kinds[entry];
        _slot.Value = Math.Clamp(entry.Slot, 1, 99);
        _kind.SelectedIndex = (int)kind;
        _path.Text = entry.Path;
        _workDir.Text = entry.WorkDir;
        _delay.Value = Math.Clamp(entry.DelayMs, 0, LaunchConfig.MaxDelayMs);
        _wait.Value = Math.Clamp(entry.WaitSec, 1, LaunchConfig.MaxWaitSec);
        _note.Text = entry.Note ?? "";
        _args.Text = kind == LaunchKind.General ? entry.Args : "";
        if (kind is LaunchKind.Chrome or LaunchKind.Edge)
        {
            var b = LaunchArgs.ParseBrowser(entry.Args);
            _profiles = BrowserProfiles.Load(kind);
            _profile.Items.Clear();
            _profile.Items.AddRange(_profiles.Select(p => (object)p.Display).ToArray());
            _profile.Text = _profiles.FirstOrDefault(p => p.Folder.Equals(b.Profile, StringComparison.OrdinalIgnoreCase))?.Display ?? b.Profile;
            _url.Text = b.Url;
            _noOcclusion.Checked = b.NoOcclusion;
            _noBackgrounding.Checked = b.NoBackgrounding;
            _extraFlags.Text = b.ExtraFlags;
        }
        else if (kind == LaunchKind.Cmd)
        {
            var c = LaunchArgs.ParseCmd(entry.Args) ?? new CmdArgs();
            _cmdMode.SelectedIndex = c.KeepOpen ? 0 : 1;
            _cmdCommand.Text = c.Command;
        }
        SetVisibility(kind);
        UpdateMatchText(entry);
        _testResult.Text = "";
        _loading = false;
        RefreshPreview();
    }

    private void Commit()
    {
        if (Current is not { } entry) return;
        entry.Slot = (int)_slot.Value;
        entry.Path = _path.Text.Trim();
        entry.WorkDir = _workDir.Text.Trim();
        entry.DelayMs = (int)_delay.Value;
        entry.WaitSec = (int)_wait.Value;
        entry.Note = string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim();
        entry.Args = _kinds[entry] switch
        {
            LaunchKind.Chrome or LaunchKind.Edge => LaunchArgs.BuildBrowser(new BrowserArgs(ProfileFolder(), _noOcclusion.Checked, _noBackgrounding.Checked, _extraFlags.Text, _url.Text)),
            LaunchKind.Cmd => LaunchArgs.BuildCmd(new CmdArgs(_cmdMode.SelectedIndex != 1, _cmdCommand.Text)),
            _ => _args.Text.Trim(),
        };
    }

    private string ProfileFolder()
    {
        string text = _profile.Text.Trim();
        return _profiles.FirstOrDefault(p => p.Display == text)?.Folder ?? text;
    }

    private void SetVisibility(LaunchKind kind, bool hideAll = false)
    {
        _table.SuspendLayout();
        foreach (var c in _browserRows) c.Visible = !hideAll && kind is LaunchKind.Chrome or LaunchKind.Edge;
        foreach (var c in _cmdRows) c.Visible = !hideAll && kind == LaunchKind.Cmd;
        foreach (var c in _generalRows) c.Visible = !hideAll && kind == LaunchKind.General;
        _table.ResumeLayout();
    }

    private void RefreshPreview()
    {
        var entry = Current;
        _preview.Text = entry is null ? "" : $"{entry.Path} {entry.Args}".Trim();
        var problems = Problems();
        _status.Text = string.Join("\r\n", problems.Take(4).Select(p => p.Message));
        _status.ForeColor = problems.Any(p => p.Blocking) ? Color.Firebrick : Color.DarkOrange;
    }

    private void UpdateMatchText(LaunchEntry entry)
    {
        var m = entry.Match;
        _matchText.Text = m is null or { IsEmpty: true }
            ? Loc.T("（未設定：綁定啟動後第一個新出現的視窗）")
            : string.Join("　", new[] { Pair(Loc.T("行程"), m.Process), Pair(Loc.T("類別"), m.Class), Pair(Loc.T("標題"), m.TitleRegex) }.Where(s => s.Length > 0));
    }

    private static string Pair(string name, string value) => string.IsNullOrWhiteSpace(value) ? "" : $"{name}={value}";

    // ---- list ----

    private static string Describe(LaunchEntry e)
    {
        string name = string.IsNullOrWhiteSpace(e.Path) ? Loc.T("（未設定程式）") : System.IO.Path.GetFileNameWithoutExtension(e.Path.Trim().Trim('"'));
        string tail = e.Note ?? e.Args;
        if (tail.Length > 24) tail = tail[..24] + "…";
        return $"{e.Slot}. {name}  {tail}".TrimEnd();
    }

    private void RefreshList()
    {
        int keep = _list.SelectedIndex;
        _loading = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _entries) _list.Items.Add(Describe(e));
        _list.EndUpdate();
        if (keep >= 0 && keep < _list.Items.Count) _list.SelectedIndex = keep;
        _loading = false;
    }

    private void RefreshListItem()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _entries.Count) return;
        bool was = _loading;
        _loading = true;
        _list.Items[i] = Describe(_entries[i]);
        _loading = was;
    }

    private int NextFreeSlot()
    {
        for (int n = 1; n <= 99; n++) if (_entries.All(e => e.Slot != n)) return n;
        return 99;
    }

    private void AddEntry(bool copy)
    {
        Commit();
        var src = Current;
        var entry = copy && src is not null
            ? new LaunchEntry { Slot = NextFreeSlot(), Path = src.Path, Args = src.Args, WorkDir = src.WorkDir, DelayMs = src.DelayMs, WaitSec = src.WaitSec, Match = src.Match, Note = src.Note }
            : new LaunchEntry { Slot = NextFreeSlot() };
        _entries.Add(entry);
        _kinds[entry] = copy && src is not null ? _kinds[src] : LaunchKind.General;
        _dirty = true;
        RefreshList();
        _list.SelectedIndex = _entries.Count - 1;
    }

    private void DeleteEntry()
    {
        if (Current is not { } entry) return;
        int i = _list.SelectedIndex;
        _entries.Remove(entry);
        _kinds.Remove(entry);
        _dirty = true;
        _list.SelectedIndex = -1;
        RefreshList();
        if (_entries.Count > 0) _list.SelectedIndex = Math.Min(i, _entries.Count - 1);
        else LoadFields();
    }

    private void MoveEntry(int delta)
    {
        int i = _list.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _entries.Count) return;
        Commit();
        (_entries[i], _entries[j]) = (_entries[j], _entries[i]);
        _dirty = true;
        RefreshList();
        _list.SelectedIndex = j;
    }

    // ---- buttons ----

    private void BrowsePath(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Title = Loc.T("選擇程式、捷徑或批次檔"), Filter = Loc.T("程式與捷徑|*.exe;*.lnk;*.bat;*.cmd|所有檔案|*.*"), CheckFileExists = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.FileName;
    }

    private void BrowseWorkDir(object? s, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = Loc.T("選擇工作目錄") };
        if (dlg.ShowDialog(this) == DialogResult.OK) _workDir.Text = dlg.SelectedPath;
    }

    private void EditMatch(object? s, EventArgs e)
    {
        if (Current is not { } entry) return;
        using var form = new MatchRuleForm((int)_slot.Value, entry.Match, _windows());
        if (form.ShowDialog(this) != DialogResult.OK) return;
        entry.Match = form.Rule;
        UpdateMatchText(entry);
        _dirty = true;
    }

    private void TestLaunch(object? s, EventArgs e)
    {
        if (Current is not { } entry) return;
        Commit();
        string? error = ProgramLauncher.Start(entry);
        _testResult.ForeColor = error is null ? SystemColors.ControlText : Color.Firebrick;
        _testResult.Text = error is null ? Loc.T("已送出（測試只開啟程式，不會綁定槽位）") : Loc.T("失敗：{0}", error);
    }

    private void UseNotepad()
    {
        if (_dirty && MessageBox.Show(this, Loc.T("有尚未儲存的修改，改用記事本會放棄這些修改。要繼續嗎？"), Loc.T("編輯呼叫設定檔"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        WantNotepad = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void TrySave()
    {
        Commit();
        var blocking = Problems().Where(p => p.Blocking).ToList();
        if (blocking.Count > 0)
        {
            MessageBox.Show(this, string.Join("\r\n", blocking.Select(p => p.Message)), Loc.T("還不能儲存"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---- checks ----

    private static string DefaultPath(LaunchKind kind)
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = kind switch
        {
            LaunchKind.Chrome => [Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"), Path.Combine(local, @"Google\Chrome\Application\chrome.exe")],
            LaunchKind.Edge => [Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe")],
            LaunchKind.Cmd => ["cmd.exe"],
            _ => [""],
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private List<(string Message, bool Blocking)> Problems()
    {
        var list = new List<(string, bool)>();
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            int n = i + 1;
            if (string.IsNullOrWhiteSpace(e.Path)) list.Add((Loc.T("第 {0} 項還沒有填程式路徑。", n), true));
            if (_entries.Take(i).Any(o => o.Slot == e.Slot)) list.Add((Loc.T("第 {0} 項的槽位 {1} 和前面的項目重複，同一個槽位只能有一項。", n, e.Slot), true));
            if (e.Slot > _slotCount) list.Add((Loc.T("第 {0} 項的槽位 {1} 超過這個佈局的槽位數（{2}），執行時會被略過。", n, e.Slot, _slotCount), false));
            if (e.Match is { } m && WindowMatcher.ValidateRegex(m.TitleRegex) is { } bad) list.Add((Loc.T("第 {0} 項的標題 Regex 無效（{1}）。", n, bad), false));
        }
        return list;
    }
}

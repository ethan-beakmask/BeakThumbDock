using System.Diagnostics;
using System.Runtime.InteropServices;
using static ThumbDock.Spike.Native;

namespace ThumbDock.Spike;

// Interactive spike console for the tests that need virtual-desktop switching or elevated windows.
internal sealed class ControlForm : Form
{
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    private readonly TextBox _log = new() { Dock = DockStyle.Bottom, Height = 220, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
    private readonly CheckBox _chkTool = new() { Text = "WS_EX_TOOLWINDOW", Checked = true, AutoSize = true };
    private readonly CheckBox _chkNoAct = new() { Text = "WS_EX_NOACTIVATE", Checked = true, AutoSize = true };
    private readonly CheckBox _chkMax = new() { Text = "點擊後最大化", AutoSize = true };
    private readonly ComboBox _slot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
    private readonly System.Windows.Forms.Timer _monitor = new() { Interval = 1000 };
    private readonly ulong[] _lastHash = new ulong[DockBar.SlotCount];
    private string _lastStatus = "";
    private DockBar? _dock;

    public static DockBar? CurrentDock { get; private set; }

    public ControlForm()
    {
        Text = "ThumbDock Spike 控制台";
        Size = new Size(1100, 720);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(420, 60);
        BuildList();
        Controls.Add(_list);
        Controls.Add(_log);
        Controls.Add(BuildToolbar());
        Log.Written += line => BeginInvokeSafe(() => _log.AppendText(line + Environment.NewLine));
        _monitor.Tick += (_, _) => MonitorTick();
        _monitor.Start();
        RefreshList();
    }

    private void BeginInvokeSafe(Action a)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(a);
    }

    private void BuildList()
    {
        foreach (var (name, w) in new[] { ("標題", 300), ("行程", 140), ("HWND", 90), ("cloaked", 60), ("分類", 120), ("桌面", 80), ("提升權限", 70) })
            _list.Columns.Add(name, w);
    }

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(4) };
        for (int i = 1; i <= DockBar.SlotCount; i++) _slot.Items.Add(i);
        _slot.SelectedIndex = 0;
        bar.Controls.AddRange(new Control[]
        {
            _chkTool, _chkNoAct, _chkMax,
            Btn("建立 AppBar", (_, _) => CreateDock()),
            Btn("移除 AppBar", (_, _) => RemoveDock()),
            Btn("重新整理清單", (_, _) => RefreshList()),
            new Label { Text = "槽位", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _slot,
            Btn("綁定選取視窗", (_, _) => BindSelected()),
            Btn("清除槽位", (_, _) => _dock?.Clear(_slot.SelectedIndex)),
            Btn("啟動 Ticker", (_, _) => LaunchTicker()),
            Btn("移動選取視窗 (S-5)", (_, _) => MoveSelected()),
            Btn("記錄一行標記", (_, _) => Log.W("---------- 使用者標記 ----------")),
        });
        return bar;
    }

    private static Button Btn(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += onClick;
        return b;
    }

    private void CreateDock()
    {
        RemoveDock();
        _dock = new DockBar(_chkTool.Checked, _chkNoAct.Checked, 300) { MaximizeOnActivate = _chkMax.Checked };
        CurrentDock = _dock;
        _dock.Show();
    }

    private void RemoveDock()
    {
        _dock?.Close();
        _dock = null;
        CurrentDock = null;
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in Windows.Enumerate())
        {
            var item = new ListViewItem(new[]
            {
                r.Title, r.Process, $"0x{r.Hwnd:X}", r.Cloaked.ToString(), r.Kind, VDesk.Short(r.Desktop),
                r.Elevated switch { true => "是", false => "否", null => "未知" },
            }) { Tag = r.Hwnd };
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        Log.W($"清單: {_list.Items.Count} 個視窗");
    }

    private IntPtr Selected() => _list.SelectedItems.Count == 0 ? IntPtr.Zero : (IntPtr)_list.SelectedItems[0].Tag!;

    private void BindSelected()
    {
        IntPtr h = Selected();
        if (_dock is null || h == IntPtr.Zero) { Log.W("請先建立 AppBar 並選取視窗"); return; }
        _dock.MaximizeOnActivate = _chkMax.Checked;
        var r = Windows.Describe(h);
        Log.W($"綁定: {r.Title} / {r.Process} / 分類={r.Kind} / 提升權限={r.Elevated}");
        _dock.Bind(_slot.SelectedIndex, h);
    }

    private void LaunchTicker()
    {
        string name = ((char)('A' + Random.Shared.Next(26))).ToString();
        Process.Start(Environment.ProcessPath!, $"--ticker {name} 700 200");
        Log.W($"啟動 Spike Ticker {name}");
    }

    private void MoveSelected()
    {
        IntPtr h = Selected();
        if (h == IntPtr.Zero) return;
        GetWindowRect(h, out RECT r);
        bool ok = SetWindowPos(h, IntPtr.Zero, r.Left + 40, r.Top + 40, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        int err = Marshal.GetLastWin32Error();
        GetWindowRect(h, out RECT r2);
        Log.W($"S-5 移動: SetWindowPos={ok} err={err} 位置 {r} → {r2} 實際有移動={r.Left != r2.Left}");
    }

    // Logs a status line whenever something relevant changes (desktop, work area, dock visibility, slot liveness).
    private void MonitorTick()
    {
        IntPtr fg = GetForegroundWindow();
        string slots = _dock is null ? "-" : string.Join(" ", Enumerable.Range(0, DockBar.SlotCount).Select(SlotState));
        string dock = _dock is null ? "無" :
            $"在目前桌面={VDesk.OnCurrent(_dock.Handle)} cloaked={Cloaked(_dock.Handle)} visible={IsWindowVisible(_dock.Handle)}";
        string status = $"前景視窗桌面={VDesk.Short(VDesk.DesktopOf(fg))} 工作區={WorkArea()} | Dock: {dock} | 槽位: {slots}";
        if (status == _lastStatus) return;
        _lastStatus = status;
        Log.W(status);
    }

    private string SlotState(int i)
    {
        if (_dock!.SourceOf(i) == IntPtr.Zero) return $"{i + 1}:空";
        ulong h = Probe.HashScreen(_dock.ThumbAreaOnScreen(i));
        bool live = h != _lastHash[i];
        _lastHash[i] = h;
        return $"{i + 1}:{(live ? "更新中" : "靜止")}";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        RemoveDock();
        base.OnFormClosed(e);
    }
}

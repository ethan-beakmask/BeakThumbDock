using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static ThumbDock.Spike.Native;

namespace ThumbDock.Spike;

// Automated checks that can run on the current virtual desktop without user help.
internal sealed class SelfTest
{
    private const int DockWidth = 300;
    private readonly StringBuilder _report = new();
    private readonly List<Process> _procs = new();
    private DockBar? _dock;

    public static DockBar? CurrentDock { get; private set; }
    public static DockBar? HoldDock { get; set; }

    public async Task RunAsync()
    {
        try { await RunStepsAsync(); }
        catch (Exception ex) { R($"例外: {ex}"); }
        finally { Cleanup(); }
        string path = Path.Combine(AppContext.BaseDirectory, "selftest-results.txt");
        File.WriteAllText(path, _report.ToString(), Encoding.UTF8);
        Application.Exit();
    }

    private void R(string s) { _report.AppendLine(s); Log.W(s); }

    private async Task RunStepsAsync()
    {
        R($"OS {Environment.OSVersion.Version}  DPI-scale {Screen.PrimaryScreen!.Bounds.Width}x{Screen.PrimaryScreen.Bounds.Height}");
        RECT waBefore = WorkArea();
        IntPtr a = await LaunchTicker("A", 900, 120);
        IntPtr b = await LaunchTicker("B", 1000, 300);
        IntPtr c = await LaunchTicker("C", 1100, 480);
        if (a == 0 || b == 0 || c == 0) { R("FAIL: Ticker 視窗沒有出現"); return; }

        await CreateDock(waBefore);
        _dock!.Bind(0, a);
        _dock.Bind(1, b);
        await Task.Delay(600);

        R($"基準: 槽位1 縮圖 8 次取樣變化 {await Changes(0)} 次（可見來源）");
        await TestOccluded(a);
        await TestClickActivate(b, c, slot: 1, maximize: true, label: "S-7/S-4a 點槽位2 → B 最大化");
        await TestMaximizeRespectsDock(b);
        await TestClickActivate(a, b, slot: 0, maximize: false, label: "S-4b 點槽位1 → A（前景為他行程 B）");
        R($"    B 是否被最小化: {IsIconic(b)}（應為 False）");
        R($"啟用後縮圖仍更新: 槽位1 {await Changes(0)}/7, 槽位2 {await Changes(1)}/7");
        await TestMinimized(a);
        await TestSourceClosed(c);
        await TestRemove(waBefore);
    }

    private async Task<IntPtr> LaunchTicker(string name, int x, int y)
    {
        _procs.Add(Process.Start(Environment.ProcessPath!, $"--ticker {name} {x} {y}"));
        return await Windows.WaitForTitle($"Spike Ticker {name}");
    }

    private async Task CreateDock(RECT waBefore)
    {
        _dock = new DockBar(Environment.GetEnvironmentVariable("SPIKE_TOOL") != "0", Environment.GetEnvironmentVariable("SPIKE_NOACT") != "0", DockWidth);
        CurrentDock = _dock;
        _dock.Show();
        for (int i = 0; i < 6; i++) { await Task.Delay(500); Log.W($"  工作區 t+{(i + 1) * 500}ms = {WorkArea()}"); }
        RECT wa = WorkArea();
        bool ok = wa.Left == waBefore.Left + DockWidth && wa.Right == waBefore.Right;
        R($"S-3a 工作區: 前 {waBefore} → 後 {wa}  {(ok ? "PASS" : "FAIL")}（預期左緣 +{DockWidth}）");
    }

    // Number of changed samples out of n-1 comparisons; ticker changes every 100 ms.
    private async Task<int> Changes(int slot, int n = 8)
    {
        var area = _dock!.ThumbAreaOnScreen(slot);
        ulong prev = Probe.HashScreen(area);
        int changed = 0;
        for (int i = 1; i < n; i++)
        {
            await Task.Delay(300);
            ulong h = Probe.HashScreen(area);
            if (h != prev) changed++;
            prev = h;
        }
        return changed;
    }

    private async Task TestOccluded(IntPtr a)
    {
        RECT ra = Probe.FrameBounds(a);
        using var cover = new Form
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
            BackColor = Color.Black, TopMost = true, Bounds = new Rectangle(ra.Left - 30, ra.Top - 30, ra.Width + 60, ra.Height + 60),
        };
        cover.Show();
        SetWindowPos(cover.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x3 /*NOSIZE|NOMOVE*/);
        await Task.Delay(400);
        bool covered = CoveredBy(ra, cover.Handle);
        int ch = await Changes(0);
        R($"S-6 被完全遮住: 來源四角與中心都被遮罩蓋住={covered}, 縮圖變化 {ch}/7  {(covered && ch >= 5 ? "PASS" : "FAIL")}");
        cover.Close();
        Log.W("  遮罩已關閉");
    }

    private static bool CoveredBy(RECT r, IntPtr cover)
    {
        var pts = new[] { (r.Left + 2, r.Top + 2), (r.Right - 3, r.Top + 2), (r.Left + 2, r.Bottom - 3), (r.Right - 3, r.Bottom - 3), (r.Left + r.Width / 2, r.Top + r.Height / 2) };
        return pts.All(p => GetAncestor(WindowFromPoint(new POINT { X = p.Item1, Y = p.Item2 }), 2 /*GA_ROOT*/) == cover);
    }

    private async Task TestClickActivate(IntPtr target, IntPtr foreignFg, int slot, bool maximize, string label)
    {
        SetForegroundWindow(foreignFg);
        await Task.Delay(400);
        IntPtr fgBefore = GetForegroundWindow();
        _dock!.MaximizeOnActivate = maximize;
        int clicksBefore = _dock.ClickCount, actBefore = _dock.ActivatedCount;
        Log.W($"  {label} 點擊前 dock 啟用次數={actBefore}");
        ClickAt(_dock.ThumbAreaOnScreen(slot));
        await Task.Delay(900);
        bool fgOk = GetForegroundWindow() == target;
        bool clickOk = _dock.ClickCount == clicksBefore + 1;
        bool noAct = _dock.ActivatedCount == actBefore;
        R($"{label}: 點擊前前景是他行程={!Proc.IsOwnProcess(fgBefore)}, dock 收到點擊={clickOk}, " +
          $"dock 沒被啟用={noAct}, 目標成為前景={fgOk}  {(fgOk && clickOk && noAct ? "PASS" : "FAIL")}");
    }

    private static void ClickAt(Rectangle r)
    {
        Point saved = Cursor.Position;
        Cursor.Position = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var inputs = new[]
        {
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } },
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } },
        };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        Thread.Sleep(50);
        Cursor.Position = saved;
    }

    private async Task TestMaximizeRespectsDock(IntPtr b)
    {
        await Task.Delay(300);
        RECT rb = Probe.FrameBounds(b);
        var d = _dock!.ScreenBounds;
        bool ok = rb.Left >= d.Right;
        R($"最大化不蓋縮圖區: B 外框 {rb}, dock {d}  {(ok ? "PASS" : "FAIL")}");
    }

    private async Task TestMinimized(IntPtr a)
    {
        ShowWindow(a, SW_MINIMIZE);
        await Task.Delay(500);
        R($"R-1 最小化來源（參考）: 縮圖變化 {await Changes(0)}/7（預期 0，DWM 限制）");
        ShowWindow(a, SW_RESTORE);
    }

    private async Task TestSourceClosed(IntPtr c)
    {
        _dock!.Bind(2, c);
        PostMessage(c, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        await Task.Delay(1500);
        R($"來源關閉: 槽位3 回到等待中={_dock.SourceOf(2) == IntPtr.Zero}, 程式未當掉  " +
          $"{(_dock.SourceOf(2) == IntPtr.Zero ? "PASS" : "FAIL")}");
    }

    private async Task TestRemove(RECT waBefore)
    {
        _dock!.Close();
        _dock = null;
        CurrentDock = null;
        for (int i = 0; i < 6; i++) { await Task.Delay(500); Log.W($"  工作區 t+{(i + 1) * 500}ms = {WorkArea()}"); }
        RECT wa = WorkArea();
        bool ok = wa.Left == waBefore.Left && wa.Right == waBefore.Right && wa.Bottom == waBefore.Bottom;
        R($"移除 AppBar 後工作區恢復: {wa}  {(ok ? "PASS" : "FAIL")}");
    }

    private void Cleanup()
    {
        _dock?.Close();
        foreach (var p in _procs)
        {
            try { if (!p.HasExited) p.Kill(); } catch { /* best effort */ }
        }
    }
}

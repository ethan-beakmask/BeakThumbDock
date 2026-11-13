using ThumbDock.Core;

namespace ThumbDock.UI;

/// <summary>
/// Runs a layout's launch config: for each entry start the program, wait for its window and bind it to the slot.
/// Slots that already hold a window are left alone, so nothing is started twice. Runs on the UI thread without blocking it.
/// </summary>
internal sealed class LaunchRunner(DockSession session, WindowEnumerator enumerator)
{
    private const int PollMs = 500;
    private bool _cancelled;

    public bool Running { get; private set; }

    public event Action<string>? Notice;

    public void Cancel() => _cancelled = true;

    public async Task RunAsync(LaunchConfig config)
    {
        if (Running) return;
        Running = true;
        _cancelled = false;
        var layout = session.Layout;
        int bound = 0, skipped = 0, failed = 0;
        AppLog.Info($"launch: layout '{layout.Name}', {config.Launch.Count} program(s)");
        try
        {
            foreach (var entry in config.Launch)
            {
                if (Aborted(layout)) break;
                switch (await RunEntryAsync(entry, layout))
                {
                    case LaunchOutcome.Bound: bound++; break;
                    case LaunchOutcome.AlreadyBound: skipped++; break;
                    default: failed++; break;
                }
                await PauseAsync(entry.DelayMs, layout);
            }
        }
        finally { Running = false; }
        string summary = Loc.T("呼叫完成：綁定 {0} 個，已有視窗而略過 {1} 個，失敗或逾時 {2} 個。", bound, skipped, failed);
        AppLog.Info("launch: " + summary);
        if (!_cancelled) Notice?.Invoke(summary);
    }

    // Waits in short steps so a layout switch or cancel ends the run quickly instead of after a long delayMs.
    private async Task PauseAsync(int ms, Layout.LayoutModel layout)
    {
        for (int waited = 0; waited < ms && !Aborted(layout); waited += PollMs)
            await Task.Delay(Math.Min(PollMs, ms - waited));
    }

    private bool Aborted(Layout.LayoutModel layout) => _cancelled || !ReferenceEquals(layout, session.Layout);

    private enum LaunchOutcome { Bound, AlreadyBound, Failed }

    private async Task<LaunchOutcome> RunEntryAsync(LaunchEntry entry, Layout.LayoutModel layout)
    {
        int index = entry.Slot - 1;
        if (index >= session.Dock.Slots.Count)
        {
            Fail(Loc.T("槽位 {0} 不存在（目前只有 {1} 個），略過 {2}。", entry.Slot, session.Dock.Slots.Count, entry.Path));
            return LaunchOutcome.Failed;
        }

        if (entry.Match is not null) session.SetRule(index, entry.Match);
        session.AutoBindNow();
        if (session.Dock.Slots[index].Source != 0)
        {
            AppLog.Info($"launch: slot {entry.Slot} already has a window, '{entry.Path}' not started");
            return LaunchOutcome.AlreadyBound;
        }

        var before = enumerator.Enumerate().Select(w => w.Hwnd).ToHashSet();
        if (ProgramLauncher.Start(entry) is { } error)
        {
            Fail(Loc.T("無法呼叫 {0}：{1}", entry.Path, error));
            return LaunchOutcome.Failed;
        }
        AppLog.Info($"launch: started '{entry.Path}' {entry.Args} -> slot {entry.Slot}");

        var deadline = DateTime.UtcNow.AddSeconds(entry.WaitSec);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollMs);
            if (Aborted(layout) || index >= session.Dock.Slots.Count) return LaunchOutcome.Failed;
            session.AutoBindNow();
            if (session.Dock.Slots[index].Source != 0) return LaunchOutcome.Bound;
            if (entry.Match is null && TryBindNewWindow(entry, index, before)) return LaunchOutcome.Bound;
        }
        Fail(Loc.T("{0} 呼叫後 {1} 秒內沒有找到可綁定的視窗（槽位 {2}）。", entry.Path, entry.WaitSec, entry.Slot));
        return LaunchOutcome.Failed;
    }

    private bool TryBindNewWindow(LaunchEntry entry, int index, HashSet<nint> before)
    {
        var windows = enumerator.Enumerate();
        var fresh = windows.Where(w => !before.Contains(w.Hwnd) && !session.Dock.IsBound(w.Hwnd));
        if (LaunchWindowPicker.Pick(fresh, entry) is not { } window) return false;
        bool ok = session.BindManually(index, window, windows);
        if (ok) AppLog.Info($"launch: slot {entry.Slot} bound to '{window.Title}' ({window.ProcessName})");
        return ok;
    }

    private void Fail(string message)
    {
        AppLog.Error("launch: " + message);
        Notice?.Invoke(message);
    }
}

using ThumbDock.Layout;

namespace ThumbDock.Core;

/// <summary>One program to start for a layout (config/launch/&lt;layout&gt;.json). Hand-edited, so values are repaired, not trusted.</summary>
internal sealed class LaunchEntry
{
    /// <summary>Slot number (1-based) the new window is bound to.</summary>
    public int Slot { get; set; }

    /// <summary>Program, shortcut or batch file. Environment variables like %ProgramFiles% are expanded.</summary>
    public string Path { get; set; } = "";

    public string Args { get; set; } = "";
    public string WorkDir { get; set; } = "";

    /// <summary>Pause after this entry before the next one starts.</summary>
    public int DelayMs { get; set; } = LaunchConfig.DefaultDelayMs;

    /// <summary>How long to wait for the program's window.</summary>
    public int WaitSec { get; set; } = LaunchConfig.DefaultWaitSec;

    /// <summary>
    /// Optional. Written into the slot's match rule before starting; the slot is then bound to the first window matching it.
    /// Without it, the first new window that appears after the start is bound.
    /// </summary>
    public MatchRule? Match { get; set; }

    /// <summary>Free text for the user ("note"). Ignored when launching; kept in the file because the editor rewrites it without // comments.</summary>
    public string? Note { get; set; }
}

internal sealed class LaunchConfig
{
    public const int DefaultDelayMs = 1000, DefaultWaitSec = 15, MaxDelayMs = 60_000, MaxWaitSec = 120;

    public List<LaunchEntry> Launch { get; set; } = [];

    /// <summary>Removes unusable entries and clamps numbers. Returns a message per problem (for the log and the user).</summary>
    public List<string> Normalize()
    {
        var warnings = new List<string>();
        Launch ??= [];
        var usedSlots = new HashSet<int>();
        var kept = new List<LaunchEntry>();
        for (int i = 0; i < Launch.Count; i++)
        {
            var e = Launch[i];
            string where = Loc.T("第 {0} 項", i + 1);
            if (e is null || string.IsNullOrWhiteSpace(e.Path)) { warnings.Add(Loc.T("{0}沒有 path，已略過。", where)); continue; }
            if (e.Slot < 1) { warnings.Add(Loc.T("{0}（{1}）的 slot 必須是 1 以上的數字，已略過。", where, e.Path)); continue; }
            if (!usedSlots.Add(e.Slot)) { warnings.Add(Loc.T("{0}（{1}）的槽位 {2} 已被前面的項目使用，已略過。", where, e.Path, e.Slot)); continue; }
            e.Path = e.Path.Trim();
            e.Args ??= "";
            e.WorkDir ??= "";
            e.DelayMs = Math.Clamp(e.DelayMs, 0, MaxDelayMs);
            e.WaitSec = Math.Clamp(e.WaitSec, 1, MaxWaitSec);
            if (e.Match is { IsEmpty: true }) e.Match = null;
            if (string.IsNullOrWhiteSpace(e.Note)) e.Note = null;
            if (e.Match is { TitleRegex: var re } && WindowMatcher.ValidateRegex(re) is { } problem)
                warnings.Add(Loc.T("{0}（{1}）的 titleRegex 無效（{2}），這個規則永遠不會符合。", where, e.Path, problem));
            kept.Add(e);
        }
        Launch = kept;
        return warnings;
    }
}

/// <summary>Chooses which newly appeared window belongs to a launched program.</summary>
internal static class LaunchWindowPicker
{
    /// <summary>
    /// Prefers a window of the same process name as the started file (chrome.exe → chrome.exe);
    /// otherwise the first one, because launchers hand windows to other processes (cmd.exe → Windows Terminal).
    /// </summary>
    public static WindowInfo? Pick(IEnumerable<WindowInfo> candidates, LaunchEntry entry)
    {
        var list = candidates.ToList();
        if (list.Count == 0) return null;
        string stem = System.IO.Path.GetFileNameWithoutExtension(entry.Path.Trim().Trim('"'));
        return list.FirstOrDefault(w => string.Equals(System.IO.Path.GetFileNameWithoutExtension(w.ProcessName), stem, StringComparison.OrdinalIgnoreCase))
               ?? list[0];
    }
}

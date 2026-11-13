using System.Text.RegularExpressions;
using ThumbDock.Layout;

namespace ThumbDock.Core;

/// <summary>FR-5: matches slot rules (process + class + title regex) against windows, because HWNDs change every boot.</summary>
internal static class WindowMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static bool IsMatch(MatchRule rule, WindowInfo w)
    {
        if (rule.IsEmpty) return false;
        if (!string.IsNullOrWhiteSpace(rule.Process) && !SameProcess(rule.Process, w.ProcessName)) return false;
        if (!string.IsNullOrWhiteSpace(rule.Class) && !string.Equals(rule.Class.Trim(), w.ClassName, StringComparison.Ordinal)) return false;
        return string.IsNullOrWhiteSpace(rule.TitleRegex) || TitleMatches(rule.TitleRegex, w.Title);
    }

    /// <summary>First window matching <paramref name="rule"/> that is not in <paramref name="taken"/>; 0 if none.</summary>
    public static nint FindMatch(MatchRule rule, IEnumerable<WindowInfo> windows, IReadOnlySet<nint> taken) =>
        windows.FirstOrDefault(w => !taken.Contains(w.Hwnd) && IsMatch(rule, w))?.Hwnd ?? 0;

    /// <summary>
    /// Rule for a window picked by the user, as loose as possible so it survives restarts:
    /// process only when the process has one window; + class when that tells its windows apart;
    /// + escaped title otherwise. (Some class names, e.g. WinForms', contain per-build hashes,
    /// and titles of browsers etc. change often, so both are added only when needed.)
    /// </summary>
    public static MatchRule CreateRule(WindowInfo w, IEnumerable<WindowInfo> all)
    {
        var sameProcess = all.Where(o => o.Hwnd != w.Hwnd && SameProcess(o.ProcessName, w.ProcessName)).ToList();
        if (sameProcess.Count == 0) return new MatchRule(w.ProcessName);
        if (sameProcess.All(o => o.ClassName != w.ClassName)) return new MatchRule(w.ProcessName, w.ClassName);
        return new MatchRule(w.ProcessName, w.ClassName, Regex.Escape(w.Title));
    }

    /// <summary>Null when valid, otherwise a short error message for the UI.</summary>
    public static string? ValidateRegex(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        try
        {
            _ = new Regex(pattern, RegexOptions.None, RegexTimeout);
            return null;
        }
        catch (ArgumentException ex) { return ex.Message; }
    }

    private static bool TitleMatches(string pattern, string title)
    {
        try { return Regex.IsMatch(title, pattern, RegexOptions.CultureInvariant, RegexTimeout); }
        catch (ArgumentException) { return false; }          // invalid pattern never matches
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static bool SameProcess(string a, string b) =>
        string.Equals(StripExe(a), StripExe(b), StringComparison.OrdinalIgnoreCase);

    private static string StripExe(string name)
    {
        name = name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}

namespace ThumbDock.Core;

/// <summary>The program types the launch editor offers field-based editing for. Never written to the file: it is derived from path and args.</summary>
internal enum LaunchKind { General, Chrome, Edge, Cmd }

/// <summary>Chromium command line split into editor fields. Anything not recognised stays in <see cref="ExtraFlags"/> untouched.</summary>
internal sealed record BrowserArgs(string Profile = "", bool NoOcclusion = false, bool NoBackgrounding = false, string ExtraFlags = "", string Url = "");

/// <summary>cmd.exe /k (stay open) or /c (close after the command) plus the command text.</summary>
internal sealed record CmdArgs(bool KeepOpen = true, string Command = "");

/// <summary>Pure logic that turns editor fields into the <c>args</c> string and back. No Win32, unit-testable.</summary>
internal static class LaunchArgs
{
    public const string OcclusionFlag = "--disable-features=CalculateNativeWinOcclusion";
    public const string BackgroundingFlag = "--disable-backgrounding-occluded-windows";
    private const string ProfilePrefix = "--profile-directory=";

    /// <summary>Guesses the editor type from the program name; cmd.exe only counts when its args fit the /k /c form.</summary>
    public static LaunchKind DetectKind(string path, string args)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(path.Trim().Trim('"')).ToLowerInvariant();
        return stem switch
        {
            "chrome" => LaunchKind.Chrome,
            "msedge" => LaunchKind.Edge,
            "cmd" when ParseCmd(args) is not null => LaunchKind.Cmd,
            _ => LaunchKind.General,
        };
    }

    /// <summary>Splits on whitespace outside double quotes. Tokens keep their quotes so they can be written back unchanged.</summary>
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuote = false;
        foreach (char c in text)
        {
            if (c == '"') inQuote = !inQuote;
            if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    public static BrowserArgs ParseBrowser(string args)
    {
        string profile = "", url = "";
        bool occlusion = false, backgrounding = false;
        var extra = new List<string>();
        foreach (string raw in Tokenize(args ?? ""))
        {
            string plain = raw.Replace("\"", "");
            if (plain.StartsWith(ProfilePrefix, StringComparison.OrdinalIgnoreCase) && profile.Length == 0) profile = plain[ProfilePrefix.Length..];
            else if (plain.Equals(OcclusionFlag, StringComparison.OrdinalIgnoreCase)) occlusion = true;
            else if (plain.Equals(BackgroundingFlag, StringComparison.OrdinalIgnoreCase)) backgrounding = true;
            else if (!plain.StartsWith('-') && url.Length == 0) url = plain;
            else extra.Add(raw);
        }
        return new BrowserArgs(profile, occlusion, backgrounding, string.Join(' ', extra), url);
    }

    public static string BuildBrowser(BrowserArgs a)
    {
        var parts = new List<string>();
        if (a.NoOcclusion) parts.Add(OcclusionFlag);
        if (a.NoBackgrounding) parts.Add(BackgroundingFlag);
        if (!string.IsNullOrWhiteSpace(a.ExtraFlags)) parts.Add(a.ExtraFlags.Trim());
        if (!string.IsNullOrWhiteSpace(a.Profile)) parts.Add($"{ProfilePrefix}\"{a.Profile.Trim()}\"");
        string url = a.Url.Trim();
        if (url.Length > 0) parts.Add(url.Contains(' ') ? $"\"{url}\"" : url);
        return string.Join(' ', parts);
    }

    /// <summary>Null when the args are not the plain "/k command" or "/c command" form (for example "/d /k ...").</summary>
    public static CmdArgs? ParseCmd(string args)
    {
        string t = (args ?? "").Trim();
        if (t.Length == 0) return new CmdArgs(true, "");
        if (t.Length >= 2 && t[0] == '/' && (t[1] is 'k' or 'K' or 'c' or 'C') && (t.Length == 2 || char.IsWhiteSpace(t[2])))
            return new CmdArgs(char.ToLowerInvariant(t[1]) == 'k', t[2..].Trim());
        return null;
    }

    public static string BuildCmd(CmdArgs a)
    {
        string command = a.Command.Trim();
        if (command.Length == 0 && a.KeepOpen) return "";
        return $"{(a.KeepOpen ? "/k" : "/c")} {command}".TrimEnd();
    }
}

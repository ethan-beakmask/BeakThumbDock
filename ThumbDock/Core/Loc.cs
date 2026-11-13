using System.Globalization;
using System.Reflection;

namespace ThumbDock.Core;

/// <summary>A language the user can pick: <see cref="Code"/> "auto" is not listed, it is a separate choice.</summary>
internal sealed record LangInfo(string Code, string Name, string Status, bool IsSource);

/// <summary>
/// UI text lookup. The source text in the code is Traditional Chinese (Taiwan) and doubles as the key:
/// <c>Loc.T("設定…")</c>. Other languages are JSON files mapping that source text to a translation (format in
/// docs/i18n-translation-format.md). Languages shipped with the exe are embedded (Lang/*.json); config/lang/*.json
/// adds languages or overrides single entries of an embedded one. A missing entry falls back to English, then to
/// the source text, so a half-finished translation never shows blanks. See docs/i18n.md.
/// </summary>
internal static class Loc
{
    public const string SourceCode = "zh-TW";
    public const string English = "en";
    public const string Auto = "auto";
    private const string ChoiceFile = "language.txt";
    private const string EmbeddedPrefix = "ThumbDock.lang.";

    private static Dictionary<string, string> _selected = [];
    private static Dictionary<string, string> _english = [];

    /// <summary>Code of the language in use ("zh-TW" = the source text).</summary>
    public static string Code { get; private set; } = SourceCode;

    /// <summary>Problems found while loading (unreadable file, entries dropped); shown in the settings window.</summary>
    public static IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <summary>Translates <paramref name="source"/>; without a translation it is returned unchanged.</summary>
    public static string T(string source) => Lookup(source);

    /// <summary>Translates and formats; placeholders are {0}, {1}…, the same in every language.</summary>
    public static string T(string source, params object?[] args)
    {
        string text = Lookup(source);
        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (FormatException)
        {
            // Entries are validated on load, so this only guards against a mismatch between code and the key itself.
            return ReferenceEquals(text, source) ? source : string.Format(CultureInfo.CurrentCulture, source, args);
        }
    }

    private static string Lookup(string source)
    {
        if (_selected.TryGetValue(source, out var text) && text.Length > 0) return text;
        if (_english.TryGetValue(source, out text) && text.Length > 0) return text;
        return source;
    }

    // ---- choosing and loading ----

    /// <summary>Reads the saved choice (config/language.txt, "auto" when absent) and loads that language.</summary>
    public static void Init(string baseDirectory) => Load(baseDirectory, ReadChoice(baseDirectory));

    /// <summary>The saved choice: "auto" or a language code.</summary>
    public static string ReadChoice(string baseDirectory)
    {
        try
        {
            string path = Path.Combine(baseDirectory, "config", ChoiceFile);
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } text) return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("read language choice failed", ex); }
        return Auto;
    }

    /// <summary>Saves the choice for the next start; the running UI keeps its language.</summary>
    public static void SaveChoice(string baseDirectory, string choice)
    {
        try
        {
            string dir = Path.Combine(baseDirectory, "config");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ChoiceFile), choice);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("save language choice failed", ex); }
    }

    /// <summary>Loads a language by code, or the one matching Windows when the choice is "auto".</summary>
    public static void Load(string baseDirectory, string choice)
    {
        var problems = new List<Problem>();
        var embedded = EmbeddedFiles(problems);
        var external = ExternalFiles(baseDirectory, problems);
        var all = embedded.Concat(external).ToList();

        _english = Table(all, English, problems);
        string code = choice.Equals(Auto, StringComparison.OrdinalIgnoreCase)
            ? FromWindows(CultureInfo.CurrentUICulture, all.Select(f => f.Code))
            : choice;
        Code = code;
        _selected = [];
        if (code.Equals(SourceCode, StringComparison.OrdinalIgnoreCase)) { _english = []; Code = SourceCode; } // the source text itself: no English fallback either
        else if (code.Equals(English, StringComparison.OrdinalIgnoreCase)) { _selected = _english; Code = English; }
        else if (all.Any(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase))) _selected = Table(all, code, problems);
        else { AppLog.Error($"language '{code}' not found, using English"); Code = English; _selected = _english; }

        // Translate the warnings only now, with the tables in place.
        Warnings = problems.Select(p => p.Dropped is null
            ? Loc.T("語言檔 {0} 無法讀取，已略過。", p.File)
            : Loc.T("語言檔 {0} 有 {1} 條譯文的佔位符錯誤，已改用英文。", p.File, p.Dropped.Value)).ToList();
        foreach (var w in Warnings) AppLog.Error("lang: " + w);
    }

    /// <summary>The language code for a Windows UI culture: Traditional Chinese = source, a language file if present, else English.</summary>
    public static string FromWindows(CultureInfo culture, IEnumerable<string> available)
    {
        string name = culture.Name;
        if (name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) || name is "zh-TW" or "zh-HK" or "zh-MO") return SourceCode;
        var codes = available.ToList();
        string? exact = codes.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        // Simplified Chinese Windows reports zh-CN, zh-Hans, zh-Hans-CN or zh-SG; all of them should use the zh-CN file.
        if (name.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) || name is "zh-SG" or "zh-CN")
        {
            string? simplified = codes.FirstOrDefault(c => c.Equals("zh-CN", StringComparison.OrdinalIgnoreCase));
            if (simplified is not null) return simplified;
        }
        string? neutral = codes.FirstOrDefault(c => c.Equals(culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
        return neutral ?? English;
    }

    /// <summary>Every selectable language: the source, then embedded and config/lang files (a config file wins the name/status of the same code).</summary>
    public static List<LangInfo> Available(string baseDirectory)
    {
        var ignored = new List<Problem>();
        var list = new List<LangInfo> { new(SourceCode, "繁體中文（台灣）", "", true) };
        foreach (var f in EmbeddedFiles(ignored).Concat(ExternalFiles(baseDirectory, ignored)))
        {
            int i = list.FindIndex(l => l.Code.Equals(f.Code, StringComparison.OrdinalIgnoreCase));
            var info = new LangInfo(f.Code, f.Name, f.Status, false);
            if (i < 0) list.Add(info); else if (!list[i].IsSource) list[i] = info;
        }
        if (!list.Any(l => l.Code == English)) list.Insert(1, new LangInfo(English, "English", "", false));
        return list;
    }

    // ---- files ----

    private sealed record LangFile(string Origin, string Code, string Name, string Status, Dictionary<string, LangEntry> Strings);
    private sealed record Problem(string File, int? Dropped);

    /// <summary>Lookup table for one code: embedded entries first, config/lang entries override per key; invalid entries are dropped.</summary>
    private static Dictionary<string, string> Table(List<LangFile> files, string code, List<Problem> problems)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in files.Where(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        {
            int dropped = 0;
            foreach (var (key, entry) in f.Strings)
            {
                if (entry.Text.Length == 0) continue;
                if (!LangFormat.IsValidTranslation(key, entry.Text, out var reason)) { dropped++; AppLog.Error($"lang {f.Origin}: \"{key}\" dropped: {reason}"); continue; }
                table[key] = entry.Text;
            }
            if (dropped > 0) problems.Add(new Problem(f.Origin, dropped));
        }
        return table;
    }

    private static List<LangFile> EmbeddedFiles(List<Problem> problems)
    {
        var result = new List<LangFile>();
        var asm = Assembly.GetExecutingAssembly();
        foreach (string name in asm.GetManifestResourceNames().Where(n => n.StartsWith(EmbeddedPrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            string file = name[EmbeddedPrefix.Length..];
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            var doc = LangFormat.Parse(stream, Path.GetFileNameWithoutExtension(file), out var error);
            if (doc is null) { problems.Add(new Problem(file, null)); AppLog.Error($"embedded language file {file} is invalid: {error}"); continue; }
            result.Add(new LangFile(file, doc.Code, doc.Language, doc.Status, doc.Strings));
        }
        return result;
    }

    private static List<LangFile> ExternalFiles(string baseDirectory, List<Problem> problems)
    {
        var result = new List<LangFile>();
        string dir = Path.Combine(baseDirectory, "config", "lang");
        if (!Directory.Exists(dir)) return result;
        foreach (string path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            string file = "config/lang/" + Path.GetFileName(path);
            try
            {
                var doc = LangFormat.Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), out var error);
                if (doc is null) { problems.Add(new Problem(file, null)); AppLog.Error($"language file {file} skipped: {error}"); continue; }
                result.Add(new LangFile(file, doc.Code, doc.Language, doc.Status, doc.Strings));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(new Problem(file, null));
                AppLog.Error($"language file {file} skipped", ex);
            }
        }
        return result;
    }
}

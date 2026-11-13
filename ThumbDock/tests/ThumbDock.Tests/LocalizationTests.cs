using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThumbDock.Core;

namespace ThumbDock.Tests;

/// <summary>Keeps Lang/en.json in step with the Loc.T("...") calls in the source (see docs/i18n.md).</summary>
public class LocalizationTests
{
    private static readonly Regex Call = new("Loc\\.T\\(\\s*\"((?:[^\"\\\\\\r\\n]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new("\\{\\d+\\}", RegexOptions.Compiled);

    internal static string ProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lang", "en.json"))) dir = dir.Parent;
        // tests live in ThumbDock/tests/ThumbDock.Tests/bin/...; the walk above looks for ThumbDock/Lang/en.json upwards
        if (dir is null)
        {
            dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThumbDock", "Lang", "en.json"))) dir = dir.Parent;
            if (dir is not null) return Path.Combine(dir.FullName, "ThumbDock");
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Unescape(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '0': sb.Append('\0'); break;
                case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                default: sb.Append(n); break; // \" \\ \'
            }
        }
        return sb.ToString();
    }

    private static HashSet<string> UsedKeys()
    {
        string root = ProjectDir();
        var keys = new HashSet<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("obj/") || rel.StartsWith("bin/") || rel.StartsWith("tests/")) continue;
            foreach (Match m in Call.Matches(File.ReadAllText(file))) keys.Add(Unescape(m.Groups[1].Value));
        }
        return keys;
    }

    private static Dictionary<string, string> English()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectDir(), "Lang", "en.json")));
        return doc.RootElement.GetProperty("strings").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    [Fact]
    public void EverySourceKeyHasAnEnglishTranslation()
    {
        var english = English();
        var missing = UsedKeys().Where(k => !english.ContainsKey(k) || english[k].Length == 0).ToList();
        Assert.True(missing.Count == 0, "Missing in Lang/en.json:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EnglishHasNoStaleKeys()
    {
        var used = UsedKeys();
        var stale = English().Keys.Where(k => !used.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "In Lang/en.json but not used by any Loc.T:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void PlaceholdersMatchBetweenSourceAndEnglish()
    {
        var bad = English().Where(kv => !Placeholders(kv.Key).SequenceEqual(Placeholders(kv.Value))).Select(kv => kv.Key).ToList();
        Assert.True(bad.Count == 0, "Placeholder mismatch:\n" + string.Join("\n", bad));
    }

    private static IEnumerable<string> Placeholders(string s) => Placeholder.Matches(s).Select(m => m.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal);

    [Theory]
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("zh-Hant-TW", "zh-TW")]
    [InlineData("zh-HK", "zh-TW")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "en")]       // no French file: English
    [InlineData("zh-CN", "en")]       // Simplified Chinese is not Traditional: English unless a zh-CN file exists
    public void WindowsLanguageMapsToACode(string culture, string expected) =>
        Assert.Equal(expected, Loc.FromWindows(new CultureInfo(culture), ["en", "ja"]));

    [Fact]
    public void WindowsLanguagePrefersAnExistingFile()
    {
        Assert.Equal("ja", Loc.FromWindows(new CultureInfo("ja-JP"), ["en", "ja"]));
        Assert.Equal("zh-CN", Loc.FromWindows(new CultureInfo("zh-CN"), ["en", "zh-CN"]));
        // Every Simplified Chinese culture name Windows may report maps to the zh-CN file.
        Assert.Equal("zh-CN", Loc.FromWindows(new CultureInfo("zh-Hans-CN"), ["en", "ja", "zh-CN"]));
        Assert.Equal("zh-CN", Loc.FromWindows(new CultureInfo("zh-Hans"), ["en", "ja", "zh-CN"]));
        Assert.Equal("zh-CN", Loc.FromWindows(new CultureInfo("zh-SG"), ["en", "ja", "zh-CN"]));
        // Traditional Chinese stays the source text even when a zh-CN file exists.
        Assert.Equal("zh-TW", Loc.FromWindows(new CultureInfo("zh-Hant-HK"), ["en", "ja", "zh-CN"]));
    }

    [Fact]
    public void EnglishIsEmbeddedInTheExecutable()
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream("ThumbDock.lang.en.json");
        Assert.NotNull(stream);
        using var doc = JsonDocument.Parse(stream!);
        Assert.Equal(English().Count, doc.RootElement.GetProperty("strings").EnumerateObject().Count());
    }

    [Fact]
    public void SourceLanguageDoesNotFallBackToEnglish()
    {
        // Regression: choosing zh-TW (or auto on a Traditional Chinese Windows) once showed English because the
        // English table was still used as the fallback. Loading the source language leaves the state this suite expects.
        string tmp = Path.Combine(Path.GetTempPath(), "thumbdock-loc-" + Guid.NewGuid().ToString("N"));
        Loc.Load(tmp, "zh-TW");
        Assert.Equal("設定…", Loc.T("設定…"));
        Assert.Equal("zh-TW", Loc.Code);
    }

    [Fact]
    public void WithoutATranslationTheSourceTextIsReturned()
    {
        Assert.Equal("沒有這個鍵 5", Loc.T("沒有這個鍵 {0}", 5));
        Assert.Equal("沒有這個鍵", Loc.T("沒有這個鍵"));
    }

    // ---- format 2, embedded languages, config/lang overrides (docs/i18n-translation-format.md) ----

    [Fact]
    public void EveryEmbeddedLanguageFileIsValid()
    {
        var asm = typeof(Loc).Assembly;
        var names = asm.GetManifestResourceNames().Where(n => n.StartsWith("ThumbDock.lang.") && n.EndsWith(".json")).ToList();
        Assert.Contains("ThumbDock.lang.en.json", names);
        using var enStream = asm.GetManifestResourceStream("ThumbDock.lang.en.json")!;
        var english = LangFormat.Parse(enStream, "en", out _)!;
        foreach (string name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            var doc = LangFormat.Parse(stream, name, out var error);
            Assert.True(doc is not null, $"{name}: {error}");
            var (errors, _) = ThumbDock.LangTool.Catalog.Check(english, doc!);
            Assert.True(errors.Count == 0, name + ":\n" + string.Join("\n", errors));
            Assert.Equal(name, "ThumbDock.lang." + doc!.Code + ".json");
        }
    }

    private sealed class TempConfig : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "thumbdock-loc-" + Guid.NewGuid().ToString("N"));
        public TempConfig() => Directory.CreateDirectory(Path.Combine(Dir, "config", "lang"));
        public void Write(string file, string json) => File.WriteAllText(Path.Combine(Dir, "config", "lang", file), json);
        public void Dispose()
        {
            Loc.Load(Dir, "zh-TW"); // leave the process in the state the other tests expect
            try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Format2FileIsReadAndMissingKeysFallBackToEnglishThenSource()
    {
        using var tmp = new TempConfig();
        // "xx" is not a shipped language, so nothing embedded sits between this file and English.
        tmp.Write("xx.json", """
            { "code": "xx", "language": "Test", "status": "test", "strings": {
              "設定…": { "text": "設定(ja)…", "en": "Settings…" },
              "舊格式的鍵": "legacy value" } }
            """);
        Loc.Load(tmp.Dir, "xx");
        Assert.Equal("xx", Loc.Code);
        Assert.Equal("設定(ja)…", Loc.T("設定…"));
        Assert.Equal("legacy value", Loc.T("舊格式的鍵"));
        Assert.Equal("Cancel", Loc.T("取消"));          // not in ja: English
        Assert.Equal("沒有這個鍵", Loc.T("沒有這個鍵"));  // nowhere: source
        Assert.Empty(Loc.Warnings);
    }

    [Fact]
    public void ConfigFileOverridesSingleEntriesOfEmbeddedEnglish()
    {
        using var tmp = new TempConfig();
        tmp.Write("en.json", """{ "code": "en", "language": "English (custom)", "strings": { "取消": { "text": "Abort", "en": "Cancel" } } }""");
        Loc.Load(tmp.Dir, "en");
        Assert.Equal("Abort", Loc.T("取消"));
        Assert.Equal("Settings…", Loc.T("設定…")); // the rest still comes from the embedded file
        var info = Loc.Available(tmp.Dir).Single(l => l.Code == "en");
        Assert.Equal("English (custom)", info.Name);
    }

    [Fact]
    public void EntriesWithBadPlaceholdersAreDroppedWithAWarningAndUnreadableFilesAreReported()
    {
        using var tmp = new TempConfig();
        tmp.Write("xx.json", """
            { "code": "xx", "language": "Test", "strings": {
              "桌面 {0}": { "text": "デスクトップ {1}", "en": "Desktop {0}" },
              "取消": { "text": "キャンセル", "en": "Cancel" } } }
            """);
        tmp.Write("broken.json", "{ not json");
        Loc.Load(tmp.Dir, "xx");
        Assert.Equal("Desktop 3", Loc.T("桌面 {0}", 3));   // dropped: English
        Assert.Equal("キャンセル", Loc.T("取消"));
        Assert.Equal(2, Loc.Warnings.Count);
        Assert.Contains(Loc.Warnings, w => w.Contains("broken.json"));
        Assert.Contains(Loc.Warnings, w => w.Contains("xx.json") && w.Contains('1'));
        Assert.DoesNotContain(Loc.Available(tmp.Dir), l => l.Code == "broken");
    }

    [Fact]
    public void FormatFallsBackToTheSourceTextWhenTheKeyItselfCannotFormat()
    {
        // The validator compares against the key, so a key/arguments mismatch in the code is the only way to get here.
        Assert.Equal("只有一個 {0} 但給了 {1}", Loc.T("只有一個 {0} 但給了 {1}", "x"));
    }

    [Fact]
    public void AvailableListsSourceEnglishAndConfigLanguagesOnce()
    {
        using var tmp = new TempConfig();
        tmp.Write("ja.json", """{ "code": "ja", "language": "日本語", "status": "draft", "strings": { } }""");
        var list = Loc.Available(tmp.Dir);
        // Shipped languages come and go (ja, zh-CN, …), so only the fixed part of the list is pinned down.
        var codes = list.Select(l => l.Code).ToArray();
        Assert.Equal(["zh-TW", "en"], codes.Take(2).ToArray());
        Assert.Equal(codes.Distinct().Count(), codes.Length);
        Assert.True(list[0].IsSource);
        Assert.Equal("draft", list.Single(l => l.Code == "ja").Status); // the config file wins over the embedded one
    }
}

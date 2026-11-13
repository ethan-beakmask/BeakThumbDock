using ThumbDock.Core;
using ThumbDock.LangTool;

namespace ThumbDock.Tests;

/// <summary>Translation tool (tools/LangTool): statuses, CSV round trip, import rules, accept, check.</summary>
public class LangToolTests
{
    private static LangDocument English() => new("en", "English", "", new Dictionary<string, LangEntry>
    {
        ["設定…"] = new("Settings…", null, null),
        ["取消"] = new("Cancel", null, null),
        ["桌面 {0}"] = new("Desktop {0}", null, null),
        ["  {0} 個視窗"] = new("  {0} windows", null, null),
        ["＝ 槽位數"] = new("= number of slots", null, null),
        ["第一行\r\n第二行"] = new("Line one\r\nLine two", null, null),
    });

    private static LangDocument Japanese() => new("ja", "日本語", "draft", new Dictionary<string, LangEntry>
    {
        ["設定…"] = new("設定…", "Settings…", null),                 // translated
        ["取消"] = new("キャンセル", "Cancel (old)", "note here"),    // review: English changed
        ["桌面 {0}"] = new("デスクトップ {0}", null, null),            // review: legacy, no basis
        ["已刪除的鍵"] = new("古い", "Old", null),                    // stale
    });

    [Fact]
    public void StatusPerEntry()
    {
        Assert.Equal(EntryStatus.Untranslated, Catalog.StatusOf("Cancel", null));
        Assert.Equal(EntryStatus.Untranslated, Catalog.StatusOf("Cancel", new LangEntry("", "Cancel", null)));
        Assert.Equal(EntryStatus.Translated, Catalog.StatusOf("Cancel", new LangEntry("x", "Cancel", null)));
        Assert.Equal(EntryStatus.Review, Catalog.StatusOf("Cancel", new LangEntry("x", "Cancel!", null)));
        Assert.Equal(EntryStatus.Review, Catalog.StatusOf("Cancel", new LangEntry("x", null, null)));
    }

    [Fact]
    public void CountsAndRowOrder()
    {
        var c = new Catalog(English(), Japanese());
        Assert.Equal(new Counts(Translated: 1, Review: 2, Untranslated: 3, Stale: 1), c.Count());
        var rows = c.Rows().ToList();
        Assert.Equal(7, rows.Count);
        Assert.Equal(EntryStatus.Stale, rows[^1].Status);
        Assert.Equal(c.Keys, rows.Take(6).Select(r => r.Key)); // English key order
    }

    [Fact]
    public void ExportCsvHasHeaderPreviousEnglishFormulaEscapeAndPendingFilter()
    {
        var c = new Catalog(English(), Japanese());
        var all = Csv.Read(c.ExportCsv(pendingOnly: false));
        Assert.Equal(Catalog.Header, all[0]);
        Assert.Equal(8, all.Count); // header + 6 + 1 stale
        var cancel = all.Single(r => r[4] == "取消");
        Assert.Equal(["2", "review", "Cancel", "キャンセル", "取消", "Cancel (old)", "note here"], cancel);
        var slots = all.Single(r => r[4] == "＝ 槽位數");
        Assert.Equal("'= number of slots", slots[2]);
        var multi = all.Single(r => r[4] == "第一行\r\n第二行");
        Assert.Equal("Line one\r\nLine two", multi[2]);
        Assert.Equal("stale", all[^1][1]);
        Assert.Equal("古い", all[^1][3]);

        var pending = Csv.Read(c.ExportCsv(pendingOnly: true));
        Assert.DoesNotContain(pending.Skip(1), r => r[1] == "translated");
        Assert.Equal(7, pending.Count);
        Assert.Equal("1", pending[1][0]); // numbering restarts per export
    }

    [Fact]
    public void ImportStoresTranslationsWithCurrentEnglishAndAppliesTheRules()
    {
        var c = new Catalog(English(), Japanese());
        string csv = Csv.Write(
        [
            Catalog.Header,
            ["1", "untranslated", "Settings…", "新しい設定…", "設定…", "", ""],              // update
            ["2", "review", "Cancel", "キャンセル", "取消", "Cancel (old)", "note here"],    // unchanged text: confirmed
            ["3", "untranslated", "  {0} windows", "{0} 個のウィンドウ", "  {0} 個視窗", "", ""], // leading spaces restored
            ["4", "untranslated", "Desktop {0}", "デスクトップ {1}", "桌面 {0}", "", ""],   // rejected: {1}
            ["5", "untranslated", "'= number of slots", "'= スロット数", "＝ 槽位數", "", ""], // apostrophe stripped
            ["6", "untranslated", "Line one\r\nLine two", "", "第一行\r\n第二行", "", ""],    // empty: untouched
            ["7", "stale", "", "古い", "已刪除的鍵", "", ""],                                // ignored
            ["8", "untranslated", "x", "y", "譯者改壞的鍵", "", ""],                         // ignored
        ]);
        var result = c.Import(csv);
        Assert.Equal(2, result.Updated); // 設定… and the confirmed 取消
        Assert.Equal(2, result.Added);
        Assert.Equal(["桌面 {0}"], result.Rejected.Select(r => r.Key));
        Assert.Equal(["已刪除的鍵", "譯者改壞的鍵"], result.Ignored);

        var s = c.Target.Strings;
        Assert.Equal(new LangEntry("新しい設定…", "Settings…", null), s["設定…"]);
        Assert.Equal(new LangEntry("キャンセル", "Cancel", "note here"), s["取消"]);
        Assert.Equal("  {0} 個のウィンドウ", s["  {0} 個視窗"].Text);
        Assert.Equal("= スロット数", s["＝ 槽位數"].Text);
        Assert.Equal(new LangEntry("デスクトップ {0}", null, null), s["桌面 {0}"]); // rejected row left as it was
        Assert.False(s.ContainsKey("第一行\r\n第二行"));
        Assert.Equal(new Counts(4, 1, 1, 1), c.Count());
    }

    [Fact]
    public void ImportRequiresTheKeyAndTranslationColumnsButToleratesReordering()
    {
        var c = new Catalog(English(), Japanese());
        Assert.Throws<InvalidDataException>(() => c.Import("No,English\r\n1,x\r\n"));
        var r = c.Import("Translation,Chinese (source)\r\nAbbrechen,取消\r\n");
        Assert.Equal(1, r.Updated);
        Assert.Equal("note here", c.Target.Strings["取消"].Note); // no Note column: note kept
    }

    [Fact]
    public void AcceptStampsReviewEntriesAllOrOne()
    {
        var c = new Catalog(English(), Japanese());
        Assert.Equal(1, c.Accept("取消"));
        Assert.Equal("Cancel", c.Target.Strings["取消"].En);
        Assert.Null(c.Target.Strings["桌面 {0}"].En);
        Assert.Equal(1, c.Accept());
        Assert.Equal("Desktop {0}", c.Target.Strings["桌面 {0}"].En);
        Assert.Equal(0, c.Accept());
    }

    [Fact]
    public void SaveWritesFormat2InEnglishOrderDropsStaleAndKeepsABackup()
    {
        string dir = Path.Combine(Path.GetTempPath(), "thumbdock-langtool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "ja.json");
            File.WriteAllText(path, "old");
            var c = new Catalog(English(), Japanese());
            var stale = c.Save(path);
            Assert.Equal(["已刪除的鍵"], stale);
            Assert.Equal("old", File.ReadAllText(path + ".bak"));
            var doc = Catalog.LoadDocument(path);
            Assert.Equal("ja", doc.Code);
            Assert.Equal("日本語", doc.Language);
            Assert.Equal(["取消", "桌面 {0}", "設定…"], doc.Strings.Keys.ToArray()); // English (ordinal) order; untranslated keys are not written
            Assert.Equal(new LangEntry("キャンセル", "Cancel (old)", "note here"), doc.Strings["取消"]);
            Assert.Equal(new LangEntry("デスクトップ {0}", "", null), doc.Strings["桌面 {0}"]); // legacy entry gets an empty basis = still review
            Assert.Equal(EntryStatus.Review, Catalog.StatusOf("Desktop {0}", doc.Strings["桌面 {0}"]));
            string json = File.ReadAllText(path);
            Assert.Contains("\"設定…\"", json); // CJK not escaped
            Assert.DoesNotContain("\\u", json);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CheckReportsPlaceholderErrorsStaleKeysAndLegacyEntries()
    {
        var ja = Japanese();
        ja.Strings["桌面 {0}"] = new("デスクトップ {0} {2}", null, null);
        var (errors, warnings) = Catalog.Check(English(), ja);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("stale key") && e.Contains("已刪除的鍵"));
        Assert.Contains(errors, e => e.Contains("placeholder") && e.Contains("桌面"));
        Assert.Contains(warnings, w => w.Contains("old flat format"));
        Assert.Contains(warnings, w => w.Contains("need review"));

        var (okErrors, _) = Catalog.Check(English(), English());
        Assert.Empty(okErrors);
    }

    [Fact]
    public void CsvRoundTripKeepsQuotesCommasAndNewlines()
    {
        string[][] rows = [["a", "say \"hi\", ok", "line1\r\nline2", ""], ["=x", "'", "中文", "end"]];
        var back = Csv.Read(Csv.Write(rows));
        Assert.Equal(rows, back);
        Assert.Equal([["x", "y"], ["1", "2"]], Csv.Read("﻿x,y\n1,2\n"));
    }

    [Theory]
    [InlineData("Desktop {0}", "デスクトップ {0}", true)]
    [InlineData("Desktop {0}", "デスクトップ", true)]            // fewer placeholders is allowed
    [InlineData("Desktop {0}", "デスクトップ {1}", false)]
    [InlineData("Desktop {0}", "デスクトップ {0", false)]
    [InlineData("Desktop {0}", "デスクトップ {0}}", false)]
    [InlineData("Desktop {0}", "{{literal}} {0}", true)]
    [InlineData("No placeholders", "anything { goes }", true)]  // never formatted
    public void PlaceholderValidation(string source, string translation, bool valid) =>
        Assert.Equal(valid, LangFormat.IsValidTranslation(source, translation, out _));

    [Fact]
    public void OuterWhitespaceFollowsTheEnglish()
    {
        Assert.Equal("  x", Catalog.MatchOuterWhitespace("  {0} windows", "x"));
        Assert.Equal("x\r\n\r\n", Catalog.MatchOuterWhitespace("help\r\n\r\n", "  x  "));
        Assert.Equal("x", Catalog.MatchOuterWhitespace("plain", " x "));
    }

    [Fact]
    public void OuterSpacesAreNotAddedNextToFullWidthCharacters()
    {
        Assert.Equal("、", Catalog.MatchOuterWhitespace(", ", "、"));
        Assert.Equal("（メイン）", Catalog.MatchOuterWhitespace(" (primary)", " （メイン）"));
        Assert.Equal("  ウィンドウ {0} 個", Catalog.MatchOuterWhitespace("  {0} windows", "ウィンドウ {0} 個")); // menu indent stays
        Assert.Equal("ヘルプ。\r\n\r\n", Catalog.MatchOuterWhitespace("Help text.\r\n\r\n", "ヘルプ。"));       // line breaks stay
        Assert.Equal("[最小化] ", Catalog.MatchOuterWhitespace("[Minimized] ", "[最小化]"));                    // ends half-width: space stays
        Assert.Equal(", ", Catalog.MatchOuterWhitespace(", ", ","));                                            // German and the like
    }

    [Fact]
    public void RealEnglishFileRoundTripsThroughExportAndImportUnchanged()
    {
        // The shipped en.json is the pivot: a full export/import cycle of an identical translation must be lossless.
        var english = Catalog.LoadDocument(Path.Combine(LocalizationTests.ProjectDir(), "Lang", "en.json"));
        var target = new LangDocument("xx", "Test", "", english.Strings.ToDictionary(kv => kv.Key, kv => new LangEntry(kv.Value.Text, kv.Value.Text, null)));
        var c = new Catalog(english, target);
        var result = c.Import(c.ExportCsv(pendingOnly: false));
        Assert.Empty(result.Rejected);
        Assert.Empty(result.Ignored);
        Assert.Equal(english.Strings.Count, result.Updated);
        foreach (var (key, entry) in english.Strings) Assert.Equal(entry.Text, c.Target.Strings[key].Text);
        Assert.Equal(new Counts(english.Strings.Count, 0, 0, 0), c.Count());
    }
}

using System.Text;
using ThumbDock.Core;

namespace ThumbDock.LangTool;

internal enum EntryStatus { Untranslated, Review, Translated, Stale }

internal sealed record CatalogRow(string Key, string English, EntryStatus Status, LangEntry? Entry);

internal sealed record Counts(int Translated, int Review, int Untranslated, int Stale)
{
    public int Total => Translated + Review + Untranslated;
}

internal sealed record ImportResult(int Added, int Updated, List<(string Key, string Reason)> Rejected, List<string> Ignored);

/// <summary>
/// English (en.json, the pivot) plus one target language: status per string, CSV export/import, accept, check.
/// Keys are the Traditional Chinese source text; the key order is en.json's (ordinal). Pure: callers do the I/O
/// except <see cref="Save"/>.
/// </summary>
internal sealed class Catalog
{
    public static readonly string[] Header = ["No", "Status", "English", "Translation", "Chinese (source)", "Previous English", "Note"];
    private const string FormulaStarters = "=+-@";

    public LangDocument EnglishDoc { get; }
    public LangDocument Target { get; private set; }
    public IReadOnlyList<string> Keys { get; }

    public Catalog(LangDocument english, LangDocument target)
    {
        EnglishDoc = english;
        Target = target;
        Keys = english.Strings.Keys.Order(StringComparer.Ordinal).ToList();
    }

    public static LangDocument LoadDocument(string path)
    {
        var doc = LangFormat.Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), out var error);
        return doc ?? throw new InvalidDataException($"{path}: {error}");
    }

    public static Catalog Load(string englishPath, string targetPath) => new(LoadDocument(englishPath), LoadDocument(targetPath));

    public static LangDocument NewDocument(string code, string language, string status) => new(code, language, status, []);

    // ---- status ----

    public static EntryStatus StatusOf(string english, LangEntry? entry)
    {
        if (entry is null || entry.Text.Length == 0) return EntryStatus.Untranslated;
        return entry.En is not null && entry.En == english ? EntryStatus.Translated : EntryStatus.Review;
    }

    public static string StatusName(EntryStatus s) => s switch
    {
        EntryStatus.Untranslated => "untranslated",
        EntryStatus.Review => "review",
        EntryStatus.Translated => "translated",
        _ => "stale",
    };

    /// <summary>Every string in English key order, then the stale ones (in the target but no longer in English).</summary>
    public IEnumerable<CatalogRow> Rows()
    {
        foreach (string key in Keys)
        {
            Target.Strings.TryGetValue(key, out var entry);
            yield return new CatalogRow(key, EnglishDoc.Strings[key].Text, StatusOf(EnglishDoc.Strings[key].Text, entry), entry);
        }
        foreach (string key in StaleKeys())
            yield return new CatalogRow(key, "", EntryStatus.Stale, Target.Strings[key]);
    }

    public List<string> StaleKeys() => Target.Strings.Keys.Where(k => !EnglishDoc.Strings.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();

    public Counts Count()
    {
        int t = 0, r = 0, u = 0, s = 0;
        foreach (var row in Rows())
        {
            switch (row.Status)
            {
                case EntryStatus.Translated: t++; break;
                case EntryStatus.Review: r++; break;
                case EntryStatus.Untranslated: u++; break;
                default: s++; break;
            }
        }
        return new Counts(t, r, u, s);
    }

    // ---- CSV ----

    /// <summary>CSV text (no BOM; the caller adds it when writing a file). <paramref name="pendingOnly"/> leaves out translated rows.</summary>
    public string ExportCsv(bool pendingOnly)
    {
        var rows = new List<IReadOnlyList<string>> { Header };
        int no = 0;
        foreach (var row in Rows())
        {
            if (pendingOnly && row.Status == EntryStatus.Translated) continue;
            no++;
            string previous = row.Status == EntryStatus.Review && row.Entry?.En is { } en && en != row.English ? en : "";
            rows.Add([
                no.ToString(),
                StatusName(row.Status),
                Escape(row.English),
                Escape(row.Entry?.Text ?? ""),
                Escape(row.Key),
                Escape(previous),
                Escape(row.Entry?.Note ?? ""),
            ]);
        }
        return Csv.Write(rows);
    }

    /// <summary>
    /// Applies a CSV from a translator: rows are matched by the source column; a non-empty translation is stored
    /// with the current English as its basis (the translator saw it), an empty one leaves the entry alone.
    /// </summary>
    public ImportResult Import(string csv)
    {
        var table = Csv.Read(csv);
        if (table.Count == 0) throw new InvalidDataException("CSV is empty");
        var header = table[0].Select(h => h.Trim()).ToList();
        int keyCol = header.IndexOf("Chinese (source)"), textCol = header.IndexOf("Translation"), noteCol = header.IndexOf("Note");
        if (keyCol < 0 || textCol < 0) throw new InvalidDataException("CSV needs the columns \"Chinese (source)\" and \"Translation\"");

        int added = 0, updated = 0;
        var rejected = new List<(string, string)>();
        var ignored = new List<string>();
        var strings = new Dictionary<string, LangEntry>(Target.Strings, StringComparer.Ordinal);
        foreach (var cells in table.Skip(1))
        {
            if (cells.Length <= Math.Max(keyCol, textCol)) continue;
            string key = Unescape(cells[keyCol]);
            if (key.Length == 0 && cells.All(c => c.Length == 0)) continue; // blank line
            if (!EnglishDoc.Strings.TryGetValue(key, out var englishEntry)) { ignored.Add(key); continue; }
            string english = englishEntry.Text;
            string text = Unescape(cells[textCol]);
            if (text.Trim().Length == 0) continue;
            text = MatchOuterWhitespace(english, text);
            if (!LangFormat.IsValidTranslation(key, text, out var reason)) { rejected.Add((key, reason!)); continue; }
            string? note = noteCol >= 0 && noteCol < cells.Length ? Unescape(cells[noteCol]) : strings.GetValueOrDefault(key)?.Note;
            bool existed = strings.TryGetValue(key, out var old) && old.Text.Length > 0;
            strings[key] = new LangEntry(text, english, string.IsNullOrEmpty(note) ? null : note);
            if (existed) updated++; else added++;
        }
        Target = Target with { Strings = strings };
        return new ImportResult(added, updated, rejected, ignored);
    }

    /// <summary>Marks review entries as current (sets their English basis); all of them, or one key.</summary>
    public int Accept(string? key = null)
    {
        int n = 0;
        var strings = new Dictionary<string, LangEntry>(Target.Strings, StringComparer.Ordinal);
        foreach (string k in Keys)
        {
            if (key is not null && k != key) continue;
            string english = EnglishDoc.Strings[k].Text;
            if (!strings.TryGetValue(k, out var e) || StatusOf(english, e) != EntryStatus.Review) continue;
            strings[k] = e with { En = english };
            n++;
        }
        Target = Target with { Strings = strings };
        return n;
    }

    /// <summary>Writes format 2 in English key order; stale keys are dropped (returned so the caller can report them). Keeps a .bak.</summary>
    public List<string> Save(string path)
    {
        var stale = StaleKeys();
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, LangFormat.Serialize(Target, Keys), new UTF8Encoding(false));
        return stale;
    }

    // ---- check ----

    /// <summary>Problems that make a file unfit to ship (errors) and things worth a look (warnings).</summary>
    public static (List<string> Errors, List<string> Warnings) Check(LangDocument english, LangDocument doc)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        bool isEnglish = doc.Code.Equals("en", StringComparison.OrdinalIgnoreCase);
        int legacy = 0, review = 0;
        foreach (var (key, entry) in doc.Strings.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!english.Strings.ContainsKey(key) && !isEnglish) { errors.Add($"stale key (not in en.json): \"{Short(key)}\""); continue; }
            if (entry.Text.Length == 0) continue;
            if (!LangFormat.IsValidTranslation(key, entry.Text, out var reason)) errors.Add($"placeholder error in \"{Short(key)}\": {reason}");
            if (isEnglish) continue;
            if (entry.En is null) legacy++;
            else if (StatusOf(english.Strings[key].Text, entry) == EntryStatus.Review) review++;
        }
        if (doc.Language.Length == 0 || doc.Language == doc.Code) warnings.Add("\"language\" (the language's own name) is missing");
        if (legacy > 0) warnings.Add($"{legacy} entries in the old flat format (no English basis): run import or accept to upgrade");
        if (review > 0) warnings.Add($"{review} entries need review (English changed since translation)");
        return (errors, warnings);
    }

    // ---- helpers ----

    private static string Short(string key) => key.Length > 40 ? key[..40] + "…" : key;

    /// <summary>Excel treats cells starting with = + - @ as formulas; a leading apostrophe keeps them text.</summary>
    public static string Escape(string cell) => cell.Length > 0 && FormulaStarters.Contains(cell[0]) ? "'" + cell : cell;

    public static string Unescape(string cell) => cell.Length > 1 && cell[0] == '\'' && FormulaStarters.Contains(cell[1]) ? cell[1..] : cell;

    /// <summary>Gives the translation the leading/trailing whitespace of the English (menu indents, trailing newlines) that Excel tends to eat.</summary>
    public static string MatchOuterWhitespace(string english, string translation)
    {
        string lead = english[..(english.Length - english.TrimStart().Length)];
        string trail = english[english.TrimEnd().Length..];
        string core = translation.Trim();
        if (core.Length == 0) return lead + trail;
        // A lone separator space is not wanted next to full-width text ("、", "（メイン）"); menu indents (2+ spaces) stay.
        if (lead == " " && IsFullWidth(core[0])) lead = "";
        // Line breaks always follow the English; trailing spaces are dropped after a full-width character.
        if (IsFullWidth(core[^1])) trail = new string(trail.Where(ch => ch is '\r' or '\n').ToArray());
        return lead + core + trail;
    }

    /// <summary>CJK punctuation and kana, CJK ideographs, full-width forms.</summary>
    private static bool IsFullWidth(char ch) =>
        ch is (>= '　' and <= 'ヿ') or (>= '一' and <= '鿿') or (>= '＀' and <= '￯');
}

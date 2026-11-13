using System.Text.Encodings.Web;
using System.Text.Json;

namespace ThumbDock.Core;

/// <summary>One translated string. <see cref="En"/> is the English the translator saw (null in the flat legacy format).</summary>
internal sealed record LangEntry(string Text, string? En, string? Note);

/// <summary>A parsed language file. Keys of <see cref="Strings"/> are the Traditional Chinese source text.</summary>
internal sealed record LangDocument(string Code, string Language, string Status, Dictionary<string, LangEntry> Strings);

/// <summary>
/// Reads and writes language files (docs/i18n-translation-format.md). Pure: no logging, no file system, so both the
/// app (Loc) and the maintainer tool (LangTool) use the same parser and the same placeholder rules.
/// Two layouts are accepted: the flat <c>"source": "text"</c> of en.json and 0.10.x files, and format 2
/// <c>"source": { "text", "en", "note" }</c>.
/// </summary>
internal static class LangFormat
{
    public static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Parses a language file; <paramref name="fallbackCode"/> (usually the file name) is used when "code" is absent.</summary>
    public static LangDocument? Parse(string json, string fallbackCode, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json, ReadOptions);
            return Parse(doc.RootElement, fallbackCode, out error);
        }
        catch (JsonException ex) { error = ex.Message; return null; }
    }

    public static LangDocument? Parse(Stream json, string fallbackCode, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json, ReadOptions);
            return Parse(doc.RootElement, fallbackCode, out error);
        }
        catch (JsonException ex) { error = ex.Message; return null; }
    }

    private static LangDocument? Parse(JsonElement root, string fallbackCode, out string? error)
    {
        error = null;
        if (root.ValueKind != JsonValueKind.Object) { error = "root is not an object"; return null; }
        string code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : fallbackCode;
        string name = root.TryGetProperty("language", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : code;
        string status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "";
        if (code.Length == 0) { error = "\"code\" is empty"; return null; }
        if (!root.TryGetProperty("strings", out var strings) || strings.ValueKind != JsonValueKind.Object) { error = "\"strings\" object is missing"; return null; }

        var map = new Dictionary<string, LangEntry>(StringComparer.Ordinal);
        foreach (var p in strings.EnumerateObject())
        {
            switch (p.Value.ValueKind)
            {
                case JsonValueKind.String:
                    map[p.Name] = new LangEntry(p.Value.GetString() ?? "", null, null);
                    break;
                case JsonValueKind.Object:
                    string text = p.Value.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
                    string? en = p.Value.TryGetProperty("en", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    string? note = p.Value.TryGetProperty("note", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
                    map[p.Name] = new LangEntry(text, en, string.IsNullOrEmpty(note) ? null : note);
                    break;
                default:
                    // null, numbers…: treated as "no translation" rather than failing the whole file
                    break;
            }
        }
        return new LangDocument(code, name, status, map);
    }

    /// <summary>Writes format 2, keys in <paramref name="keyOrder"/> (keys not listed are dropped), UTF-8 without BOM.</summary>
    public static string Serialize(LangDocument doc, IEnumerable<string> keyOrder)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("code", doc.Code);
            w.WriteString("language", doc.Language);
            w.WriteString("status", doc.Status);
            w.WritePropertyName("strings");
            w.WriteStartObject();
            foreach (string key in keyOrder)
            {
                if (!doc.Strings.TryGetValue(key, out var entry)) continue;
                w.WritePropertyName(key);
                w.WriteStartObject();
                w.WriteString("text", entry.Text);
                w.WriteString("en", entry.En ?? "");
                if (!string.IsNullOrEmpty(entry.Note)) w.WriteString("note", entry.Note);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()) + Environment.NewLine;
    }

    // ---- placeholders ----

    /// <summary>Distinct placeholder indexes ({0}, {1}…) in <paramref name="s"/>, ascending.</summary>
    public static List<int> Placeholders(string s)
    {
        var set = new SortedSet<int>();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '{') continue;
            int j = i + 1;
            while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
            if (j > i + 1 && j < s.Length && s[j] == '}') { set.Add(int.Parse(s.AsSpan(i + 1, j - i - 1))); i = j; }
        }
        return set.ToList();
    }

    /// <summary>
    /// True when <paramref name="translation"/> can be passed to string.Format with the arguments
    /// <paramref name="source"/> expects: every '{' is "{{" or "{n}" with n within the source's range, every '}' is
    /// "}}" or closes a placeholder. A source without placeholders is never formatted, so anything goes there.
    /// </summary>
    public static bool IsValidTranslation(string source, string translation, out string? reason)
    {
        reason = null;
        var allowed = Placeholders(source);
        if (allowed.Count == 0) return true;
        int max = allowed[^1];
        for (int i = 0; i < translation.Length; i++)
        {
            char ch = translation[i];
            if (ch == '{')
            {
                if (i + 1 < translation.Length && translation[i + 1] == '{') { i++; continue; }
                int j = i + 1;
                while (j < translation.Length && char.IsAsciiDigit(translation[j])) j++;
                if (j == i + 1 || j >= translation.Length || translation[j] != '}') { reason = $"'{{' at position {i} is not a {{n}} placeholder"; return false; }
                int n = int.Parse(translation.AsSpan(i + 1, j - i - 1));
                if (n > max) { reason = $"{{{n}}} is not used by the English text (max {{{max}}})"; return false; }
                i = j;
            }
            else if (ch == '}')
            {
                if (i + 1 < translation.Length && translation[i + 1] == '}') { i++; continue; }
                reason = $"unmatched '}}' at position {i}"; return false;
            }
        }
        return true;
    }
}

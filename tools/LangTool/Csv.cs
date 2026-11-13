using System.Text;

namespace ThumbDock.LangTool;

/// <summary>Minimal RFC 4180 CSV: every cell quoted on output, quoted cells may span lines on input.</summary>
internal static class Csv
{
    public static string Write(IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (int i = 0; i < row.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(row[i].Replace("\"", "\"\"")).Append('"');
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public static List<string[]> Read(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false, cellStarted = false;
        int i = 0;
        if (text.Length > 0 && text[0] == '﻿') i = 1;
        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(c);
                continue;
            }
            switch (c)
            {
                case '"' when !cellStarted || cell.Length == 0:
                    quoted = true; cellStarted = true; break;
                case ',':
                    row.Add(cell.ToString()); cell.Clear(); cellStarted = false; break;
                case '\r':
                    break;
                case '\n':
                    row.Add(cell.ToString()); cell.Clear(); cellStarted = false;
                    rows.Add(row.ToArray()); row.Clear();
                    break;
                default:
                    cell.Append(c); cellStarted = true; break;
            }
        }
        if (cellStarted || cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}

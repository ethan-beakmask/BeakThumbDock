using System.Text;
using ThumbDock.Core;

namespace ThumbDock.LangTool;

/// <summary>Command line of the translation tool; the logic lives in <see cref="Catalog"/>. Messages are for the maintainer (Traditional Chinese).</summary>
internal static class Program
{
    private const string Usage = """
        LangTool：ThumbDock 翻譯檔工具（格式見 docs/i18n-translation-format.md）

        用法：dotnet run --project tools/LangTool -- <命令> [參數]

          status  [<code>]                       各語言的 translated / review / untranslated / stale 數量
          new     <code> --name <自稱> [--status <文字>]
                                                 建立空的語言檔
          export  <code> [--out <csv>] [--pending]
                                                 匯出 CSV 給譯者（--pending 只匯未翻譯、需覆核、多餘）
          import  <code> <csv>                   依譯者交回的 CSV 更新語言檔
          accept  <code> [--key <原文>]          把需覆核的條目標成已確認（全部或單一鍵）
          check   [--dir <資料夾>]               驗證語言檔（預設 ThumbDock/Lang）

        共用選項：
          --lang-dir <資料夾>   語言檔所在資料夾，預設是方案根目錄下的 ThumbDock/Lang
          --file <路徑>         直接指定目標語言檔（例如 publish 下的 config/lang/ja.json）
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { return Run(args); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine("錯誤：" + ex.Message);
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        var (positional, options) = ParseArgs(args);
        if (positional.Count == 0) { Console.WriteLine(Usage); return 1; }
        string langDir = options.GetValueOrDefault("lang-dir") ?? DefaultLangDir();
        string englishPath = Path.Combine(langDir, "en.json");
        string TargetPath(string code) => options.GetValueOrDefault("file") ?? Path.Combine(langDir, code + ".json");

        switch (positional[0])
        {
            case "status":
                return Status(langDir, englishPath, positional.ElementAtOrDefault(1), options.GetValueOrDefault("file"));
            case "new":
            {
                string code = Need(positional, 1, "code");
                string name = options.GetValueOrDefault("name") ?? throw new ArgumentException("new 需要 --name <語言自稱>");
                string path = TargetPath(code);
                if (File.Exists(path)) throw new ArgumentException($"{path} 已存在");
                var english = Catalog.LoadDocument(englishPath);
                var catalog = new Catalog(english, Catalog.NewDocument(code, name, options.GetValueOrDefault("status") ?? ""));
                catalog.Save(path);
                Console.WriteLine($"已建立 {path}（{catalog.Keys.Count} 條待翻譯）。下一步：export {code}");
                return 0;
            }
            case "export":
            {
                string code = Need(positional, 1, "code");
                var catalog = Catalog.Load(englishPath, TargetPath(code));
                bool pending = options.ContainsKey("pending");
                string outPath = options.GetValueOrDefault("out") ?? $"ThumbDock-{code}-{DateTime.Now:yyyyMMdd}.csv";
                File.WriteAllText(outPath, catalog.ExportCsv(pending), new UTF8Encoding(true));
                var c = catalog.Count();
                Console.WriteLine($"已匯出 {outPath}（{(pending ? "只含待處理" : "全部")}）：translated {c.Translated}, review {c.Review}, untranslated {c.Untranslated}, stale {c.Stale}");
                return 0;
            }
            case "import":
            {
                string code = Need(positional, 1, "code");
                string csvPath = Need(positional, 2, "csv");
                string path = TargetPath(code);
                var catalog = Catalog.Load(englishPath, path);
                var result = catalog.Import(File.ReadAllText(csvPath));
                var stale = catalog.Save(path);
                Console.WriteLine($"已更新 {path}：新增 {result.Added}、更新 {result.Updated}、拒絕 {result.Rejected.Count}、忽略 {result.Ignored.Count}、移除多餘 {stale.Count}");
                foreach (var (key, reason) in result.Rejected) Console.WriteLine($"  拒絕「{key}」：{reason}");
                foreach (var key in result.Ignored) Console.WriteLine($"  忽略（不是現有原文）「{key}」");
                foreach (var key in stale) Console.WriteLine($"  移除多餘「{key}」");
                PrintCounts(catalog);
                Console.WriteLine("建議接著執行 check。");
                return result.Rejected.Count == 0 ? 0 : 1;
            }
            case "accept":
            {
                string code = Need(positional, 1, "code");
                string path = TargetPath(code);
                var catalog = Catalog.Load(englishPath, path);
                int n = catalog.Accept(options.GetValueOrDefault("key"));
                var stale = catalog.Save(path);
                Console.WriteLine($"已確認 {n} 條，移除多餘 {stale.Count} 條。");
                PrintCounts(catalog);
                return 0;
            }
            case "check":
                return Check(options.GetValueOrDefault("dir") ?? langDir, englishPath);
            default:
                Console.WriteLine(Usage);
                return 1;
        }
    }

    private static int Status(string langDir, string englishPath, string? code, string? file)
    {
        var english = Catalog.LoadDocument(englishPath);
        List<string> files = file is not null ? [file] : Directory.EnumerateFiles(langDir, "*.json").Where(p => !Path.GetFileName(p).Equals("en.json", StringComparison.OrdinalIgnoreCase)).Order().ToList();
        Console.WriteLine($"英文（樞紐）：{english.Strings.Count} 條");
        bool any = false;
        foreach (string path in files)
        {
            var doc = Catalog.LoadDocument(path);
            if (code is not null && !doc.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) continue;
            any = true;
            var c = new Catalog(english, doc).Count();
            Console.WriteLine($"{doc.Code,-8} {doc.Language,-14} translated {c.Translated,4}  review {c.Review,4}  untranslated {c.Untranslated,4}  stale {c.Stale,4}   {Path.GetFileName(path)}  {doc.Status}");
        }
        if (!any) Console.WriteLine(code is null ? "（沒有其他語言檔）" : $"（找不到語言 {code}）");
        return 0;
    }

    private static int Check(string dir, string englishPath)
    {
        var english = Catalog.LoadDocument(englishPath);
        int errorCount = 0;
        List<string> paths = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.json").Order().ToList() : [];
        if (paths.Count == 0) { Console.WriteLine($"{dir} 沒有語言檔。"); return 0; }
        foreach (string path in paths)
        {
            var doc = LangFormat.Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), out var parseError);
            if (doc is null) { Console.WriteLine($"[錯誤] {path}：{parseError}"); errorCount++; continue; }
            var (errors, warnings) = Catalog.Check(english, doc);
            errorCount += errors.Count;
            Console.WriteLine($"{Path.GetFileName(path)}（{doc.Code}，{doc.Language}）：{(errors.Count == 0 ? "正常" : errors.Count + " 個錯誤")}{(warnings.Count > 0 ? $"，{warnings.Count} 個提醒" : "")}");
            foreach (var e in errors) Console.WriteLine("  [錯誤] " + e);
            foreach (var w in warnings) Console.WriteLine("  [提醒] " + w);
        }
        return errorCount == 0 ? 0 : 1;
    }

    private static void PrintCounts(Catalog catalog)
    {
        var c = catalog.Count();
        Console.WriteLine($"目前：translated {c.Translated}, review {c.Review}, untranslated {c.Untranslated}（共 {c.Total}）");
    }

    private static string Need(List<string> positional, int index, string what) =>
        positional.ElementAtOrDefault(index) ?? throw new ArgumentException($"缺少參數 <{what}>");

    /// <summary>"--name value" pairs and "--flag" switches (value "" ) plus positional arguments.</summary>
    private static (List<string> Positional, Dictionary<string, string> Options) ParseArgs(string[] args)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) { positional.Add(args[i]); continue; }
            string name = args[i][2..];
            int eq = name.IndexOf('=');
            if (eq > 0) { options[name[..eq]] = name[(eq + 1)..]; continue; }
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) && name is not "pending") options[name] = args[++i];
            else options[name] = "";
        }
        return (positional, options);
    }

    /// <summary>ThumbDock/Lang under the solution root (found by walking up from the current directory).</summary>
    private static string DefaultLangDir()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThumbDock.slnx"))) dir = dir.Parent;
        if (dir is null) throw new ArgumentException("找不到 ThumbDock.slnx，請在方案目錄內執行或加 --lang-dir");
        return Path.Combine(dir.FullName, "ThumbDock", "Lang");
    }
}

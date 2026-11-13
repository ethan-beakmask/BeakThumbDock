namespace ThumbDock.Core;

/// <summary>Command-line options. Without arguments the app starts normally (last layout, tray icon).</summary>
/// <param name="Error">Set when an argument is unknown or incomplete.</param>
internal sealed record CliOptions(string? Layout, string? Monitor, bool ShowHelp, string? Error, bool NoLaunch = false, bool Safe = false)
{
    public static string HelpText => Loc.T(
        "ThumbDock 命令列參數：\r\n\r\n  --layout <名稱>    以指定的佈局啟動（名稱即佈局檔內的 name）\r\n  --monitor <編號>   只用單螢幕模式，縮圖區放在指定螢幕\r\n                     編號 2 代表 \\\\.\\DISPLAY2（設定視窗裡看得到）\r\n  --no-launch        本次不呼叫佈局呼叫設定檔中的程式（類似安全模式，優先於設定）\r\n  --safe             安全啟動：忽略 --layout 與上次的佈局，改用內建「default」佈局\r\n                     （左邊 1×8、寬 200px，不可修改），並且不呼叫程式。設定調壞卡住時使用\r\n  --help, -h, /?     顯示這份說明\r\n\r\n多螢幕多開規則：\r\n  每個實體螢幕最多一個 ThumbDock，各自使用不同的佈局，互不干擾。\r\n  雙螢幕模式的佈局會佔用全部螢幕，執行期間不能再開其他實例。\r\n  只有最先啟動的實例會註冊全域快速鍵。\r\n\r\n範例：\r\n  ThumbDock.exe\r\n  ThumbDock.exe --layout layoutname_1\r\n  ThumbDock.exe --layout layoutname_2 --monitor 1\r\n  ThumbDock.exe --layout layoutname_3 --monitor 2\r\n\r\n錯誤紀錄：logs\\thumbdock.log\r\n\r\n");

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        string? layout = null, monitor = null;
        bool noLaunch = false, safe = false;
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h" or "/?" or "-?") return new CliOptions(null, null, true, null);
            if (arg == "--no-launch") { noLaunch = true; continue; }
            if (arg == "--safe") { safe = true; continue; }
            (string name, string? inline) = Split(arg);
            switch (name)
            {
                case "--layout": if (!TakeValue(args, ref i, inline, out layout)) return Missing(arg); break;
                case "--monitor": if (!TakeValue(args, ref i, inline, out monitor)) return Missing(arg); break;
                default: return new CliOptions(null, null, false, Loc.T("不認得的參數：{0}", arg));
            }
        }
        return new CliOptions(layout, monitor, false, null, noLaunch || safe, safe);
    }

    private static CliOptions Missing(string arg) => new(null, null, false, Loc.T("參數 {0} 需要指定值。", arg));

    private static (string Name, string? Value) Split(string arg)
    {
        int eq = arg.IndexOf('=');
        return arg.StartsWith("--") && eq > 0 ? (arg[..eq], arg[(eq + 1)..]) : (arg, null);
    }

    private static bool TakeValue(IReadOnlyList<string> args, ref int i, string? inline, out string? value)
    {
        value = inline;
        if (value is null && i + 1 < args.Count && !args[i + 1].StartsWith("--")) value = args[++i];
        return !string.IsNullOrWhiteSpace(value);
    }
}

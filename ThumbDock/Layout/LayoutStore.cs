using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ThumbDock.Core;

namespace ThumbDock.Layout;

/// <summary>
/// Reads and writes layouts (config/layouts/*.json) and app settings (config/settings.json) next to the exe (FR-8).
/// Never throws for I/O or bad JSON: problems are logged and treated as "not found".
/// </summary>
internal sealed class LayoutStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Chinese readable in the file
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _configDir;
    private readonly string _layoutDir;

    public LayoutStore(string baseDirectory)
    {
        _configDir = Path.Combine(baseDirectory, "config");
        _layoutDir = Path.Combine(_configDir, "layouts");
        MigrateLegacyDefault();
    }

    // Before 0.10.2 the fixed layout was named "預設"; it is "default" in every language now. Moves the layout file
    // (and its launch file) over; the old file is kept as .migrated.
    private const string LegacyDefaultName = "預設";

    private void MigrateLegacyDefault()
    {
        try
        {
            if (!Directory.Exists(_layoutDir)) return;
            foreach (string path in Directory.EnumerateFiles(_layoutDir, "*.json").ToList())
            {
                var layout = TryRead<LayoutModel>(path);
                if (layout is null || layout.Name != LegacyDefaultName) continue;
                string target = PathFor(DefaultLayouts.ReservedName);
                if (!File.Exists(target))
                {
                    layout.Name = DefaultLayouts.ReservedName;
                    TryWrite(target, layout);
                }
                File.Move(path, path + ".migrated", true);
                string oldLaunch = LaunchPath(LegacyDefaultName), newLaunch = LaunchPath(DefaultLayouts.ReservedName);
                if (File.Exists(oldLaunch) && !File.Exists(newLaunch)) File.Move(oldLaunch, newLaunch);
                AppLog.Info($"migrated layout '{LegacyDefaultName}' to '{DefaultLayouts.ReservedName}'");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("migrate legacy default layout failed", ex);
        }
    }

    /// <summary>
    /// Null = config/settings.json (the main instance). A key such as "DISPLAY2" gives that monitor's own
    /// config/settings.DISPLAY2.json, so instances on different monitors never overwrite each other's settings.
    /// </summary>
    public string? SettingsKey { get; set; }

    private string SettingsPath => Path.Combine(_configDir, string.IsNullOrEmpty(SettingsKey) ? "settings.json" : $"settings.{SettingsKey}.json");

    public List<string> ListNames()
    {
        if (!Directory.Exists(_layoutDir)) return [];
        return Directory.EnumerateFiles(_layoutDir, "*.json")
            .Select(TryRead<LayoutModel>)
            .Where(l => l is not null)
            .Select(l => l!.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.CurrentCulture)
            .ToList();
    }

    public bool Exists(string name) => File.Exists(PathFor(name));

    public LayoutModel? Load(string name)
    {
        var layout = TryRead<LayoutModel>(PathFor(name));
        return layout is null ? null : Normalize(layout);
    }

    public bool Save(LayoutModel layout) => TryWrite(PathFor(layout.Name), layout);

    public bool Delete(string name)
    {
        try { File.Delete(PathFor(name)); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"delete layout '{name}' failed", ex);
            return false;
        }
    }

    // ---- launch configs (config/launch/<layout>.json), hand-edited in Notepad ----

    private static readonly JsonSerializerOptions LaunchOptions = new(Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public string LaunchPath(string layoutName) => Path.Combine(_configDir, "launch", FileNameFor(layoutName));

    public bool LaunchFileExists(string layoutName) => File.Exists(LaunchPath(layoutName));

    /// <summary>
    /// Reads and repairs the launch config of a layout. Config is null when there is no file (Error null)
    /// or the file is unreadable (Error set, with the line number for JSON mistakes).
    /// </summary>
    public (LaunchConfig? Config, List<string> Warnings, string? Error) LoadLaunch(string layoutName)
    {
        string path = LaunchPath(layoutName);
        if (!File.Exists(path)) return (null, [], null);
        try
        {
            var config = JsonSerializer.Deserialize<LaunchConfig>(File.ReadAllText(path), LaunchOptions) ?? new LaunchConfig();
            return (config, config.Normalize(), null);
        }
        catch (JsonException ex)
        {
            string line = ex.LineNumber is { } n ? Loc.T("第 {0} 行", n + 1) : "";
            return (null, [], Loc.T("呼叫設定檔格式錯誤（{0} {1}）：{2}\r\n提示：路徑中的反斜線要寫成兩個，例如 C:\\\\Program Files\\\\app.exe", Path.GetFileName(path), line, ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, [], Loc.T("無法讀取呼叫設定檔{0}：{1}", Path.GetFileName(path), ex.Message));
        }
    }

    // What the editor writes: empty optional fields are left out so the file stays short and readable.
    private sealed class LaunchFile { public List<LaunchEntryFile> Launch { get; set; } = []; }

    private sealed class LaunchEntryFile
    {
        public int Slot { get; set; }
        public string Path { get; set; } = "";
        public string? Args { get; set; }
        public string? WorkDir { get; set; }
        public int DelayMs { get; set; }
        public int WaitSec { get; set; }
        public MatchFile? Match { get; set; }
        public string? Note { get; set; }
    }

    private sealed class MatchFile
    {
        public string? Process { get; set; }
        public string? Class { get; set; }
        public string? TitleRegex { get; set; }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>
    /// Writes the launch config (used by the editor). The previous file is copied to "&lt;name&gt;.json.bak" first,
    /// because writing drops hand-written // comments and unknown keys.
    /// </summary>
    public bool SaveLaunch(string layoutName, LaunchConfig config)
    {
        string path = LaunchPath(layoutName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            var file = new LaunchFile
            {
                Launch = config.Launch.Select(e => new LaunchEntryFile
                {
                    Slot = e.Slot, Path = e.Path, Args = NullIfBlank(e.Args), WorkDir = NullIfBlank(e.WorkDir),
                    DelayMs = e.DelayMs, WaitSec = e.WaitSec, Note = NullIfBlank(e.Note),
                    Match = e.Match is { IsEmpty: false } m
                        ? new MatchFile { Process = NullIfBlank(m.Process), Class = NullIfBlank(m.Class), TitleRegex = NullIfBlank(m.TitleRegex) }
                        : null,
                }).ToList(),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"save launch config {path} failed", ex);
            return false;
        }
    }

    /// <summary>Creates a starter file (empty launch list plus examples that are ignored) if none exists; returns its path.</summary>
    public string? EnsureLaunchTemplate(string layoutName)
    {
        string path = LaunchPath(layoutName);
        if (File.Exists(path)) return path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, LaunchTemplate);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"create launch template {path} failed", ex);
            return null;
        }
    }

    // The examples sit under "_範例", which the app ignores. Move an entry into "launch" to use it.
    private const string LaunchTemplate = """
        {
          "說明": "把要呼叫的程式放進 launch 陣列，格式見 _範例。路徑中的反斜線要寫成兩個。改完存檔，再從系統匣選「啟動時呼叫程式 > 現在呼叫這個佈局的程式」。slot 是槽位編號（從 1 開始）。",
          "launch": [],
          "_範例": [
            {
              "slot": 1,
              "path": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
              "args": "--profile-directory=\"Profile 3\" https://example.com",
              "delayMs": 1500,
              "waitSec": 15,
              "match": { "process": "chrome.exe", "titleRegex": "視窗標題的一部分" }
            },
            {
              "slot": 2,
              "path": "cmd.exe",
              "args": "/k cd /d D:\\work && python run.py"
            }
          ]
        }
        """;

    public AppSettings LoadSettings()
    {
        var settings = TryRead<AppSettings>(SettingsPath) ?? new AppSettings();
        if (settings.LastLayout == LegacyDefaultName) settings.LastLayout = DefaultLayouts.ReservedName;
        return settings;
    }

    public bool SaveSettings(AppSettings settings) => TryWrite(SettingsPath, settings);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Deep copy through JSON (used for "save as").</summary>
    public static LayoutModel Clone(LayoutModel layout) => Deserialize<LayoutModel>(Serialize(layout))!;

    /// <summary>Repairs values a hand-edited or old file may contain.</summary>
    public static LayoutModel Normalize(LayoutModel layout)
    {
        if (string.IsNullOrWhiteSpace(layout.Name)) layout.Name = DefaultLayouts.ReservedName;
        layout.ThumbZone ??= new ThumbZone();
        layout.ThumbZone.SizePx = Math.Clamp(layout.ThumbZone.SizePx, 80, GridSpec.MaxPx);
        layout.Grid?.Normalize();
        layout.Slots ??= [];
        layout.Monitors ??= [];
        var usedIds = new HashSet<int>();
        int nextId = layout.Slots.Count == 0 ? 1 : layout.Slots.Max(s => s.Id) + 1;
        foreach (var slot in layout.Slots)
        {
            if (slot.Id <= 0 || !usedIds.Add(slot.Id)) { slot.Id = nextId++; usedIds.Add(slot.Id); }
            slot.Rect = SlotGeometry.ClampRelative(slot.Rect);
            if (slot.Match is { IsEmpty: true }) slot.Match = null;
            if (slot.LegacyMaximize == true && slot.Place == PlaceMode.None) slot.Place = PlaceMode.Maximize;
            slot.LegacyMaximize = null;
        }
        DefaultLayouts.Enforce(layout);
        return layout;
    }

    /// <summary>File name for a layout name: invalid characters replaced, never empty.</summary>
    public static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return (safe.Length == 0 ? "layout" : safe) + ".json";
    }

    private string PathFor(string name) => Path.Combine(_layoutDir, FileNameFor(name));

    private static T? TryRead<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? Deserialize<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            AppLog.Error($"read {path} failed", ex);
            return null;
        }
    }

    // Write to a temp file first so a crash mid-write never leaves a truncated layout.
    private static bool TryWrite<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(value));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"write {path} failed", ex);
            return false;
        }
    }
}

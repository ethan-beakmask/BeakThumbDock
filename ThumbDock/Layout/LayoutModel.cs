using System.Text.Json.Serialization;

namespace ThumbDock.Layout;

internal enum LayoutMode { Single, Dual }

/// <summary>
/// dock = AppBar strip on one edge; fill = the whole thumbnail monitor.
/// Fill in single mode is the "wall": it covers the only monitor and source windows are shown above it.
/// </summary>
internal enum ZoneType { Dock, Fill }

internal enum DockEdge { Left, Right, Top, Bottom }

/// <summary>
/// What happens when the source window is on another virtual desktop (FR-3).
/// Switch = activate it and let Windows switch desktops; Pull = move it to the current desktop (R-3, falls back to Switch).
/// </summary>
internal enum CrossDesktop { Switch, Pull }

/// <summary>How a slot's window is placed when its thumbnail is clicked (FR-3 placeOnActivate, per slot).</summary>
internal enum PlaceMode
{
    /// <summary>Keep position and size (restored to normal size if it was minimized or maximized).</summary>
    None,
    /// <summary>Maximize on its monitor.</summary>
    Maximize,
    /// <summary>Not maximized; sized to the operation area of its monitor.</summary>
    Fit,
}

/// <summary>A layout file (spec section 7). Mutable: edit mode and the settings dialog change it in place.</summary>
internal sealed class LayoutModel
{
    public string Name { get; set; } = "default";
    public LayoutMode Mode { get; set; } = LayoutMode.Single;

    /// <summary>Device name (e.g. \\.\DISPLAY1) of the monitor without thumbnails. Same as ThumbMonitor in single mode.</summary>
    public string? FocusMonitor { get; set; }

    /// <summary>Device name of the monitor hosting the thumbnail zone. Null = primary monitor.</summary>
    public string? ThumbMonitor { get; set; }

    /// <summary>Monitors connected when the layout was last saved (FR-6), for diagnostics.</summary>
    public List<MonitorSignature> Monitors { get; set; } = [];

    public ThumbZone ThumbZone { get; set; } = new();
    public CrossDesktop CrossDesktop { get; set; } = CrossDesktop.Switch;

    /// <summary>When a bound source window gets minimized, restore it behind all other windows so its thumbnail keeps updating (R-1).</summary>
    public bool AutoRestoreMinimized { get; set; }
    public bool ShowTitles { get; set; } = true;

    /// <summary>
    /// Wall only (single-monitor fill): when returning to the wall, put back the windows it maximized.
    /// A browser hidden under a maximized window stops painting, so its thumbnail would freeze otherwise.
    /// </summary>
    public bool RestoreOnReturn { get; set; } = true;

    /// <summary>n×m grid settings, used by fill zones only. Null = automatic square-ish grid.</summary>
    public GridSpec? Grid { get; set; }

    public List<SlotModel> Slots { get; set; } = [];
}

internal sealed class ThumbZone
{
    public ZoneType Type { get; set; } = ZoneType.Dock;
    public DockEdge Edge { get; set; } = DockEdge.Left;

    /// <summary>Strip thickness for dock zones, in pixels at 100 % scaling (scaled by the monitor's DPI).</summary>
    public int SizePx { get; set; } = DefaultSizePx;

    public const int DefaultSizePx = 200;
}

internal sealed record MonitorSignature(string Device, int Width, int Height);

internal sealed class SlotModel
{
    public int Id { get; set; }

    /// <summary>Position as fractions (0–1) of the thumbnail zone, so it survives resolution and DPI changes (FR-4).</summary>
    public RelRect Rect { get; set; }

    /// <summary>How the slot finds its window after a restart (FR-5). Null = never auto-bind.</summary>
    public MatchRule? Match { get; set; }

    /// <summary>How the window is placed when its thumbnail is clicked.</summary>
    public PlaceMode Place { get; set; }

    /// <summary>Stage 1–2 files stored a boolean "maximize"; read only, converted by LayoutStore.Normalize.</summary>
    [JsonPropertyName("maximize"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyMaximize { get; set; }

    /// <summary>Where the window was when it was bound; it is put back there when needed.</summary>
    public SavedPlacement? Home { get; set; }
}

internal readonly record struct RelRect(double X, double Y, double W, double H);

/// <summary>Window matching rule: process name + window class + title regex. Empty fields match anything.</summary>
internal sealed record MatchRule(string Process = "", string Class = "", string TitleRegex = "")
{
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Process) && string.IsNullOrWhiteSpace(Class) && string.IsNullOrWhiteSpace(TitleRegex);
}

/// <summary>A window's home: monitor device and normal (restored) rectangle in screen pixels.</summary>
internal sealed record SavedPlacement(string Monitor, int X, int Y, int Width, int Height, bool Maximized)
{
    [JsonIgnore]
    public Rectangle Bounds => new(X, Y, Width, Height);
}

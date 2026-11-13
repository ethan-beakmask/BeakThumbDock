using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.Core;

/// <summary>
/// Placement choices for windows that are not bound yet (shown in the picker), keyed by HWND.
/// Once bound, the choice lives on the slot (<see cref="SlotModel.Place"/>) and is saved with the layout.
/// Also captures and describes window positions.
/// </summary>
internal sealed class WindowPreferences
{
    private readonly Dictionary<nint, PlaceMode> _place = new();

    /// <summary>Stored choice, or one matching the window's current state when the user has not chosen.</summary>
    public PlaceMode PlaceFor(nint hwnd) =>
        _place.TryGetValue(hwnd, out var m) ? m
        : User32.GetPlacement(hwnd)?.IsEffectivelyMaximized == true ? PlaceMode.Maximize : PlaceMode.None;

    public void SetPlace(nint hwnd, PlaceMode place) => _place[hwnd] = place;

    public void Prune(IReadOnlySet<nint> keep)
    {
        foreach (var hwnd in _place.Keys.Where(h => !keep.Contains(h)).ToList()) _place.Remove(hwnd);
    }

    /// <summary>Next value when the picker cell is clicked: 不變 → 最大化 → 填滿操作區 → 不變.</summary>
    public static PlaceMode Next(PlaceMode place) => place switch
    {
        PlaceMode.None => PlaceMode.Maximize,
        PlaceMode.Maximize => PlaceMode.Fit,
        _ => PlaceMode.None,
    };

    public static string Label(PlaceMode place) => place switch
    {
        PlaceMode.Maximize => Loc.T("最大化"),
        PlaceMode.Fit => Loc.T("填滿操作區"),
        _ => Loc.T("不變"),
    };

    /// <summary>The window's current position as a home to save with its slot; null if the window is gone.</summary>
    public static SavedPlacement? Capture(nint hwnd)
    {
        if (User32.GetPlacement(hwnd) is not { } p) return null;
        var b = p.NormalBounds;
        return new SavedPlacement(MonitorService.ScreenOf(hwnd, p).DeviceName, b.X, b.Y, b.Width, b.Height, p.IsEffectivelyMaximized);
    }

    /// <summary>Live position text for a window, e.g. "副螢幕 · 最大化".</summary>
    public static string DescribeLive(nint hwnd)
    {
        if (User32.GetPlacement(hwnd) is not { } p) return "";
        var bounds = p.Minimized || p.Maximized ? p.NormalBounds : User32.GetBounds(hwnd) ?? p.NormalBounds;
        return Describe(MonitorService.Label(MonitorService.ScreenOf(hwnd, p)), p, bounds);
    }

    public static string DescribeSaved(SavedPlacement home) =>
        $"{MonitorService.LabelFor(home.Monitor)} · {(home.Maximized ? Loc.T("最大化") : Size(home.Bounds))}";

    /// <summary>Human-readable location, e.g. "副螢幕 · 最大化" or "主螢幕 · 1280×720 @ (100,50)".</summary>
    public static string Describe(string monitorLabel, User32.WindowPlacement placement, Rectangle bounds)
    {
        string state = placement switch
        {
            { Minimized: true, RestoreToMaximized: true } => Loc.T("最小化（原為最大化）"),
            { Minimized: true } => Loc.T("最小化（原為 {0}）", Size(bounds)),
            { Maximized: true } => Loc.T("最大化"),
            _ => Size(bounds),
        };
        return $"{monitorLabel} · {state}";
    }

    private static string Size(Rectangle b) => $"{b.Width}×{b.Height} @ ({b.X},{b.Y})";
}

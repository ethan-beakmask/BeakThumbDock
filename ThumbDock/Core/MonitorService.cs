using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.Core;

internal sealed record MonitorDescriptor(string Device, Rectangle Bounds, bool Primary);

/// <summary>Which monitors the zone and the focus area use, after checking the layout against the connected monitors.</summary>
/// <param name="FallbackReason">Set when a dual layout could not be honoured and single mode is used instead (FR-6).</param>
internal sealed record ZoneResolution(string ThumbDevice, string FocusDevice, bool IsDual, ZoneType Type, DockEdge Edge, string? FallbackReason);

/// <summary>Monitor lookup and layout resolution (FR-6). Screen values are physical pixels (PerMonitorV2).</summary>
internal static class MonitorService
{
    public static IReadOnlyList<MonitorDescriptor> Current() =>
        Screen.AllScreens.Select(s => new MonitorDescriptor(s.DeviceName, s.Bounds, s.Primary)).ToList();

    public static Screen Primary => Screen.PrimaryScreen ?? Screen.AllScreens[0];

    /// <summary>Short stable key of a device name: "\\.\DISPLAY2" becomes "DISPLAY2" (used in file and mutex names).</summary>
    public static string DeviceKey(string device)
    {
        string key = new string(device.Where(char.IsLetterOrDigit).ToArray());
        return key.Length == 0 ? "UNKNOWN" : key;
    }

    /// <summary>Maps "2", "DISPLAY2" or "\\.\DISPLAY2" (any case) to a connected monitor's device name; null if none matches.</summary>
    public static string? ParseDevice(string text, IReadOnlyList<MonitorDescriptor> monitors)
    {
        string t = text.Trim();
        string key = t.All(char.IsDigit) ? "DISPLAY" + t : DeviceKey(t);
        return monitors.FirstOrDefault(m => string.Equals(DeviceKey(m.Device), key, StringComparison.OrdinalIgnoreCase))?.Device;
    }

    public static Screen? ScreenFor(string? device) =>
        device is null ? null : Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device);

    public static List<MonitorSignature> Signatures() =>
        Screen.AllScreens.Select(s => new MonitorSignature(s.DeviceName, s.Bounds.Width, s.Bounds.Height)).ToList();

    /// <summary>Applies the layout's monitor choice to the connected monitors, falling back to single mode when needed.</summary>
    public static ZoneResolution Resolve(LayoutModel layout, IReadOnlyList<MonitorDescriptor> monitors)
    {
        var primary = monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
        var zone = layout.ThumbZone;
        if (layout.Mode == LayoutMode.Single)
        {
            var thumb = Find(monitors, layout.ThumbMonitor);
            string? reason = thumb is null && layout.ThumbMonitor is not null ? Loc.T("找不到縮圖螢幕 {0}，改用主螢幕。", layout.ThumbMonitor) : null;
            string device = (thumb ?? primary).Device;
            return new ZoneResolution(device, device, false, zone.Type, zone.Edge, reason); // fill here = the wall
        }
        string? problem = DualProblem(layout, monitors);
        if (problem is not null)
            return new ZoneResolution(primary.Device, primary.Device, false, ZoneType.Dock, zone.Edge, problem + Loc.T("暫時改為單螢幕模式。"));
        return new ZoneResolution(layout.ThumbMonitor!, layout.FocusMonitor!, true, zone.Type, zone.Edge, null);
    }

    private static string? DualProblem(LayoutModel layout, IReadOnlyList<MonitorDescriptor> monitors)
    {
        if (monitors.Count < 2) return Loc.T("只偵測到一個螢幕，");
        if (Find(monitors, layout.ThumbMonitor) is null) return Loc.T("找不到縮圖螢幕 {0}，", layout.ThumbMonitor ?? Loc.T("（未指定）"));
        if (Find(monitors, layout.FocusMonitor) is null) return Loc.T("找不到焦點螢幕 {0}，", layout.FocusMonitor ?? Loc.T("（未指定）"));
        if (layout.ThumbMonitor == layout.FocusMonitor) return Loc.T("焦點螢幕與縮圖螢幕是同一個，");
        return null;
    }

    private static MonitorDescriptor? Find(IReadOnlyList<MonitorDescriptor> monitors, string? device) =>
        device is null ? null : monitors.FirstOrDefault(m => m.Device == device);

    /// <summary>
    /// Monitor a window is on. A minimized window sits off-screen, so its restore rectangle is used instead.
    /// </summary>
    public static Screen ScreenOf(nint hwnd, User32.WindowPlacement placement) =>
        placement.Minimized ? Screen.FromRectangle(placement.NormalBounds) : Screen.FromHandle(hwnd);

    /// <summary>"主螢幕" / "副螢幕" with two monitors, "螢幕 N" with more.</summary>
    public static string Label(Screen screen)
    {
        if (screen.Primary) return Loc.T("主螢幕");
        var screens = Screen.AllScreens;
        if (screens.Length == 2) return Loc.T("副螢幕");
        int index = Array.FindIndex(screens, s => s.DeviceName == screen.DeviceName);
        return Loc.T("螢幕 {0}", index + 1);
    }

    public static string LabelFor(string device) => ScreenFor(device) is { } s ? Label(s) : Loc.T("已中斷的螢幕");

    /// <summary>e.g. "主螢幕  \\.\DISPLAY1  1920×1080", for the settings dialog.</summary>
    public static string Describe(Screen s) => $"{Label(s)}  {s.DeviceName}  {s.Bounds.Width}×{s.Bounds.Height}";
}

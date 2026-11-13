using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.UI;

/// <summary>What the app starts with. <see cref="Label"/> names the instance (e.g. DISPLAY2) in the tray and the log.</summary>
internal sealed record StartupPlan(LayoutModel Layout, AppSettings Settings, bool FirstRun, string Label, bool NoLaunch, bool Safe = false);

/// <summary>
/// Decides layout and monitors at startup from the command line, the saved settings and the other running
/// instances (one instance per monitor, one per layout). On refusal returns an error text for the user.
/// </summary>
internal static class StartupResolver
{
    public static (StartupPlan? Plan, string? Error) Resolve(CliOptions options, LayoutStore store, InstanceGuard guard)
    {
        var monitors = MonitorService.Current();
        string? hint = null;
        if (options.Monitor is not null)
        {
            hint = MonitorService.ParseDevice(options.Monitor, monitors);
            if (hint is null) return Fail(Loc.T("找不到螢幕「{0}」。目前的螢幕：{1}", options.Monitor, Describe(monitors)));
        }

        bool main = guard.TryClaimMain();
        store.SettingsKey = main || hint is null ? null : MonitorService.DeviceKey(hint);
        var settings = store.LoadSettings();

        var (layout, firstRun, layoutError) = options.Safe ? SafeLayout(options, store) : ChooseLayout(options, settings, store);
        if (layout is null) return Fail(layoutError!);
        if (!guard.TryClaimLayout(layout.Name))
            return Fail(Loc.T("佈局「{0}」正被另一個 ThumbDock 使用。\r\n每個實例需使用不同的佈局，請用 --layout 指定其他佈局。", layout.Name));

        var (devices, monitorError) = ChooseMonitors(layout, hint, monitors, guard);
        if (devices is null) return Fail(monitorError!);

        string label = devices.Count == 1 ? MonitorService.DeviceKey(devices[0]) : "ALL";
        if (!main)
        {
            store.SettingsKey = MonitorService.DeviceKey(devices[0]);
            settings = store.LoadSettings();
        }
        AppLog.Info($"startup: layout='{layout.Name}' monitors=[{string.Join(", ", devices)}] main={main}");
        return (new StartupPlan(layout, settings, firstRun, label, options.NoLaunch, options.Safe), null);
    }

    // --safe: the built-in default layout whatever the saved settings say (a broken or missing file gives a fresh one).
    private static (LayoutModel? Layout, bool FirstRun, string? Error) SafeLayout(CliOptions options, LayoutStore store)
    {
        if (options.Layout is not null) AppLog.Info($"--safe: ignoring --layout '{options.Layout}'");
        return (store.Load(DefaultLayouts.ReservedName) ?? DefaultLayouts.Create(), false, null);
    }

    private static (LayoutModel? Layout, bool FirstRun, string? Error) ChooseLayout(CliOptions options, AppSettings settings, LayoutStore store)
    {
        if (options.Layout is not null)
        {
            if (store.Load(options.Layout) is { } named) return (named, false, null);
            string known = string.Join("、", store.ListNames());
            return (null, false, Loc.T("找不到佈局「{0}」。現有佈局：{1}", options.Layout, known.Length == 0 ? Loc.T("（無）") : known));
        }
        if (settings.LastLayout is { } last && store.Load(last) is { } lastLayout) return (lastLayout, false, null);
        if (store.ListNames().FirstOrDefault() is { } any && store.Load(any) is { } other) return (other, false, null);
        return (DefaultLayouts.Create(), true, null);
    }

    // Claims the monitors the layout needs. Single layouts may move to a free monitor when theirs is taken
    // and no --monitor was given; dual layouts need every monitor.
    private static (List<string>? Devices, string? Error) ChooseMonitors(
        LayoutModel layout, string? hint, IReadOnlyList<MonitorDescriptor> monitors, InstanceGuard guard)
    {
        if (hint is not null)
        {
            if (layout.Mode == LayoutMode.Dual)
                return (null, Loc.T("佈局「{0}」是雙螢幕模式，不能搭配 --monitor（多開時每個實例只能用單螢幕模式）。", layout.Name));
            var (own, error) = Claim([hint], guard, Loc.T("已有另一個 ThumbDock 在使用螢幕 {0}。"));
            if (own is not null) MoveToMonitor(layout, hint);
            return (own, error);
        }

        var zone = MonitorService.Resolve(layout, monitors);
        if (zone.IsDual)
        {
            var all = monitors.Select(m => m.Device).ToList();
            return Claim(all, guard, Loc.T("已有另一個 ThumbDock 在執行，雙螢幕模式需要佔用全部螢幕（衝突：{0}）。"));
        }

        var (devices, _) = Claim([zone.ThumbDevice], guard, "");
        if (devices is not null) return (devices, null);
        if (layout.Mode == LayoutMode.Dual) return (null, Loc.T("佈局「{0}」是雙螢幕模式，但螢幕 {1} 已有另一個 ThumbDock 在使用。", layout.Name, zone.ThumbDevice));
        foreach (var m in monitors.OrderByDescending(m => m.Primary))
        {
            var (free, _) = Claim([m.Device], guard, "");
            if (free is not null)
            {
                MoveToMonitor(layout, m.Device);
                AppLog.Info($"monitor {zone.ThumbDevice} is in use, moved layout '{layout.Name}' to {m.Device}");
                return (free, null);
            }
        }
        return (null, Loc.T("所有螢幕都已有 ThumbDock 在執行，無法再啟動。"));
    }

    private static (List<string>? Devices, string? Error) Claim(List<string> devices, InstanceGuard guard, string conflictText) =>
        guard.TryClaimMonitors(devices, out string? conflict) ? (devices, null) : (null, string.Format(conflictText, conflict));

    // Only done when this instance was told (or forced) to use a monitor other than the one the layout names.
    private static void MoveToMonitor(LayoutModel layout, string device)
    {
        // A dual layout's fill zone would cover the only monitor of a single instance, so it becomes a dock;
        // a layout that was single already (fill = the wall) keeps its type.
        if (layout.Mode == LayoutMode.Dual) layout.ThumbZone.Type = ZoneType.Dock;
        layout.Mode = LayoutMode.Single;
        layout.ThumbMonitor = layout.FocusMonitor = device;
    }

    private static string Describe(IReadOnlyList<MonitorDescriptor> monitors) =>
        string.Join(Loc.T("、"), monitors.Select(m => MonitorService.DeviceKey(m.Device).Replace("DISPLAY", "") + (m.Primary ? Loc.T("（主）") : "")));

    private static (StartupPlan?, string?) Fail(string message)
    {
        AppLog.Error("startup refused: " + message.Replace("\r\n", " "));
        return (null, message);
    }
}

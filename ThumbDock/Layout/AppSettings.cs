namespace ThumbDock.Layout;

/// <summary>App-wide settings (config/settings.json), independent of the layout in use.</summary>
internal sealed class AppSettings
{
    /// <summary>Name of the layout loaded at startup (FR-5).</summary>
    public string? LastLayout { get; set; }

    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>
    /// Allow starting the programs listed in a layout's launch config (at startup and from the tray menu).
    /// Off by default: starting programs can look suspicious to security software. --no-launch overrides it.
    /// </summary>
    public bool AllowLaunch { get; set; }

    /// <summary>
    /// Also run the launch config of the layout you switch to (work-mode switching: documents, development, monitoring).
    /// Only matters when <see cref="AllowLaunch"/> is on. Already-open programs are kept, never closed or started twice.
    /// </summary>
    public bool LaunchOnSwitch { get; set; }

    /// <summary>Wall only: resting the cursor at the middle of the monitor's left edge returns to the wall. Off by default.</summary>
    public bool HotEdgeWall { get; set; }

    /// <summary>The user ticked "don't show again" on the first-use Chrome tip dialog.</summary>
    public bool HideChromeTip { get; set; }
}

/// <summary>Global hotkeys (FR-7). Strings like "Ctrl+Alt+D"; slot hotkeys are <see cref="SlotModifiers"/> + 1…9.</summary>
internal sealed class HotkeySettings
{
    public bool Enabled { get; set; } = true;
    public string ToggleDock { get; set; } = "Ctrl+Alt+D";
    public string SlotModifiers { get; set; } = "Alt+Shift";
}

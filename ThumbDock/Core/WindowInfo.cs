namespace ThumbDock.Core;

/// <summary>A watchable top-level source window (FR-1).</summary>
internal sealed record WindowInfo(
    nint Hwnd,
    string Title,
    string ClassName,
    string ProcessName,
    uint ProcessId,
    Guid? DesktopId,
    bool IsOnCurrentDesktop,
    string DesktopLabel);

/// <summary>Raw facts about a window, used by <see cref="WindowFilter"/>. Pure data so the rule is testable.</summary>
internal readonly record struct WindowTraits(
    bool Visible,
    string Title,
    int ExStyle,
    bool HasOwner,
    bool IsOwnProcess,
    int Cloaked,
    Guid? DesktopId,
    bool? IsOnCurrentDesktop);

using ThumbDock.Native;

namespace ThumbDock.Core;

/// <summary>FR-1 rule deciding whether a top-level window can be watched.</summary>
internal static class WindowFilter
{
    public static bool IsCandidate(in WindowTraits t)
    {
        if (!t.Visible || string.IsNullOrWhiteSpace(t.Title) || t.IsOwnProcess) return false;
        if ((t.ExStyle & User32.WsExToolWindow) != 0) return false;
        if (t.HasOwner && (t.ExStyle & User32.WsExAppWindow) == 0) return false;
        return t.Cloaked == 0 || IsOnOtherDesktop(t);
    }

    /// <summary>
    /// Windows on other virtual desktops are cloaked by the shell but still belong to a desktop.
    /// Cloaked windows without a desktop, or cloaked while on the current desktop
    /// (e.g. suspended UWP frames), are not watchable.
    /// </summary>
    private static bool IsOnOtherDesktop(in WindowTraits t) =>
        (t.Cloaked & Dwm.CloakedShell) != 0 && t.DesktopId is not null && t.IsOnCurrentDesktop == false;
}

using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.UI;

/// <summary>What the window picker needs from the current layout session.</summary>
internal interface IWindowOptions
{
    /// <summary>Binds a window chosen by the user and records a match rule and its home for it.</summary>
    bool BindManually(int slotIndex, WindowInfo window, IReadOnlyList<WindowInfo> allWindows);

    /// <summary>Clears a slot chosen by the user, including its match rule.</summary>
    void ClearSlot(int slotIndex);

    PlaceMode PlaceFor(nint hwnd);

    void SetPlace(nint hwnd, PlaceMode place);

    /// <summary>Recorded home for bound windows, live position otherwise.</summary>
    string LocationText(nint hwnd);
}

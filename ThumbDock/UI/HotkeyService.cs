using ThumbDock.Core;
using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.UI;

/// <summary>
/// Global hotkeys (FR-7): show/hide the thumbnail zone, and modifiers + 1…9 to activate slot N.
/// Uses a message-only window to receive WM_HOTKEY.
/// </summary>
internal sealed class HotkeyService : NativeWindow, IDisposable
{
    private const int ToggleId = 1, SlotBaseId = 100, MaxSlotHotkeys = 9;
    private static readonly nint HwndMessage = -3;
    private readonly List<int> _registered = [];

    public HotkeyService() => CreateHandle(new CreateParams { Parent = HwndMessage });

    public event Action? ToggleDockPressed;

    /// <summary>1-based slot number.</summary>
    public event Action<int>? SlotPressed;

    /// <summary>Re-registers all hotkeys. Returns the ones that could not be registered (taken by another app or invalid).</summary>
    public List<string> Apply(HotkeySettings settings)
    {
        UnregisterAll();
        var failed = new List<string>();
        if (!settings.Enabled) return failed;
        if (!string.IsNullOrWhiteSpace(settings.ToggleDock) && !TryRegister(ToggleId, HotkeyParser.Parse(settings.ToggleDock)))
            failed.Add(settings.ToggleDock);
        if (HotkeyParser.ParseModifiers(settings.SlotModifiers) is { } mods)
        {
            for (int n = 1; n <= MaxSlotHotkeys; n++)
                if (!TryRegister(SlotBaseId + n, new HotkeyDef(mods, Keys.D0 + n))) failed.Add($"{settings.SlotModifiers}+{n}");
        }
        else if (!string.IsNullOrWhiteSpace(settings.SlotModifiers)) failed.Add(settings.SlotModifiers + "+1…9");
        if (failed.Count > 0) AppLog.Info($"hotkeys not registered: {string.Join(", ", failed)}");
        return failed;
    }

    private bool TryRegister(int id, HotkeyDef? def)
    {
        if (def is null || !User32.RegisterHotKey(Handle, id, def.Value.Modifiers, (uint)def.Value.Key)) return false;
        _registered.Add(id);
        return true;
    }

    private void UnregisterAll()
    {
        foreach (int id in _registered) User32.UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == User32.WM_HOTKEY)
        {
            int id = (int)m.WParam;
            if (id == ToggleId) ToggleDockPressed?.Invoke();
            else if (id > SlotBaseId) SlotPressed?.Invoke(id - SlotBaseId);
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}

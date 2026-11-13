using ThumbDock.Native;

namespace ThumbDock.Core;

/// <summary>Tracks the foreground window via EVENT_SYSTEM_FOREGROUND. Must be created on the UI thread.</summary>
internal sealed class ForegroundTracker : IDisposable
{
    private readonly IDisposable _hook;

    public ForegroundTracker()
    {
        Current = User32.GetForegroundWindow();
        _hook = User32.HookForeground(OnForeground);
    }

    public nint Current { get; private set; }

    public event Action<nint>? Changed;

    private void OnForeground(nint hwnd)
    {
        Current = hwnd;
        Changed?.Invoke(hwnd);
    }

    public void Dispose() => _hook.Dispose();
}

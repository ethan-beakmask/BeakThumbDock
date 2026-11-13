using ThumbDock.Core;

namespace ThumbDock.UI;

/// <summary>
/// Polls the cursor (no mouse hook) and raises <see cref="Triggered"/> when it rests in the hot zone of the monitor
/// given by <see cref="MonitorBounds"/>. Does nothing while that returns null.
/// </summary>
internal sealed class HotEdgeWatcher : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Func<Rectangle?> _monitorBounds;
    private HotEdgeState _state = HotEdgeState.Idle;

    public HotEdgeWatcher(Func<Rectangle?> monitorBounds)
    {
        _monitorBounds = monitorBounds;
        _timer.Tick += (_, _) => Poll();
    }

    public event Action? Triggered;

    public bool Enabled
    {
        get => _timer.Enabled;
        set { _state = HotEdgeState.Idle; _timer.Enabled = value; AppLog.Info($"hot edge: {(value ? "on" : "off")}"); }
    }

    private void Poll()
    {
        if (_monitorBounds() is not { } bounds) { _state = HotEdgeState.Idle; return; }
        bool down = Control.MouseButtons != MouseButtons.None;
        (_state, bool trigger) = HotEdgePlanner.Step(_state, Cursor.Position, bounds, down, Environment.TickCount64);
        if (trigger) { AppLog.Info($"hot edge: triggered at {Cursor.Position}"); Triggered?.Invoke(); }
    }

    public void Dispose() => _timer.Dispose();
}

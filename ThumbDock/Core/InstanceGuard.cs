using ThumbDock.Layout;

namespace ThumbDock.Core;

/// <summary>
/// Lets several ThumbDock processes coexist without interfering: one process per physical monitor,
/// one process per layout, and a single "main" process that owns the global hotkeys.
/// Claims are named mutexes (session-local), so a crashed process releases them automatically.
/// All calls must come from the UI thread (mutex ownership is per thread).
/// </summary>
internal sealed class InstanceGuard : IDisposable
{
    private const string Prefix = @"Local\ThumbDock.";
    private readonly Dictionary<string, Mutex> _monitors = new();
    private Mutex? _main, _layout;
    private string? _layoutKey;

    /// <summary>True for the first process: it owns the global hotkeys and the shared settings.json.</summary>
    public bool IsMain => _main is not null;

    public bool TryClaimMain() => IsMain || (_main = TryAcquire(Prefix + "Main")) is not null;

    /// <summary>
    /// Claims exactly these monitors (devices like \\.\DISPLAY2) for this process.
    /// On failure nothing changes and <paramref name="conflict"/> names the first monitor in use elsewhere.
    /// </summary>
    public bool TryClaimMonitors(IEnumerable<string> devices, out string? conflict)
    {
        conflict = null;
        var wanted = devices.Select(MonitorService.DeviceKey).Distinct().ToList();
        var acquired = new List<string>();
        foreach (string key in wanted.Where(k => !_monitors.ContainsKey(k)))
        {
            if (TryAcquire(Prefix + "M." + key) is { } mutex) { _monitors[key] = mutex; acquired.Add(key); }
            else
            {
                conflict = key;
                foreach (string k in acquired) Release(_monitors, k);
                return false;
            }
        }
        foreach (string key in _monitors.Keys.Except(wanted).ToList()) Release(_monitors, key);
        return true;
    }

    /// <summary>Claims the layout name for this process (replacing the previous claim). False when another process uses it.</summary>
    public bool TryClaimLayout(string name)
    {
        string key = LayoutKey(name);
        if (key == _layoutKey) return true;
        if (TryAcquire(Prefix + "L." + key) is not { } mutex) return false;
        ReleaseOne(_layout);
        _layout = mutex;
        _layoutKey = key;
        return true;
    }

    /// <summary>True when this process owns the layout or nobody does.</summary>
    public bool IsLayoutAvailable(string name)
    {
        string key = LayoutKey(name);
        if (key == _layoutKey) return true;
        if (TryAcquire(Prefix + "L." + key) is not { } probe) return false;
        ReleaseOne(probe);
        return true;
    }

    public static string LayoutKey(string name)
    {
        string key = LayoutStore.FileNameFor(name).ToLowerInvariant();
        return key.Length > 120 ? key[..120] : key;
    }

    private static Mutex? TryAcquire(string name)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(false, name);
            if (mutex.WaitOne(0)) return mutex;
        }
        catch (AbandonedMutexException)
        {
            return mutex; // the previous owner crashed; we own it now
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            AppLog.Error($"mutex '{name}' failed", ex);
        }
        mutex?.Dispose();
        return null;
    }

    private static void Release(Dictionary<string, Mutex> map, string key)
    {
        if (map.Remove(key, out var mutex)) ReleaseOne(mutex);
    }

    private static void ReleaseOne(Mutex? mutex)
    {
        if (mutex is null) return;
        try { mutex.ReleaseMutex(); }
        catch (ApplicationException) { /* not owned by this thread: the OS frees it at exit */ }
        mutex.Dispose();
    }

    public void Dispose()
    {
        foreach (string key in _monitors.Keys.ToList()) Release(_monitors, key);
        ReleaseOne(_layout);
        ReleaseOne(_main);
        _layout = _main = null;
        _layoutKey = null;
    }
}

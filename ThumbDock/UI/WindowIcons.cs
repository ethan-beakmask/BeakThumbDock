using ThumbDock.Native;

namespace ThumbDock.UI;

/// <summary>Small icons for the window list, cached per window. Falls back to the exe icon, then a generic icon.</summary>
internal sealed class WindowIcons : IDisposable
{
    private readonly Dictionary<nint, Image> _cache = new();
    private readonly Image _fallback = SystemIcons.Application.ToBitmap();

    public Image Get(nint hwnd, uint processId)
    {
        if (_cache.TryGetValue(hwnd, out var image)) return image;
        image = FromWindow(hwnd) ?? FromProcess(processId) ?? _fallback;
        _cache[hwnd] = image;
        return image;
    }

    /// <summary>Drops cached icons of windows that no longer exist.</summary>
    public void Prune(IReadOnlySet<nint> alive)
    {
        foreach (var hwnd in _cache.Keys.Where(h => !alive.Contains(h)).ToList())
        {
            if (!ReferenceEquals(_cache[hwnd], _fallback)) _cache[hwnd].Dispose();
            _cache.Remove(hwnd);
        }
    }

    private static Image? FromWindow(nint hwnd)
    {
        nint handle = User32.GetWindowIconHandle(hwnd);
        if (handle == 0) return null;
        try
        {
            using var icon = Icon.FromHandle(handle);
            return icon.ToBitmap(); // copy: the HICON belongs to the other process's window
        }
        catch (ArgumentException) { return null; }
    }

    private static Image? FromProcess(uint processId)
    {
        try
        {
            // Not Process.MainModule: that needs PROCESS_VM_READ on the other process (see ProcessPath).
            string? path = ProcessPath.Of(processId);
            using var icon = path is null ? null : Icon.ExtractAssociatedIcon(path);
            return icon?.ToBitmap();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or IOException
                                       or UnauthorizedAccessException)
        {
            return null; // elevated, packaged or exited process
        }
    }

    public void Dispose()
    {
        foreach (var image in _cache.Values.Where(i => !ReferenceEquals(i, _fallback))) image.Dispose();
        _cache.Clear();
        _fallback.Dispose();
    }
}

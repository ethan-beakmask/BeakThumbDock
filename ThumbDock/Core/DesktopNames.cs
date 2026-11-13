using Microsoft.Win32;

namespace ThumbDock.Core;

/// <summary>
/// Best-effort display names for virtual desktops, read from the user's Explorer registry (read only).
/// Returns null when nothing is known; callers fall back to a generic label.
/// </summary>
internal sealed class DesktopNames
{
    private const string RootKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private Dictionary<Guid, string> _names = new();

    public void Reload()
    {
        try { _names = Load(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            _names = new();
        }
    }

    public string? NameOf(Guid id) => _names.TryGetValue(id, out var name) ? name : null;

    private static Dictionary<Guid, string> Load()
    {
        var result = new Dictionary<Guid, string>();
        using var root = Registry.CurrentUser.OpenSubKey(RootKey);
        if (root?.GetValue("VirtualDesktopIDs") is not byte[] raw) return result;
        var ids = ParseIds(raw);
        for (int i = 0; i < ids.Count; i++)
        {
            using var key = root.OpenSubKey($@"Desktops\{ids[i]:B}");
            string? custom = key?.GetValue("Name") as string;
            result[ids[i]] = string.IsNullOrWhiteSpace(custom) ? Loc.T("桌面 {0}", i + 1) : custom;
        }
        return result;
    }

    /// <summary>VirtualDesktopIDs is a concatenation of 16-byte GUIDs in desktop order.</summary>
    public static List<Guid> ParseIds(byte[] raw)
    {
        var ids = new List<Guid>();
        for (int offset = 0; offset + 16 <= raw.Length; offset += 16)
            ids.Add(new Guid(raw.AsSpan(offset, 16)));
        return ids;
    }
}

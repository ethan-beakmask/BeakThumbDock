using System.Text.Json;

namespace ThumbDock.Core;

/// <summary>One browser profile: <see cref="Folder"/> is what --profile-directory takes ("Default", "Profile 3").</summary>
internal sealed record BrowserProfile(string Folder, string Name, string UserName)
{
    public string Display => string.IsNullOrWhiteSpace(UserName)
        ? $"{Folder} — {Name}"
        : $"{Folder} — {Name}（{UserName}）";
}

/// <summary>
/// Reads the profile list from the browser's "Local State" file (profile.info_cache). The file is not a documented
/// interface but is what community tools use; on any problem the list is empty and the editor falls back to typing.
/// </summary>
internal static class BrowserProfiles
{
    public static List<BrowserProfile> Load(LaunchKind kind)
    {
        string? path = LocalStatePath(kind);
        if (path is null || !File.Exists(path)) return [];
        try
        {
            // Chrome keeps the file open while running; read it with sharing allowed.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"read {path} failed", ex);
            return [];
        }
    }

    public static List<BrowserProfile> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("profile", out var profile) ||
                !profile.TryGetProperty("info_cache", out var cache) || cache.ValueKind != JsonValueKind.Object) return [];
            return cache.EnumerateObject()
                .Select(p => new BrowserProfile(p.Name, Str(p.Value, "name"), Str(p.Value, "user_name")))
                .OrderBy(p => p.Folder, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string? LocalStatePath(LaunchKind kind)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return kind switch
        {
            LaunchKind.Chrome => Path.Combine(local, "Google", "Chrome", "User Data", "Local State"),
            LaunchKind.Edge => Path.Combine(local, "Microsoft", "Edge", "User Data", "Local State"),
            _ => null,
        };
    }
}

using System.Reflection;

namespace ThumbDock.Core;

internal static class AppInfo
{
    /// <summary>"0.3.0" (major.minor.build of the assembly version).</summary>
    public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";
}

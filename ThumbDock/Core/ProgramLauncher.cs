using System.ComponentModel;
using System.Diagnostics;

namespace ThumbDock.Core;

/// <summary>Starts the programs of a launch config as the current user (never elevated). Never throws.</summary>
internal static class ProgramLauncher
{
    /// <summary>Returns null on success, otherwise a message for the log and the user.</summary>
    public static string? Start(LaunchEntry entry)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = Environment.ExpandEnvironmentVariables(entry.Path),
                Arguments = entry.Args, // not expanded: cmd.exe expands its own %VARS%
                UseShellExecute = true, // finds programs on PATH and runs .lnk/.bat like Explorer does
            };
            if (!string.IsNullOrWhiteSpace(entry.WorkDir)) info.WorkingDirectory = Environment.ExpandEnvironmentVariables(entry.WorkDir);
            using var process = Process.Start(info);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or FileNotFoundException or PlatformNotSupportedException)
        {
            return ex.Message;
        }
    }
}

using System.Text;

namespace ThumbDock.Core;

/// <summary>Small append-only log in logs/ next to the exe, shared by all running instances. Logging never throws.</summary>
internal static class AppLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "logs", "thumbdock.log");

    /// <summary>Instance label (e.g. DISPLAY2) written into every line so interleaved instances can be told apart.</summary>
    public static string Tag { get; set; } = "";

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                RotateIfLarge();
                string tag = Tag.Length == 0 ? "" : $"[{Tag}] ";
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {tag}{message}{Environment.NewLine}";
                // FileShare.ReadWrite: several instances append to the same file.
                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(Encoding.UTF8.GetBytes(line));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging is best effort (e.g. exe placed in a read-only folder).
        }
    }

    private static void RotateIfLarge()
    {
        var info = new FileInfo(FilePath);
        if (info.Exists && info.Length > MaxBytes) File.Move(FilePath, FilePath + ".old", overwrite: true);
    }
}

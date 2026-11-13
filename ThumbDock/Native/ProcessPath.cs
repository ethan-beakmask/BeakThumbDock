using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace ThumbDock.Native;

/// <summary>
/// Full exe path of another process. Asks only for PROCESS_QUERY_LIMITED_INFORMATION: Process.MainModule would open
/// the process with PROCESS_VM_READ and walk its modules, which endpoint protection (EDR/XDR) treats as suspicious.
/// </summary>
internal static unsafe class ProcessPath
{
    private const int MaxChars = 1024;

    /// <summary>Null when the process is gone or cannot be opened (protected or exited process).</summary>
    public static string? Of(uint processId)
    {
        if (processId == 0) return null;
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid) return null;
        Span<char> buffer = stackalloc char[MaxChars];
        uint size = MaxChars;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size) && size > 0
            ? new string(buffer[..(int)size])
            : null;
    }
}

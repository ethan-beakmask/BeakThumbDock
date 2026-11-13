using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace ThumbDock.Native;

/// <summary>
/// Whether a process runs elevated (administrator). A normal-rights ThumbDock cannot move or resize an elevated
/// window (UIPI blocks it silently), but it can still show its thumbnail and bring it to the front.
/// </summary>
internal static unsafe class ProcessElevation
{
    private static readonly Lazy<bool> Self = new(() =>
    {
        using var token = OpenToken(PInvoke.GetCurrentProcess_SafeHandle());
        return token is not null && IsElevated(token) == true;
    });

    /// <summary>True when ThumbDock itself runs elevated, so it can move any window.</summary>
    public static bool AppIsElevated => Self.Value;

    /// <summary>True when ThumbDock is not elevated and the window's process is, so moving it will not work.</summary>
    public static bool BlocksMoving(nint hwnd) => !AppIsElevated && OfWindow(hwnd) == true;

    /// <summary>Elevation of the process owning the window; null when it cannot be determined.</summary>
    public static bool? OfWindow(nint hwnd)
    {
        if (PInvoke.GetWindowThreadProcessId((Windows.Win32.Foundation.HWND)hwnd, out uint pid) == 0 || pid == 0) return null;
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid) return null;
        using var token = OpenToken(process);
        return token is null ? null : IsElevated(token);
    }

    private static Microsoft.Win32.SafeHandles.SafeFileHandle? OpenToken(System.Runtime.InteropServices.SafeHandle process) =>
        PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token) ? token : null;

    private static bool? IsElevated(Microsoft.Win32.SafeHandles.SafeFileHandle token)
    {
        TOKEN_ELEVATION elevation;
        return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, &elevation, (uint)sizeof(TOKEN_ELEVATION), out _)
            ? elevation.TokenIsElevated != 0 : null;
    }
}

using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using static ThumbDock.Spike.Native;

namespace ThumbDock.Spike;

internal static class Log
{
    private static readonly object Gate = new();
    public static string FilePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "spike-log.txt");
    public static event Action<string>? Written;

    public static void W(string msg)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff}  {msg}";
        lock (Gate) File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
        Written?.Invoke(line);
    }
}

internal static class Probe
{
    // Hash of the composed screen pixels in a region. Changes over time => the thumbnail is live.
    public static ulong HashScreen(Rectangle r)
    {
        using var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        var data = bmp.LockBits(new Rectangle(Point.Empty, r.Size), ImageLockMode.ReadOnly, bmp.PixelFormat);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            ulong h = 14695981039346656037UL;
            foreach (byte b in bytes) { h ^= b; h *= 1099511628211UL; }
            return h;
        }
        finally { bmp.UnlockBits(data); }
    }

    public static RECT FrameBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out RECT rc, Marshal.SizeOf<RECT>()) == 0) return rc;
        GetWindowRect(hwnd, out rc);
        return rc;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
}

internal static class Proc
{
    public static string NameOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return $"pid {pid} (無法開啟)";
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? Path.GetFileName(sb.ToString()) : $"pid {pid}";
        }
        finally { CloseHandle(h); }
    }

    // null = could not determine (usually means we lack access => likely elevated).
    public static bool? IsElevated(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(h, TOKEN_QUERY, out IntPtr tok)) return null;
            try { return GetTokenInformation(tok, TokenElevation, out int e, 4, out _) ? e != 0 : null; }
            finally { CloseHandle(tok); }
        }
        finally { CloseHandle(h); }
    }

    public static bool IsOwnProcess(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == Environment.ProcessId;
    }
}

internal sealed record WinRow(IntPtr Hwnd, string Title, string Process, int Cloaked, Guid? Desktop, bool? OnCurrent, bool? Elevated)
{
    public string Kind => Cloaked switch
    {
        0 => "可見",
        _ when Desktop is not null && OnCurrent == false => "其他虛擬桌面",
        _ when Desktop is null => "疑似暫停的 UWP",
        _ => $"cloaked={Cloaked}",
    };
}

internal static class Windows
{
    public static List<WinRow> Enumerate(bool includeOwn = false)
    {
        var list = new List<WinRow>();
        EnumWindows((hwnd, _) =>
        {
            if (IsCandidate(hwnd, includeOwn)) list.Add(Describe(hwnd));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static bool IsCandidate(IntPtr hwnd, bool includeOwn)
    {
        if (!IsWindowVisible(hwnd)) return false;
        if (Native.Text(hwnd).Length == 0) return false;
        if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0) return false;
        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        return includeOwn || !Proc.IsOwnProcess(hwnd);
    }

    public static WinRow Describe(IntPtr hwnd) => new(
        hwnd, Native.Text(hwnd), Proc.NameOf(hwnd), Cloaked(hwnd),
        VDesk.DesktopOf(hwnd), VDesk.OnCurrent(hwnd), Proc.IsElevated(hwnd));

    public static IntPtr FindByTitle(string title) =>
        Enumerate(includeOwn: true).FirstOrDefault(r => r.Title == title)?.Hwnd ?? IntPtr.Zero;

    public static async Task<IntPtr> WaitForTitle(string title, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            IntPtr h = FindByTitle(title);
            if (h != IntPtr.Zero) return h;
            await Task.Delay(100);
        }
        return IntPtr.Zero;
    }
}

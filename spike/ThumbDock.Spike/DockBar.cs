using System.Runtime.InteropServices;
using static ThumbDock.Spike.Native;

namespace ThumbDock.Spike;

// Left-edge AppBar hosting up to 4 live DWM thumbnails.
internal sealed class DockBar : Form
{
    public const int SlotCount = 4;
    private const int Gap = 8, TitleH = 20;

    private readonly bool _toolWindow, _noActivate;
    private readonly int _widthPx;
    private readonly IntPtr[] _thumbs = new IntPtr[SlotCount];
    private readonly IntPtr[] _sources = new IntPtr[SlotCount];
    private readonly string[] _titles = new string[SlotCount];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private uint _callbackMsg;
    private bool _registered;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool MaximizeOnActivate { get; set; }
    public int ActivatedCount { get; private set; }
    public int ClickCount { get; private set; }

    public DockBar(bool toolWindow, bool noActivate, int widthPx)
    {
        _toolWindow = toolWindow;
        _noActivate = noActivate;
        _widthPx = widthPx;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 32, 32);
        Text = "ThumbDock Spike Dock";
        DoubleBuffered = true;
        _timer.Tick += (_, _) => Refresh_();
        _timer.Start();
    }

    protected override bool ShowWithoutActivation => _noActivate;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)WS_EX_TOPMOST;
            if (_toolWindow) cp.ExStyle |= (int)WS_EX_TOOLWINDOW;
            if (_noActivate) cp.ExStyle |= (int)WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _callbackMsg = RegisterWindowMessage("ThumbDockSpikeAppBarMsg");
        var abd = NewData();
        abd.uCallbackMessage = _callbackMsg;
        _registered = SHAppBarMessage(ABM_NEW, ref abd) != IntPtr.Zero;
        Log.W($"AppBar ABM_NEW 結果={_registered} toolWindow={_toolWindow} noActivate={_noActivate}");
        SetPos();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Environment.GetEnvironmentVariable("SPIKE_SETPOS_ON_SHOWN") == "1") SetPos();
    }

    public Rectangle ScreenBounds => RectangleToScreen(ClientRectangle);

    private APPBARDATA NewData() => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = Handle };

    private void SetPos()
    {
        var b = Screen.PrimaryScreen!.Bounds;
        var abd = NewData();
        abd.uEdge = ABE_LEFT;
        abd.rc = new RECT(b.Left, b.Top, b.Left + _widthPx, b.Bottom);
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        abd.rc.Right = abd.rc.Left + _widthPx;
        SHAppBarMessage(ABM_SETPOS, ref abd);
        MoveWindow(Handle, abd.rc.Left, abd.rc.Top, abd.rc.Width, abd.rc.Height, true);
        Log.W($"AppBar SETPOS rc={abd.rc} visible={IsWindowVisible(Handle)} 工作區={WorkArea()}");
        LayoutThumbs();
    }

    public void RemoveAppBar()
    {
        if (!_registered || !IsHandleCreated) return;
        var abd = NewData();
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
        Log.W("AppBar ABM_REMOVE");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0021 /*WM_MOUSEACTIVATE*/ && _noActivate && Environment.GetEnvironmentVariable("SPIKE_MA") != "0")
        {
            m.Result = 3; // MA_NOACTIVATE
            return;
        }
        if (m.Msg == 0x0006 /*WM_ACTIVATE*/ && _registered)
        {
            if ((m.WParam.ToInt64() & 0xFFFF) != 0) { ActivatedCount++; Log.W($"  [dock] 收到 WM_ACTIVATE (被啟用) 第 {ActivatedCount} 次"); }
            var abd = NewData();
            SHAppBarMessage(ABM_ACTIVATE, ref abd);
        }
        else if (m.Msg == 0x0047 /*WM_WINDOWPOSCHANGED*/ && _registered)
        {
            var abd = NewData();
            SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref abd);
        }
        else if (_registered && m.Msg == _callbackMsg && m.WParam.ToInt32() == ABN_POSCHANGED)
        {
            SetPos();
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        for (int i = 0; i < SlotCount; i++) Clear(i);
        RemoveAppBar();
        _timer.Dispose();
        base.OnFormClosed(e);
    }

    public IntPtr SourceOf(int slot) => _sources[slot];

    public int Bind(int slot, IntPtr src)
    {
        Clear(slot);
        int hr = DwmRegisterThumbnail(Handle, src, out IntPtr thumb);
        if (hr != 0) { Log.W($"槽位{slot + 1} DwmRegisterThumbnail 失敗 hr=0x{hr:X8}"); return hr; }
        _thumbs[slot] = thumb;
        _sources[slot] = src;
        _titles[slot] = Native.Text(src);
        LayoutThumbs();
        Invalidate();
        Log.W($"槽位{slot + 1} 綁定 0x{src:X} \"{_titles[slot]}\"");
        return 0;
    }

    public void Clear(int slot)
    {
        if (_thumbs[slot] != IntPtr.Zero) DwmUnregisterThumbnail(_thumbs[slot]);
        _thumbs[slot] = IntPtr.Zero;
        _sources[slot] = IntPtr.Zero;
        _titles[slot] = "";
        Invalidate();
    }

    public Rectangle SlotRect(int i)
    {
        int h = (ClientSize.Height - Gap * (SlotCount + 1)) / SlotCount;
        return new Rectangle(Gap, Gap + i * (h + Gap), ClientSize.Width - 2 * Gap, h);
    }

    public Rectangle ThumbArea(int i)
    {
        var r = SlotRect(i);
        return new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - TitleH - 4);
    }

    public Rectangle ThumbAreaOnScreen(int i) => RectangleToScreen(ThumbArea(i));

    private void LayoutThumbs()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_thumbs[i] == IntPtr.Zero) continue;
            if (DwmQueryThumbnailSourceSize(_thumbs[i], out SIZE s) != 0 || s.cx <= 0 || s.cy <= 0) continue;
            var area = ThumbArea(i);
            double scale = Math.Min((double)area.Width / s.cx, (double)area.Height / s.cy);
            int w = (int)(s.cx * scale), h = (int)(s.cy * scale);
            int x = area.X + (area.Width - w) / 2, y = area.Y + (area.Height - h) / 2;
            var p = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY,
                rcDestination = new RECT(x, y, x + w, y + h),
                opacity = 255,
                fVisible = true,
            };
            DwmUpdateThumbnailProperties(_thumbs[i], ref p);
        }
    }

    private void Refresh_()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_sources[i] != IntPtr.Zero && !IsWindow(_sources[i]))
            {
                Log.W($"槽位{i + 1} 來源視窗已關閉 → 等待中");
                Clear(i);
            }
        }
        LayoutThumbs();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        IntPtr fg = GetForegroundWindow();
        for (int i = 0; i < SlotCount; i++)
        {
            var r = SlotRect(i);
            bool active = _sources[i] != IntPtr.Zero && _sources[i] == fg;
            using var pen = new Pen(active ? Color.Orange : Color.FromArgb(70, 70, 70), active ? 3 : 1);
            e.Graphics.DrawRectangle(pen, r);
            string label = _sources[i] == IntPtr.Zero ? $"{i + 1}. 等待中" : $"{i + 1}. {_titles[i]}";
            var tr = new Rectangle(r.X + 4, r.Bottom - TitleH, r.Width - 8, TitleH);
            TextRenderer.DrawText(e.Graphics, label, Font, tr, Color.Gainsboro, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        for (int i = 0; i < SlotCount; i++)
        {
            if (!SlotRect(i).Contains(e.Location)) continue;
            ClickCount++;
            if (_sources[i] != IntPtr.Zero) _ = ActivateAsync(i, _sources[i]);
            return;
        }
    }

    private async Task ActivateAsync(int slot, IntPtr src)
    {
        Guid? desk = VDesk.DesktopOf(src);
        bool? onCur = VDesk.OnCurrent(src);
        if (IsIconic(src)) ShowWindow(src, SW_RESTORE);
        bool ok = SetForegroundWindow(src);
        int err = Marshal.GetLastWin32Error();
        if (MaximizeOnActivate) ShowWindow(src, SW_MAXIMIZE);
        await Task.Delay(500);
        IntPtr fg = GetForegroundWindow();
        Log.W($"點擊槽位{slot + 1}: 來源桌面={VDesk.Short(desk)} 在目前桌面={onCur} SetForegroundWindow={ok} err={err} " +
              $"500ms後前景是來源={fg == src} 來源現在在目前桌面={VDesk.OnCurrent(src)} 本 dock 被啟用次數={ActivatedCount}");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
}

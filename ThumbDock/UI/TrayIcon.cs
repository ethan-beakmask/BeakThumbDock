namespace ThumbDock.UI;

/// <summary>Notification-area icon (FR-7). The owner fills <see cref="Menu"/> when it opens.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly string _title;

    /// <param name="title">"ThumbDock" or "ThumbDock · DISPLAY2": tells instances on different monitors apart.</param>
    public TrayIcon(string title = "ThumbDock")
    {
        _title = title;
        Menu.Opening += (_, e) => { MenuOpening?.Invoke(Menu); e.Cancel = false; };
        Menu.Items.Add("…"); // placeholder so the menu opens; rebuilt in MenuOpening
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = title.Length > 63 ? title[..63] : title, // NotifyIcon.Text limit
            ContextMenuStrip = Menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => DoubleClicked?.Invoke();
    }

    public ContextMenuStrip Menu { get; } = new();

    /// <summary>Raised before the menu is shown so the owner can rebuild its items.</summary>
    public event Action<ContextMenuStrip>? MenuOpening;

    public event Action? DoubleClicked;

    // Every balloon is also logged: they vanish after a few seconds and are easy to miss when debugging.
    public void Notify(string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        Core.AppLog.Info($"notice ({icon}): {text.Replace("\r\n", " | ")}");
        _icon.ShowBalloonTip(5000, _title, text, icon);
    }

    // The icon is embedded in the exe (app.ico); fall back to the stock icon if it cannot be read.
    private static Icon LoadIcon()
    {
        try
        {
            using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("ThumbDock.app.ico");
            if (stream is not null) return new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            Core.AppLog.Error("load tray icon failed", ex);
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        Menu.Dispose();
    }
}

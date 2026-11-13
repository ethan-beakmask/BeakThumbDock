namespace ThumbDock.Spike;

// A source window whose content changes every 100 ms, so live thumbnails are detectable.
internal sealed class TickerForm : Form
{
    private readonly Label _label = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private int _n;

    public TickerForm(string name, int x, int y)
    {
        Text = $"Spike Ticker {name}";
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(x, y, 480, 320);
        _label.Font = new Font("Segoe UI", 48, FontStyle.Bold);
        Controls.Add(_label);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        _n++;
        _label.Text = $"{Text[^1]} {_n}";
        BackColor = Color.FromArgb((_n * 37) % 256, (_n * 67) % 256, (_n * 97) % 256);
    }
}

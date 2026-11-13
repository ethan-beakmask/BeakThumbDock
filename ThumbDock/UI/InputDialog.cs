using ThumbDock.Core;

namespace ThumbDock.UI;

/// <summary>Minimal single-line text prompt (layout names).</summary>
internal static class InputDialog
{
    /// <summary>Returns the trimmed text, or null when cancelled or empty.</summary>
    public static string? Ask(string title, string prompt, string initial = "")
    {
        using var form = new Form
        {
            Text = title, AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(380, 120),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, TopMost = true,
        };
        var label = new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true };
        var box = new TextBox { Text = initial, Left = 12, Top = 38, Width = 356 };
        var ok = new Button { Text = Loc.T("確定"), DialogResult = DialogResult.OK, Left = 212, Top = 78, Width = 75 };
        var cancel = new Button { Text = Loc.T("取消"), DialogResult = DialogResult.Cancel, Left = 293, Top = 78, Width = 75 };
        form.Controls.AddRange([label, box, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        if (form.ShowDialog() != DialogResult.OK) return null;
        string text = box.Text.Trim();
        return text.Length == 0 ? null : text;
    }
}

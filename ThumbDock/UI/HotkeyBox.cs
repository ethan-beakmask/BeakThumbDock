using ThumbDock.Core;

namespace ThumbDock.UI;

/// <summary>Text box that records a key combination when pressed. Backspace or Delete clears it.</summary>
internal sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode is Keys.Back or Keys.Delete && e.Modifiers == Keys.None) { Text = ""; return; }
        if (HotkeyParser.FromKeyData(e.KeyData) is { } def && HotkeyParser.Parse(HotkeyParser.Format(def)) is not null)
            Text = HotkeyParser.Format(def);
    }

    // Let Tab etc. reach OnKeyDown instead of moving focus while recording.
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) != Keys.Tab || keyData.HasFlag(Keys.Control);
}

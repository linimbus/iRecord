// iRecord - hotkey capture text box.
// Converts the next key chord into canonical display text.
using iRecord.App;

namespace iRecord.UI;

/// <summary>
/// Read-only text box that records a key chord for SettingsForm.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        TextAlign = HorizontalAlignment.Center;
        Cursor = Cursors.Hand;
        BackColor = Color.White;
    }

    protected override bool IsInputKey(Keys keyData)
        => (keyData & Keys.KeyCode) != Keys.Tab;

    /// <summary>Captures the next chord; Backspace/Delete clears the assignment.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
        {
            Text = string.Empty;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode is Keys.Tab)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.KeyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            e.SuppressKeyPress = true;
            return;
        }

        Text = HotkeyParser.FromKeys(e.KeyCode, e.Modifiers);
        e.SuppressKeyPress = true;
    }
}

using System.Windows.Forms;

namespace iRecord.App;

public readonly record struct Hotkey(uint Modifiers, uint Vk, string Text);

public static class HotkeyParser
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint modifiers = 0;
        uint vk = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                case "super":
                case "meta":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    vk = ParseKey(raw);
                    break;
            }
        }

        if (vk == 0) return false;
        hotkey = new Hotkey(modifiers, vk, BuildText(modifiers, vk));
        return true;
    }

    public static string FromKeys(Keys key, Keys modifiers)
    {
        uint mods = 0;
        if ((modifiers & Keys.Control) != 0) mods |= NativeMethods.MOD_CONTROL;
        if ((modifiers & Keys.Alt) != 0) mods |= NativeMethods.MOD_ALT;
        if ((modifiers & Keys.Shift) != 0) mods |= NativeMethods.MOD_SHIFT;
        return BuildText(mods, (uint)key);
    }

    private static uint ParseKey(string key)
    {
        if (key.Length >= 2 && (key[0] == 'F' || key[0] == 'f') &&
            int.TryParse(key[1..], out int f) && f is >= 1 and <= 24)
            return (uint)(0x70 + f - 1);

        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= '0' and <= '9') return c;
        }

        return key.ToLowerInvariant() switch
        {
            "printscreen" or "prtsc" => 0x2C,
            "pause" => 0x13,
            "insert" => 0x2D,
            "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "space" => 0x20,
            "tab" => 0x09,
            "enter" or "return" => 0x0D,
            "backspace" => 0x08,
            "escape" or "esc" => 0x1B,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            _ => 0
        };
    }

    public static string BuildText(uint modifiers, uint vk)
    {
        var parts = new List<string>(5);
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName(vk));
        return string.Join("+", parts);
    }

    public static string KeyName(uint vk)
    {
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();

        return vk switch
        {
            0x2C => "PrintScreen",
            0x13 => "Pause",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x1B => "Escape",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            _ => ((Keys)vk).ToString()
        };
    }
}

public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Dictionary<int, string> _actions = new();
    private int _nextId = 1;

    public event Action<string>? Pressed;

    public HotkeyWindow()
    {
        var cp = new CreateParams { Caption = "iRecord.Hotkeys" };
        CreateHandle(cp);
    }

    public bool Register(string action, Hotkey hotkey, out int id)
    {
        id = _nextId;
        if (NativeMethods.RegisterHotKey(Handle, id, hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, hotkey.Vk))
        {
            _actions[id] = action;
            _nextId++;
            return true;
        }

        return false;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            NativeMethods.UnregisterHotKey(Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (_actions.TryGetValue(id, out var action))
                Pressed?.Invoke(action);
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }
}

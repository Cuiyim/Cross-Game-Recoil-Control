using Forms = System.Windows.Forms;

namespace LegendaryCSharp.Services;

public static class KeyNameMapper
{
    public static ushort ToVirtualKey(string? keyName)
    {
        var normalized = Normalize(keyName);
        if (normalized.StartsWith("NUMPAD", StringComparison.Ordinal) 
            && normalized.Length == 7 
            && normalized[6] is >= '0' and <= '9')
        {
            return (ushort)((int)Forms.Keys.NumPad0 + normalized[6] - '0');
        }

        if (normalized.Length == 1)
        {
            var ch = char.ToUpperInvariant(normalized[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                return ch;
            }

            if (ch is >= '0' and <= '9')
            {
                return ch;
            }
        }

        return normalized switch
        {
            "LBUTTON" => (ushort)Forms.Keys.LButton,
            "RBUTTON" => (ushort)Forms.Keys.RButton,
            "MBUTTON" => (ushort)Forms.Keys.MButton,
            "XBUTTON1" => (ushort)Forms.Keys.XButton1,
            "XBUTTON2" => (ushort)Forms.Keys.XButton2,
            "PAGEDOWN" or "PGDN" => (ushort)Forms.Keys.PageDown,
            "PAGEUP" or "PGUP" => (ushort)Forms.Keys.PageUp,
            "ESC" or "ESCAPE" => (ushort)Forms.Keys.Escape,
            "SPACE" => (ushort)Forms.Keys.Space,
            "ENTER" or "RETURN" => (ushort)Forms.Keys.Enter,
            "TAB" => (ushort)Forms.Keys.Tab,
            "CAPSLOCK" or "CAPS" => (ushort)Forms.Keys.CapsLock,
            "BACKSPACE" or "BACK" => (ushort)Forms.Keys.Back,
            "DELETE" or "DEL" => (ushort)Forms.Keys.Delete,
            "INSERT" or "INS" => (ushort)Forms.Keys.Insert,
            "HOME" => (ushort)Forms.Keys.Home,
            "END" => (ushort)Forms.Keys.End,
            "LEFT" => (ushort)Forms.Keys.Left,
            "RIGHT" => (ushort)Forms.Keys.Right,
            "UP" => (ushort)Forms.Keys.Up,
            "DOWN" => (ushort)Forms.Keys.Down,
            "SHIFT" => (ushort)Forms.Keys.ShiftKey,
            "CTRL" or "CONTROL" => (ushort)Forms.Keys.ControlKey,
            "ALT" => (ushort)Forms.Keys.Menu,
            "F1" => (ushort)Forms.Keys.F1,
            "F2" => (ushort)Forms.Keys.F2,
            "F3" => (ushort)Forms.Keys.F3,
            "F4" => (ushort)Forms.Keys.F4,
            "F5" => (ushort)Forms.Keys.F5,
            "F6" => (ushort)Forms.Keys.F6,
            "F7" => (ushort)Forms.Keys.F7,
            "F8" => (ushort)Forms.Keys.F8,
            "F9" => (ushort)Forms.Keys.F9,
            "F10" => (ushort)Forms.Keys.F10,
            "F11" => (ushort)Forms.Keys.F11,
            "F12" => (ushort)Forms.Keys.F12,
            _ => 0
        };
    }

    public static string Normalize(string? keyName) =>
        (keyName ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();
}

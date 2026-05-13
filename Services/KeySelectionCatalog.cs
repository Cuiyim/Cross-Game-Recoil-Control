namespace LegendaryCSharp.Services;

public enum KeySelectionMode
{
    MasterHotkey,
    KeyboardAndMouse
}

public sealed record KeyChoice(string Name, string Label);

public static class KeySelectionCatalog
{
    private static readonly KeyChoice[] MasterHotkeyChoices =
    [
        new("PageDown", "PageDown"),
        new("PageUp", "PageUp"),
        new("Insert", "Insert"),
        new("Delete", "Delete"),
        new("Home", "Home"),
        new("End", "End"),
        new("F1", "F1"),
        new("F3", "F3"),
        new("F4", "F4"),
        new("F5", "F5"),
        new("F6", "F6"),
        new("F7", "F7"),
        new("F8", "F8"),
        new("F9", "F9"),
        new("F10", "F10"),
        new("F11", "F11"),
        new("F12", "F12")
    ];

    private static readonly KeyChoice[] KeyboardChoices =
    [
        new("Escape", "Esc"),
        new("F1", "F1"),
        new("F2", "F2"),
        new("F3", "F3"),
        new("F4", "F4"),
        new("F5", "F5"),
        new("F6", "F6"),
        new("F7", "F7"),
        new("F8", "F8"),
        new("F9", "F9"),
        new("F10", "F10"),
        new("F11", "F11"),
        new("F12", "F12"),
        new("1", "1"),
        new("2", "2"),
        new("3", "3"),
        new("4", "4"),
        new("5", "5"),
        new("6", "6"),
        new("7", "7"),
        new("8", "8"),
        new("9", "9"),
        new("0", "0"),
        new("Q", "Q"),
        new("W", "W"),
        new("E", "E"),
        new("R", "R"),
        new("T", "T"),
        new("Y", "Y"),
        new("U", "U"),
        new("I", "I"),
        new("O", "O"),
        new("P", "P"),
        new("A", "A"),
        new("S", "S"),
        new("D", "D"),
        new("F", "F"),
        new("G", "G"),
        new("H", "H"),
        new("J", "J"),
        new("K", "K"),
        new("L", "L"),
        new("Z", "Z"),
        new("X", "X"),
        new("C", "C"),
        new("V", "V"),
        new("B", "B"),
        new("N", "N"),
        new("M", "M"),
        new("Tab", "Tab"),
        new("CapsLock", "Caps"),
        new("Shift", "Shift"),
        new("Ctrl", "Ctrl"),
        new("Alt", "Alt"),
        new("Space", "Space"),
        new("Enter", "Enter"),
        new("Backspace", "Backspace"),
        new("Insert", "Ins"),
        new("Delete", "Del"),
        new("Home", "Home"),
        new("End", "End"),
        new("PageUp", "PgUp"),
        new("PageDown", "PgDn"),
        new("Up", "Up"),
        new("Down", "Down"),
        new("Left", "Left"),
        new("Right", "Right"),
        new("NumPad0", "Num0"),
        new("NumPad1", "Num1"),
        new("NumPad2", "Num2"),
        new("NumPad3", "Num3"),
        new("NumPad4", "Num4"),
        new("NumPad5", "Num5"),
        new("NumPad6", "Num6"),
        new("NumPad7", "Num7"),
        new("NumPad8", "Num8"),
        new("NumPad9", "Num9")
    ];

    private static readonly KeyChoice[] MouseChoices =
    [
        new("LButton", "Left"),
        new("RButton", "Right"),
        new("MButton", "Middle"),
        new("XButton1", "Side 1"),
        new("XButton2", "Side 2")
    ];

    public static IReadOnlyList<KeyChoice> GetChoices(KeySelectionMode mode) =>
        mode == KeySelectionMode.MasterHotkey
            ? MasterHotkeyChoices
            : [.. KeyboardChoices, .. MouseChoices];

    public static bool TryNormalize(string? keyName, KeySelectionMode mode, out string normalizedName)
    {
        var normalized = KeyNameMapper.Normalize(keyName);
        foreach (var choice in GetChoices(mode))
        {
            if (KeyNameMapper.Normalize(choice.Name) == normalized)
            {
                normalizedName = choice.Name;
                return true;
            }
        }

        normalizedName = string.Empty;
        return false;
    }
}

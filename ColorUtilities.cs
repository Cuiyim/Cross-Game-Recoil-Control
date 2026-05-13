using System.Globalization;

namespace LegendaryCSharp;

public static class ColorUtilities
{
    public static bool TryParseHexColor(string? text, out int rgb)
    {
        rgb = 0;
        var raw = (text ?? string.Empty).Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[2..];
        }

        raw = new string(raw.Where(Uri.IsHexDigit).ToArray());
        if (raw.Length == 0 || raw.Length > 6)
        {
            return false;
        }

        if (!int.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
        {
            return false;
        }

        rgb &= 0xFFFFFF;
        return true;
    }

    public static string ToHex(int rgb) => $"0x{rgb & 0xFFFFFF:X6}";

    public static bool WithinTolerance(int leftRgb, int rightRgb, int tolerance)
    {
        var lr = (leftRgb >> 16) & 0xFF;
        var lg = (leftRgb >> 8) & 0xFF;
        var lb = leftRgb & 0xFF;
        var rr = (rightRgb >> 16) & 0xFF;
        var rg = (rightRgb >> 8) & 0xFF;
        var rb = rightRgb & 0xFF;

        return Math.Abs(lr - rr) <= tolerance
            && Math.Abs(lg - rg) <= tolerance
            && Math.Abs(lb - rb) <= tolerance;
    }

    public static int FromBgra(byte blue, byte green, byte red) =>
        (red << 16) | (green << 8) | blue;
}

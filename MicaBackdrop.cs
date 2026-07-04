using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LegendaryCSharp;

/// <summary>
/// Applies a Windows 11 system backdrop (Mica / Acrylic) to a WPF window via DWM — the closest
/// Windows equivalent of macOS "vibrancy". Returns false (caller keeps a solid background) on OS
/// builds that don't support it, so it degrades gracefully.
/// </summary>
public static class MicaBackdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public enum BackdropType
    {
        Auto = 0,
        None = 1,
        Mica = 2,
        Acrylic = 3,
        Tabbed = 4,
    }

    public static bool TryApply(Window window, BackdropType type = BackdropType.Mica, bool dark = false)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        // The WPF surface must be transparent for the DWM backdrop to show through.
        window.Background = System.Windows.Media.Brushes.Transparent;
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }

        // Extend the (now transparent) frame across the whole client area so DWM paints the backdrop behind it.
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        var darkValue = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));

        var backdrop = (int)type;
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}

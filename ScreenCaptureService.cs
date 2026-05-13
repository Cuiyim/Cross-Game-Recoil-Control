using System.Drawing;
using System.Drawing.Imaging;
using Forms = System.Windows.Forms;

namespace LegendaryCSharp;

public sealed class ScreenCaptureService : IDisposable
{
    private readonly object _searchCaptureLock = new();
    private Bitmap? _searchBitmap;
    private Graphics? _searchGraphics;
    private Rectangle _cachedVirtualScreenBounds;
    private DateTime _screenBoundsCachedUtc = DateTime.MinValue;
    private bool _disposed;

    public string LastCaptureBackend { get; private set; } = "GDI";

    public int GetPixelColor(int x, int y)
    {
        var screen = GetCachedVirtualScreenBounds();
        var clampedX = Math.Clamp(x, screen.Left, screen.Right - 1);
        var clampedY = Math.Clamp(y, screen.Top, screen.Bottom - 1);
        using var capture = CaptureGdi(Rectangle.FromLTRB(clampedX, clampedY, clampedX + 1, clampedY + 1));
        var color = capture.Image.GetPixel(0, 0);
        LastCaptureBackend = "GDI";
        return (color.R << 16) | (color.G << 8) | color.B;
    }

    public PixelSearchResult? FindColor(Rectangle region, int targetRgb, int tolerance)
    {
        var bounds = NormalizeScreenRegion(region);
        if (bounds.IsEmpty)
        {
            return null;
        }

        lock (_searchCaptureLock)
        {
            ThrowIfDisposed();
            EnsureSearchBitmap(bounds.Width, bounds.Height);
            _searchGraphics!.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                new System.Drawing.Size(bounds.Width, bounds.Height),
                CopyPixelOperation.SourceCopy);

            var bitmapBounds = new Rectangle(0, 0, _searchBitmap!.Width, _searchBitmap.Height);
            var data = _searchBitmap.LockBits(bitmapBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var targetR = (targetRgb >> 16) & 0xFF;
                var targetG = (targetRgb >> 8) & 0xFF;
                var targetB = targetRgb & 0xFF;
                var minR = Math.Max(0, targetR - tolerance);
                var maxR = Math.Min(255, targetR + tolerance);
                var minG = Math.Max(0, targetG - tolerance);
                var maxG = Math.Min(255, targetG + tolerance);
                var minB = Math.Max(0, targetB - tolerance);
                var maxB = Math.Min(255, targetB + tolerance);

                unsafe
                {
                    var basePtr = (byte*)data.Scan0;
                    for (var y = 0; y < bitmapBounds.Height; y++)
                    {
                        var row = basePtr + y * data.Stride;
                        for (var x = 0; x < bitmapBounds.Width; x++)
                        {
                            var pixel = row + x * 4;
                            var blue = pixel[0];
                            var green = pixel[1];
                            var red = pixel[2];
                            if (red >= minR
                                && red <= maxR
                                && green >= minG
                                && green <= maxG
                                && blue >= minB
                                && blue <= maxB)
                            {
                                LastCaptureBackend = "GDI";
                                return new PixelSearchResult(bounds.Left + x, bounds.Top + y, ColorUtilities.FromBgra(blue, green, red));
                            }
                        }
                    }
                }
            }

            finally
            {
                _searchBitmap.UnlockBits(data);
            }
        }

        LastCaptureBackend = "GDI";
        return null;
    }

    public ScreenBitmap? CaptureRegion(Rectangle region)
    {
        var bounds = NormalizeScreenRegion(region);
        if (bounds.IsEmpty)
        {
            return null;
        }

        LastCaptureBackend = "GDI";
        return CaptureGdi(bounds);
    }

    private static Rectangle GetVirtualScreenBounds()
    {
        var screens = Forms.Screen.AllScreens;
        if (screens.Length == 0)
        {
            return new Rectangle(0, 0, 1, 1);
        }

        var bounds = screens[0].Bounds;
        for (var i = 1; i < screens.Length; i++)
        {
            bounds = Rectangle.Union(bounds, screens[i].Bounds);
        }

        return bounds;
    }

    private Rectangle NormalizeScreenRegion(Rectangle region)
    {
        var screen = GetCachedVirtualScreenBounds();
        var left = Math.Clamp(Math.Min(region.Left, region.Right), screen.Left, screen.Right - 1);
        var top = Math.Clamp(Math.Min(region.Top, region.Bottom), screen.Top, screen.Bottom - 1);
        var right = Math.Clamp(Math.Max(region.Left, region.Right), screen.Left, screen.Right - 1);
        var bottom = Math.Clamp(Math.Max(region.Top, region.Bottom), screen.Top, screen.Bottom - 1);
        if (right < left || bottom < top)
        {
            return Rectangle.Empty;
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private Rectangle GetCachedVirtualScreenBounds()
    {
        var now = DateTime.UtcNow;
        if (!_cachedVirtualScreenBounds.IsEmpty && now - _screenBoundsCachedUtc < TimeSpan.FromSeconds(1))
        {
            return _cachedVirtualScreenBounds;
        }

        _cachedVirtualScreenBounds = GetVirtualScreenBounds();
        _screenBoundsCachedUtc = now;
        return _cachedVirtualScreenBounds;
    }

    private static ScreenBitmap CaptureGdi(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new System.Drawing.Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
        return new ScreenBitmap(bitmap, bounds.Left, bounds.Top);
    }

    private void EnsureSearchBitmap(int width, int height)
    {
        if (_searchBitmap is not null && _searchBitmap.Width == width && _searchBitmap.Height == height)
        {
            return;
        }

        _searchGraphics?.Dispose();
        _searchBitmap?.Dispose();
        _searchBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _searchGraphics = Graphics.FromImage(_searchBitmap);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_searchCaptureLock)
        {
            if (_disposed)
            {
                return;
            }

            _searchGraphics?.Dispose();
            _searchBitmap?.Dispose();
            _disposed = true;
        }
    }

    public sealed class ScreenBitmap : IDisposable
    {
        public ScreenBitmap(Bitmap image, int left, int top)
        {
            Image = image;
            Left = left;
            Top = top;
        }

        public Bitmap Image { get; }
        public int Left { get; }
        public int Top { get; }

        public void Dispose() => Image.Dispose();
    }
}

public sealed record PixelSearchResult(int X, int Y, int Rgb);

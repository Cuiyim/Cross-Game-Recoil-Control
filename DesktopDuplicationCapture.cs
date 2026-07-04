using System.Diagnostics;
using System.Drawing;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LegendaryCSharp;

/// <summary>
/// GPU-accelerated screen capture via the DXGI Desktop Duplication API.
/// Captures only the requested sub-region into a CPU-readable staging texture and
/// runs the colour search directly on the mapped buffer, avoiding the GDI BitBlt
/// (GPU-&gt;CPU full readback) that dominates <see cref="ScreenCaptureService"/>.
///
/// Works against DirectX games (borderless, fullscreen-optimised, and exclusive
/// fullscreen) because Desktop Duplication captures the composited desktop output.
/// On any transition that invalidates the duplication (resolution / fullscreen
/// toggle / secure desktop) it releases and rebuilds on the next call; the caller
/// transparently falls back to GDI for that single round.
/// </summary>
internal sealed class DesktopDuplicationCapture : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
    {
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0,
    };

    private readonly object _lock = new();
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private Rectangle _activeOutputBounds = Rectangle.Empty;
    private int _activeAdapterIndex = -1;
    private int _stagingWidth;
    private int _stagingHeight;
    private bool _hasFrame;
    private bool _disposed;

    /// <summary>Milliseconds spent in the last AcquireNextFrame + sub-region copy (≈ frame-present cadence).</summary>
    public double LastAcquireMs { get; private set; }

    /// <summary>Milliseconds spent mapping + scanning the staging copy in the last call.</summary>
    public double LastScanMs { get; private set; }

    /// <summary>
    /// Attempts to locate <paramref name="targetRgb"/> within <paramref name="region"/>
    /// (already normalised to virtual-desktop pixel coordinates) using Desktop Duplication.
    /// Returns <c>false</c> when DXGI cannot service the request this round (region spans
    /// multiple outputs, transient ACCESS_LOST, no frame yet) so the caller can fall back
    /// to GDI. A <c>true</c> return with a <c>null</c> <paramref name="result"/> means the
    /// scan ran successfully but found no match.
    /// </summary>
    public bool TryFindColor(Rectangle region, int targetRgb, int tolerance, out PixelSearchResult? result)
        => TryFindColor(region, targetRgb, tolerance, 0, out result);

    /// <summary>
    /// As the 4-argument overload, but in steady state blocks up to
    /// <paramref name="steadyStateTimeoutMs"/> inside <c>AcquireNextFrame</c> until the desktop
    /// presents a new frame. This makes the scan frame-driven — it returns the instant the frame
    /// containing the target is composited, instead of waiting for an external poll tick. On a
    /// static screen it returns after the timeout and re-scans the retained staging copy.
    /// </summary>
    public bool TryFindColor(Rectangle region, int targetRgb, int tolerance, int steadyStateTimeoutMs, out PixelSearchResult? result)
    {
        result = null;
        lock (_lock)
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                if (_duplication is null || !_activeOutputBounds.Contains(region))
                {
                    if (!ResolveAndCreate(region))
                    {
                        return false;
                    }
                }

                if (!EnsureStaging(region.Width, region.Height))
                {
                    return false;
                }

                var localRegion = new Rectangle(
                    region.Left - _activeOutputBounds.Left,
                    region.Top - _activeOutputBounds.Top,
                    region.Width,
                    region.Height);

                var acquireStart = Stopwatch.GetTimestamp();
                if (!Acquire(localRegion, steadyStateTimeoutMs))
                {
                    return false;
                }

                var scanStart = Stopwatch.GetTimestamp();
                result = Scan(region, targetRgb, tolerance);
                LastAcquireMs = Stopwatch.GetElapsedTime(acquireStart, scanStart).TotalMilliseconds;
                LastScanMs = Stopwatch.GetElapsedTime(scanStart).TotalMilliseconds;
                return true;
            }
            catch (SharpGenException)
            {
                ReleaseDuplication();
                return false;
            }
        }
    }

    private bool ResolveAndCreate(Rectangle region)
    {
        ReleaseDuplication();

        IDXGIFactory1? factory;
        try
        {
            factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        }
        catch (SharpGenException)
        {
            return false;
        }

        try
        {
            for (var a = 0; factory.EnumAdapters1((uint)a, out var adapter).Success; a++)
            {
                try
                {
                    for (var o = 0; adapter.EnumOutputs((uint)o, out var output).Success; o++)
                    {
                        try
                        {
                            var coords = output.Description.DesktopCoordinates;
                            var outputBounds = Rectangle.FromLTRB(coords.Left, coords.Top, coords.Right, coords.Bottom);
                            if (!outputBounds.Contains(region))
                            {
                                continue;
                            }

                            if (!EnsureDevice(adapter, a))
                            {
                                return false;
                            }

                            using var output1 = output.QueryInterface<IDXGIOutput1>();
                            _duplication = output1.DuplicateOutput(_device!);
                            _activeOutputBounds = outputBounds;
                            _activeAdapterIndex = a;
                            _hasFrame = false;
                            return true;
                        }
                        catch (SharpGenException)
                        {
                            // DuplicateOutput can transiently fail (secure desktop, races);
                            // give up this round and let the caller use GDI.
                            return false;
                        }
                        finally
                        {
                            output.Dispose();
                        }
                    }
                }
                finally
                {
                    adapter.Dispose();
                }
            }
        }
        finally
        {
            factory.Dispose();
        }

        // No single output fully contains the region (e.g. it spans two monitors).
        return false;
    }

    private bool EnsureDevice(IDXGIAdapter1 adapter, int adapterIndex)
    {
        if (_device is not null && _activeAdapterIndex == adapterIndex)
        {
            return true;
        }

        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;

        var hr = D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            FeatureLevels,
            out ID3D11Device? device,
            out ID3D11DeviceContext? context);

        if (hr.Failure || device is null || context is null)
        {
            device?.Dispose();
            context?.Dispose();
            return false;
        }

        _device = device;
        _context = context;
        return true;
    }

    private bool EnsureStaging(int width, int height)
    {
        if (_staging is not null && _stagingWidth == width && _stagingHeight == height)
        {
            return true;
        }

        _staging?.Dispose();
        _staging = null;
        _hasFrame = false;

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };

        try
        {
            _staging = _device!.CreateTexture2D(description);
        }
        catch (SharpGenException)
        {
            return false;
        }

        _stagingWidth = width;
        _stagingHeight = height;
        return true;
    }

    private bool Acquire(Rectangle localRegion, int steadyStateTimeoutMs)
    {
        // First frame: block up to 250ms to prime. Steady state: block up to the caller's
        // timeout so we wake the instant a new frame is presented (frame-driven). If nothing
        // is presented within the timeout the desktop is unchanged, so the retained staging
        // copy is still correct and we reuse it.
        var timeout = _hasFrame ? (uint)Math.Max(0, steadyStateTimeoutMs) : 250u;
        var hr = _duplication!.AcquireNextFrame(timeout, out _, out var resource);

        if (hr == Vortice.DXGI.ResultCode.WaitTimeout)
        {
            return _hasFrame;
        }

        if (hr.Failure)
        {
            resource?.Dispose();
            if (hr == Vortice.DXGI.ResultCode.AccessLost || hr == Vortice.DXGI.ResultCode.AccessDenied)
            {
                ReleaseDuplication();
            }

            return _hasFrame;
        }

        try
        {
            using var frame = resource!.QueryInterface<ID3D11Texture2D>();
            var box = new Vortice.Mathematics.Box(localRegion.Left, localRegion.Top, 0, localRegion.Right, localRegion.Bottom, 1);
            _context!.CopySubresourceRegion(_staging!, 0, 0, 0, 0, frame, 0, box);
        }
        finally
        {
            resource!.Dispose();
            _duplication!.ReleaseFrame();
        }

        _hasFrame = true;
        return true;
    }

    private unsafe PixelSearchResult? Scan(Rectangle region, int targetRgb, int tolerance)
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

        var map = _context!.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var basePtr = (byte*)map.DataPointer;
            var stride = map.RowPitch;
            var width = region.Width;
            var height = region.Height;

            for (var y = 0; y < height; y++)
            {
                var row = basePtr + y * stride;
                for (var x = 0; x < width; x++)
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
                        return new PixelSearchResult(region.Left + x, region.Top + y, ColorUtilities.FromBgra(blue, green, red));
                    }
                }
            }
        }
        finally
        {
            _context!.Unmap(_staging!, 0);
        }

        return null;
    }

    private void ReleaseDuplication()
    {
        _staging?.Dispose();
        _staging = null;
        _stagingWidth = 0;
        _stagingHeight = 0;
        _duplication?.Dispose();
        _duplication = null;
        _hasFrame = false;
        _activeOutputBounds = Rectangle.Empty;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _staging?.Dispose();
            _duplication?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
        }
    }
}

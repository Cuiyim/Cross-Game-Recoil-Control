using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using LegendaryCSharp;

namespace LegendaryCSharp.Services;

public sealed class ImageRecognitionMonitor
{
    private readonly ScreenCaptureService _screenCapture;
    private readonly InputService _input;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _scanCancellation;
    private int _isSearching;
    private int _hitStreak;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private string? _lastStateMessage;
    private DateTime _lastMissDebugUtc = DateTime.MinValue;
    private DateTime _lastMatchUpdateUtc = DateTime.MinValue;
    private DateTime _lastTriggerUtc = DateTime.MinValue;
    private double _emaCycleMs;
    private double _emaAcquireMs;
    private double _emaScanMs;

    public ImageRecognitionMonitor(ScreenCaptureService screenCapture, InputService input)
    {
        _screenCapture = screenCapture;
        _input = input;
    }

    public event EventHandler<ImageRecognitionEventArgs>? MatchFound;
    public event EventHandler<ImageRecognitionDebugEventArgs>? DebugUpdated;
    public event EventHandler<string>? StatusChanged;

    public void ApplySettings(AppSettings settings)
    {
        RestartScanner(ImageScanPlan.From(settings));
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_syncRoot)
        {
            cancellation = _scanCancellation;
            _scanCancellation = null;
        }

        cancellation?.Cancel();
    }

    private void RestartScanner(ImageScanPlan plan)
    {
        Stop();
        ResetSearchState();

        if (!plan.CanScan)
        {
            PublishState(plan.StatusMessage);
            return;
        }

        var cancellation = new CancellationTokenSource();
        lock (_syncRoot)
        {
            _scanCancellation = cancellation;
        }

        // Dedicated long-running thread: the DXGI path runs a tight, mostly-synchronous loop
        // (it blocks inside the capture rather than awaiting a timer), so keep it off the
        // shared thread pool.
        var scanTask = Task.Factory.StartNew(
            () => ScanLoopAsync(plan, cancellation.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        _ = scanTask.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        PublishState(plan.StatusMessage);
    }

    private async Task ScanLoopAsync(ImageScanPlan plan, CancellationToken cancellationToken)
    {
        // Raise the system timer resolution to 1ms for the lifetime of the scan so the few
        // remaining timed waits (GDI-fallback yield, DXGI acquire timeout, thread wake-ups) are
        // accurate instead of being rounded up to the ~15.6ms Windows default tick.
        HighResolutionTimerScope.Begin();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ScanOnce(plan);

                // On the DXGI path ScanOnce already blocked inside AcquireNextFrame until the
                // desktop presented a new frame (bounded by IntervalMs), so there is no fixed
                // poll delay — we react the instant the target is composited. The GDI fallback
                // does not block, so yield a slice to hold the cadence and avoid a busy spin.
                if (_screenCapture.LastCaptureBackend != "DXGI")
                {
                    await Task.Delay(plan.IntervalMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            HighResolutionTimerScope.End();
        }
    }

    private void ScanOnce(ImageScanPlan plan)
    {
        if (Interlocked.Exchange(ref _isSearching, 1) == 1)
        {
            return;
        }

        try
        {
            var cycleStart = Stopwatch.GetTimestamp();
            var result = _screenCapture.FindColor(plan.Region, plan.TargetRgb, plan.ColorTolerance, plan.IntervalMs);
            RecordTiming(Stopwatch.GetElapsedTime(cycleStart).TotalMilliseconds);
            if (result is null)
            {
                _hitStreak = 0;
                _lastX = int.MinValue;
                _lastY = int.MinValue;
                PublishMissDebug(plan);
                return;
            }

            if (Math.Abs(result.X - _lastX) <= 1 && Math.Abs(result.Y - _lastY) <= 1)
            {
                _hitStreak++;
            }
            else
            {
                _hitStreak = 1;
            }

            _lastX = result.X;
            _lastY = result.Y;
            var now = DateTime.UtcNow;
            PublishMatch(plan, result);
            PublishDebug(
                plan,
                Localization.T("ImageDebug.Match"),
                Localization.Format("ImageDebug.MatchDetail", ColorUtilities.ToHex(result.Rgb), result.X, result.Y, _hitStreak, plan.HitStreakRequired) + TimingSuffix());

            if (_hitStreak < plan.HitStreakRequired)
            {
                return;
            }

            if (now - _lastTriggerUtc < TimeSpan.FromMilliseconds(plan.TriggerCooldownMs))
            {
                return;
            }

            _hitStreak = 0;
            _lastTriggerUtc = now;
            TriggerConfiguredKey(plan);
        }
        catch (Exception ex)
        {
            Stop();
            PublishDebug(Localization.T("ImageDebug.Stopped"), ex.Message);
            StatusChanged?.Invoke(this, Localization.Format("Image.StatusStopped", ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _isSearching, 0);
        }
    }

    private void TriggerConfiguredKey(ImageScanPlan plan)
    {
        var key = plan.TriggerKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        switch (plan.TriggerMode)
        {
            case ImageTriggerMode.Down:
                _input.KeyDown(key);
                PublishDebug(plan, Localization.T("ImageDebug.Trigger"), Localization.Format("ImageTrigger.Down", key));
                StatusChanged?.Invoke(this, Localization.Format("ImageTrigger.StatusDown", key));
                break;
            case ImageTriggerMode.Up:
                _input.KeyUp(key);
                PublishDebug(plan, Localization.T("ImageDebug.Trigger"), Localization.Format("ImageTrigger.Up", key));
                StatusChanged?.Invoke(this, Localization.Format("ImageTrigger.StatusUp", key));
                break;
            case ImageTriggerMode.Auto:
                if (_input.IsKeyDown(key))
                {
                    _input.KeyUp(key);
                    PublishDebug(plan, Localization.T("ImageDebug.Trigger"), Localization.Format("ImageTrigger.Up", key));
                    StatusChanged?.Invoke(this, Localization.Format("ImageTrigger.StatusUp", key));
                }
                else
                {
                    _input.TapKey(key);
                    PublishDebug(plan, Localization.T("ImageDebug.Trigger"), Localization.Format("ImageTrigger.Tap", key));
                    StatusChanged?.Invoke(this, Localization.Format("ImageTrigger.StatusTap", key));
                }

                break;
            case ImageTriggerMode.Tap:
            default:
                _input.TapKey(key);
                PublishDebug(plan, Localization.T("ImageDebug.Trigger"), Localization.Format("ImageTrigger.Tap", key));
                StatusChanged?.Invoke(this, Localization.Format("ImageTrigger.StatusTap", key));
                break;
        }
    }

    private void ResetSearchState()
    {
        _hitStreak = 0;
        _lastX = int.MinValue;
        _lastY = int.MinValue;
        _lastMissDebugUtc = DateTime.MinValue;
        _lastMatchUpdateUtc = DateTime.MinValue;
        _emaCycleMs = 0;
        _emaAcquireMs = 0;
        _emaScanMs = 0;
    }

    // Lightweight per-scan timing so the debug overlay can show where each cycle goes:
    // "等帧" (AcquireNextFrame block ≈ frame-present cadence / the refresh floor) vs "扫描"
    // (our map+scan overhead). Smoothed with an EMA to stay readable under high frame rates.
    private void RecordTiming(double cycleMs)
    {
        _emaCycleMs = Ema(_emaCycleMs, cycleMs);
        if (_screenCapture.LastCaptureBackend == "DXGI")
        {
            _emaAcquireMs = Ema(_emaAcquireMs, _screenCapture.LastAcquireMs);
            _emaScanMs = Ema(_emaScanMs, _screenCapture.LastScanMs);
        }
    }

    private static double Ema(double previous, double sample)
        => previous <= 0 ? sample : (previous * 0.9) + (sample * 0.1);

    private string TimingSuffix()
    {
        if (_emaCycleMs <= 0)
        {
            return string.Empty;
        }

        var fps = 1000.0 / _emaCycleMs;
        return $"  [周期 {_emaCycleMs:F1}ms · 等帧 {_emaAcquireMs:F1} · 扫描 {_emaScanMs:F2} · ~{fps:F0}fps · {_screenCapture.LastCaptureBackend}]";
    }

    private void PublishMatch(ImageScanPlan plan, PixelSearchResult result)
    {
        var now = DateTime.UtcNow;
        if (now - _lastMatchUpdateUtc < TimeSpan.FromMilliseconds(100))
        {
            return;
        }

        _lastMatchUpdateUtc = now;
        MatchFound?.Invoke(this, new ImageRecognitionEventArgs(result.X, result.Y, result.Rgb, _hitStreak));
    }

    private void PublishDebug(ImageScanPlan plan, string state, string detail)
    {
        if (plan.DebugEnabled)
        {
            PublishDebug(state, detail);
        }
    }

    private void PublishDebug(string state, string detail) =>
        DebugUpdated?.Invoke(this, new ImageRecognitionDebugEventArgs(state, detail));

    private void PublishMissDebug(ImageScanPlan plan)
    {
        if (!plan.DebugEnabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastMissDebugUtc < TimeSpan.FromMilliseconds(200))
        {
            return;
        }

        _lastMissDebugUtc = now;
        PublishDebug(
            Localization.T("ImageDebug.Scanning"),
            Localization.Format("ImageDebug.NoMatchDetail", plan.Region.Left, plan.Region.Top, plan.Region.Right, plan.Region.Bottom) + TimingSuffix());
    }

    private void PublishState(string message)
    {
        lock (_syncRoot)
        {
            if (_lastStateMessage == message)
            {
                return;
            }

            _lastStateMessage = message;
        }

        StatusChanged?.Invoke(this, message);
    }

    private sealed record ImageScanPlan(
        bool CanScan,
        string StatusMessage,
        Rectangle Region,
        int TargetRgb,
        int ColorTolerance,
        int IntervalMs,
        string TriggerKey,
        ImageTriggerMode TriggerMode,
        int HitStreakRequired,
        int TriggerCooldownMs,
        bool DebugEnabled)
    {
        public static ImageScanPlan From(AppSettings settings)
        {
            if (!settings.ImageRecognitionEnabled)
            {
                return Stopped(Localization.T("Image.PlanAutoOff"));
            }

            if (!settings.ImageRecognitionF2Enabled)
            {
                return Stopped(Localization.T("Image.PlanF2Off"));
            }

            if (!ColorUtilities.TryParseHexColor(settings.TargetColor, out var targetRgb))
            {
                return Stopped(Localization.T("Image.PlanInvalidColor"));
            }

            var intervalMs = Math.Clamp(settings.SearchIntervalMs, 20, 2000);
            var targetHex = ColorUtilities.ToHex(targetRgb);
            var region = Rectangle.FromLTRB(settings.SearchX1, settings.SearchY1, settings.SearchX2, settings.SearchY2);
            return new ImageScanPlan(
                true,
                Localization.Format("Image.PlanScanning", settings.SearchX1, settings.SearchY1, settings.SearchX2, settings.SearchY2, targetHex, intervalMs),
                region,
                targetRgb,
                Math.Clamp(settings.ColorTolerance, 0, 255),
                intervalMs,
                settings.TriggerKey,
                settings.ImageTriggerMode,
                Math.Max(1, settings.ImageHitStreakRequired),
                Math.Max(0, settings.ImageTriggerCooldownMs),
                settings.ImageDebug);
        }

        private static ImageScanPlan Stopped(string statusMessage) =>
            new(
                false,
                statusMessage,
                Rectangle.Empty,
                0,
                0,
                1000,
                string.Empty,
                ImageTriggerMode.Tap,
                1,
                0,
                false);
    }
}

public sealed record ImageRecognitionEventArgs(int X, int Y, int Rgb, int HitStreak);

public sealed record ImageRecognitionDebugEventArgs(string State, string Detail);

/// <summary>
/// Ref-counted wrapper around <c>timeBeginPeriod</c>/<c>timeEndPeriod</c> (winmm). Raising the
/// global timer resolution to 1ms makes short waits and thread wake-ups accurate; it is scoped
/// to the active scan so the system reverts to its default tick when idle.
/// </summary>
internal static class HighResolutionTimerScope
{
    private const uint PeriodMs = 1;
    private static int _refCount;

    public static void Begin()
    {
        if (Interlocked.Increment(ref _refCount) == 1)
        {
            TimeBeginPeriod(PeriodMs);
        }
    }

    public static void End()
    {
        if (Interlocked.Decrement(ref _refCount) == 0)
        {
            TimeEndPeriod(PeriodMs);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uPeriod);
}

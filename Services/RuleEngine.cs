using System.Drawing;
using LegendaryCSharp;

namespace LegendaryCSharp.Services;

/// <summary>
/// Evaluates user-defined <see cref="InputRule"/>s against live input (and, for 图像识别类, against the
/// screen) and runs their actions. This is the data-driven, user-composable generalisation of the fixed
/// features: trigger → action across three categories — 持续类 (sustained), 触发类 (one-shot per key edge)
/// and 图像识别类 (one-shot per colour match).
///
/// Self-injected input is filtered upstream by <see cref="GlobalInputHook"/>, so an action that presses a
/// key cannot feed back and re-trigger a rule. Key/mouse handlers run on the hook thread under
/// <see cref="_syncRoot"/>; 定速点击 and 图像识别 run on their own background loops.
/// </summary>
public sealed class RuleEngine
{
    private readonly InputService _input;
    private readonly ScreenCaptureService _screenCapture;
    private readonly object _syncRoot = new();

    private List<InputRule> _rules = new();
    private bool _enabled;

    // Keys we pressed via an action and must release on stop (长按 holds, 按下 with leave-held / hold timer).
    private readonly HashSet<string> _heldKeys = new(StringComparer.OrdinalIgnoreCase);

    // Physical (non-injected) keys currently down, to collapse keyboard auto-repeat into one edge.
    private readonly HashSet<uint> _physicalKeysDown = new();

    // Per-continuous-rule engage state (identity-keyed on the cloned rule instances in _rules).
    private readonly Dictionary<InputRule, ContinuousState> _continuous = new();

    // Background 图像识别 scan loops, one per enabled ImageMatch rule.
    private readonly List<CancellationTokenSource> _imageScans = new();

    public RuleEngine(InputService input, ScreenCaptureService screenCapture)
    {
        _input = input;
        _screenCapture = screenCapture;
    }

    public event EventHandler<string>? StatusChanged;

    public void ApplySettings(AppSettings settings)
    {
        lock (_syncRoot)
        {
            StopAllLocked();
            _rules = settings.InputRules.Select(r => r.Clone()).ToList();
            _enabled = settings.MasterEnabled && settings.InputRulesEnabled;
            if (_enabled)
            {
                StartImageScansLocked();
            }
        }
    }

    public void HandleKey(uint virtualKey, bool isDown)
    {
        lock (_syncRoot)
        {
            // Track edges first so auto-repeat (repeated WM_KEYDOWN) is collapsed to a single down edge.
            if (isDown)
            {
                if (!_physicalKeysDown.Add(virtualKey))
                {
                    return;
                }
            }
            else
            {
                _physicalKeysDown.Remove(virtualKey);
            }

            if (!_enabled || virtualKey == 0)
            {
                return;
            }

            foreach (var rule in _rules)
            {
                if (!rule.Enabled || rule.Category == RuleCategory.ImageMatch)
                {
                    continue;
                }

                if (KeyNameMapper.ToVirtualKey(rule.TriggerKey) != virtualKey)
                {
                    continue;
                }

                DispatchKeyOrButtonLocked(rule, isDown);
            }
        }
    }

    public void HandleMouseButton(GlobalMouseButton button, bool isDown)
    {
        var token = MouseButtonToken(button);
        if (token.Length == 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!_enabled)
            {
                return;
            }

            foreach (var rule in _rules)
            {
                if (!rule.Enabled || rule.Category == RuleCategory.ImageMatch)
                {
                    continue;
                }

                if (!string.Equals(KeyNameMapper.Normalize(rule.TriggerKey), token, StringComparison.Ordinal))
                {
                    continue;
                }

                DispatchKeyOrButtonLocked(rule, isDown);
            }
        }
    }

    /// <summary>Release any keys this engine is holding and stop all loops (call on shutdown).</summary>
    public void ReleaseAll()
    {
        lock (_syncRoot)
        {
            StopAllLocked();
        }
    }

    private void DispatchKeyOrButtonLocked(InputRule rule, bool isDown)
    {
        switch (rule.Category)
        {
            case RuleCategory.Continuous:
                OnContinuousTriggerLocked(rule, isDown);
                break;
            case RuleCategory.Trigger:
                if (isDown)
                {
                    FireActionsLocked(rule);
                }

                break;
        }
    }

    // --- 持续类 ---------------------------------------------------------------------------------

    private void OnContinuousTriggerLocked(InputRule rule, bool isDown)
    {
        if (rule.EngageMode == EngageMode.WhileHeld)
        {
            if (isDown)
            {
                EngageLocked(rule);
            }
            else
            {
                DisengageLocked(rule);
            }

            return;
        }

        // Toggle: flip on the down edge only.
        if (!isDown)
        {
            return;
        }

        if (GetState(rule).Engaged)
        {
            DisengageLocked(rule);
        }
        else
        {
            EngageLocked(rule);
        }
    }

    private void EngageLocked(InputRule rule)
    {
        var state = GetState(rule);
        if (state.Engaged || string.IsNullOrWhiteSpace(rule.TargetKey))
        {
            return;
        }

        state.Engaged = true;
        if (rule.ContinuousMode == ContinuousMode.Hold)
        {
            _input.KeyDown(rule.TargetKey);
            _heldKeys.Add(rule.TargetKey);
        }
        else
        {
            var cts = new CancellationTokenSource();
            state.AutoClickCts = cts;
            var target = rule.TargetKey;
            var intervalMs = Math.Clamp((int)Math.Round(60000.0 / Math.Max(1, rule.RatePerMinute)), 5, 60000);
            _ = Task.Factory.StartNew(
                () => AutoClickLoopAsync(target, intervalMs, cts.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }
    }

    private void DisengageLocked(InputRule rule)
    {
        if (!_continuous.TryGetValue(rule, out var state) || !state.Engaged)
        {
            return;
        }

        state.Engaged = false;
        if (rule.ContinuousMode == ContinuousMode.Hold)
        {
            _input.KeyUp(rule.TargetKey);
            _heldKeys.Remove(rule.TargetKey);
        }
        else
        {
            state.AutoClickCts?.Cancel();
            state.AutoClickCts?.Dispose();
            state.AutoClickCts = null;
        }
    }

    private async Task AutoClickLoopAsync(string target, int intervalMs, CancellationToken token)
    {
        HighResolutionTimerScope.Begin();
        try
        {
            while (!token.IsCancellationRequested)
            {
                _input.TapKey(target);
                await Task.Delay(intervalMs, token).ConfigureAwait(false);
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

    private ContinuousState GetState(InputRule rule)
    {
        if (!_continuous.TryGetValue(rule, out var state))
        {
            state = new ContinuousState();
            _continuous[rule] = state;
        }

        return state;
    }

    // --- 触发类 / 图像识别类 shared output ------------------------------------------------------

    private void FireActionsLocked(InputRule rule)
    {
        foreach (var action in rule.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.TargetKey))
            {
                continue;
            }

            if (action.DelayMs <= 0)
            {
                ApplyActionLocked(action);
            }
            else
            {
                // Staggered output: fire after the per-action delay, re-checking the engine is still on.
                var captured = action;
                var delay = action.DelayMs;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    lock (_syncRoot)
                    {
                        if (_enabled)
                        {
                            ApplyActionLocked(captured);
                        }
                    }
                });
            }
        }
    }

    private void ApplyActionLocked(RuleAction action)
    {
        var key = action.TargetKey;
        switch (action.Form)
        {
            case TriggerForm.Tap:
                _input.TapKey(key);
                break;
            case TriggerForm.Down:
                _input.KeyDown(key);
                _heldKeys.Add(key);
                break;
            case TriggerForm.Up:
                _input.KeyUp(key);
                _heldKeys.Remove(key);
                break;
        }
    }

    // --- 图像识别类 ------------------------------------------------------------------------------

    private void StartImageScansLocked()
    {
        foreach (var rule in _rules)
        {
            if (!rule.Enabled || rule.Category != RuleCategory.ImageMatch)
            {
                continue;
            }

            if (!ColorUtilities.TryParseHexColor(rule.TargetColor, out _) || rule.Actions.Count == 0)
            {
                continue;
            }

            var cts = new CancellationTokenSource();
            _imageScans.Add(cts);
            var snapshot = rule.Clone();
            _ = Task.Factory.StartNew(
                () => ImageScanLoopAsync(snapshot, cts.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }
    }

    private async Task ImageScanLoopAsync(InputRule rule, CancellationToken token)
    {
        ColorUtilities.TryParseHexColor(rule.TargetColor, out var targetRgb);
        var region = Rectangle.FromLTRB(rule.RegionX1, rule.RegionY1, rule.RegionX2, rule.RegionY2);
        var intervalMs = Math.Clamp(rule.ScanIntervalMs, 5, 2000);
        var tolerance = Math.Clamp(rule.ColorTolerance, 0, 255);
        var hitStreakRequired = Math.Max(1, rule.HitStreakRequired);
        var cooldown = TimeSpan.FromMilliseconds(Math.Max(0, rule.CooldownMs));

        var hitStreak = 0;
        var lastX = int.MinValue;
        var lastY = int.MinValue;
        var lastFireUtc = DateTime.MinValue;

        HighResolutionTimerScope.Begin();
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = _screenCapture.FindColor(region, targetRgb, tolerance, intervalMs);
                if (result is null)
                {
                    hitStreak = 0;
                    lastX = int.MinValue;
                    lastY = int.MinValue;
                }
                else
                {
                    if (Math.Abs(result.X - lastX) <= 1 && Math.Abs(result.Y - lastY) <= 1)
                    {
                        hitStreak++;
                    }
                    else
                    {
                        hitStreak = 1;
                    }

                    lastX = result.X;
                    lastY = result.Y;

                    var now = DateTime.UtcNow;
                    if (hitStreak >= hitStreakRequired && now - lastFireUtc >= cooldown)
                    {
                        hitStreak = 0;
                        lastFireUtc = now;
                        lock (_syncRoot)
                        {
                            if (_enabled)
                            {
                                FireActionsLocked(rule);
                            }
                        }
                    }
                }

                // The DXGI path already blocked inside FindColor until a new frame presented (bounded by
                // intervalMs); only the GDI fallback needs an explicit yield to hold cadence.
                if (_screenCapture.LastCaptureBackend != "DXGI")
                {
                    await Task.Delay(intervalMs, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, Localization.Format("Image.StatusStopped", ex.Message));
        }
        finally
        {
            HighResolutionTimerScope.End();
        }
    }

    // --- lifecycle -----------------------------------------------------------------------------

    private void StopAllLocked()
    {
        foreach (var cts in _imageScans)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _imageScans.Clear();

        foreach (var state in _continuous.Values)
        {
            state.AutoClickCts?.Cancel();
            state.AutoClickCts?.Dispose();
            state.AutoClickCts = null;
            state.Engaged = false;
        }

        _continuous.Clear();

        foreach (var key in _heldKeys)
        {
            _input.KeyUp(key);
        }

        _heldKeys.Clear();
    }

    private static string MouseButtonToken(GlobalMouseButton button) => button switch
    {
        GlobalMouseButton.Left => "LBUTTON",
        GlobalMouseButton.Right => "RBUTTON",
        GlobalMouseButton.Middle => "MBUTTON",
        GlobalMouseButton.XButton1 => "XBUTTON1",
        GlobalMouseButton.XButton2 => "XBUTTON2",
        _ => string.Empty,
    };

    private sealed class ContinuousState
    {
        public bool Engaged;
        public CancellationTokenSource? AutoClickCts;
    }
}

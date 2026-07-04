using System.Text.Json.Serialization;

namespace LegendaryCSharp.Services;

/// <summary>
/// The big category a rule belongs to. The editor shows a different parameter panel per category,
/// and <see cref="RuleEngine"/> interprets each kind differently.
/// </summary>
public enum RuleCategory
{
    /// <summary>持续类: the action sustains while the rule is engaged (长按 / 定速点击).</summary>
    Continuous = 0,

    /// <summary>触发类: one trigger-key edge fires a list of timed actions (一对多).</summary>
    Trigger = 1,

    /// <summary>图像识别类: like 触发类 but the trigger is a screen colour match instead of a key.</summary>
    ImageMatch = 2,
}

/// <summary>How a <see cref="RuleCategory.Continuous"/> rule sustains its action while engaged.</summary>
public enum ContinuousMode
{
    /// <summary>长按保持: hold the target key down for as long as the rule is engaged.</summary>
    Hold = 0,

    /// <summary>定速点击: tap the target key at <see cref="InputRule.RatePerMinute"/> while engaged.</summary>
    AutoClick = 1,
}

/// <summary>What counts as "engaged" for a <see cref="RuleCategory.Continuous"/> rule.</summary>
public enum EngageMode
{
    /// <summary>按住触发: engaged while the trigger key is physically held; releases when it goes up.</summary>
    WhileHeld = 0,

    /// <summary>切换锁定: tap the trigger to engage, tap again to disengage ("按键黏连").</summary>
    Toggle = 1,
}

/// <summary>What a single action does to its target key.</summary>
public enum TriggerForm
{
    /// <summary>点击: press and immediately release the target.</summary>
    Tap = 0,

    /// <summary>按下: press the target and leave it held (release later with a 抬起 action).</summary>
    Down = 1,

    /// <summary>抬起: release the target.</summary>
    Up = 2,
}

/// <summary>
/// One timed key action inside a 触发类/图像识别类 rule. <see cref="DelayMs"/> is measured from the
/// moment the rule fires, so several actions can run together (delay 0) or be staggered. A timed hold
/// is expressed as a 按下 action plus a later 抬起 action — that replaces the old single "按下时长".
/// </summary>
public sealed class RuleAction
{
    /// <summary>Key/button the action operates on.</summary>
    public string TargetKey { get; set; } = string.Empty;

    /// <summary>Delay (ms) after the rule fires before this action runs; 0 = immediately.</summary>
    public int DelayMs { get; set; }

    public TriggerForm Form { get; set; } = TriggerForm.Tap;

    // Back-compat: v3.1.5 step-1 actions serialised {Type,Key}; fold Key into TargetKey.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Key
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(TargetKey))
            {
                TargetKey = value;
            }
        }
    }

    public RuleAction Clone() => new() { TargetKey = TargetKey, DelayMs = DelayMs, Form = Form };
}

/// <summary>
/// One user-composable rule. This is the data-driven generalisation of the fixed features;
/// <see cref="RuleEngine"/> interprets it. Fields outside the active <see cref="Category"/> are
/// ignored (kept so the editor can round-trip without data loss).
/// </summary>
public sealed class InputRule
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public RuleCategory Category { get; set; } = RuleCategory.Continuous;

    /// <summary>Key/button that drives 持续类/触发类 rules, e.g. "W", "XButton2" (unused by 图像识别类).</summary>
    public string TriggerKey { get; set; } = string.Empty;

    /// <summary>Single target for 持续类 (持续类 holds / auto-clicks exactly one key).</summary>
    public string TargetKey { get; set; } = string.Empty;

    // --- 持续类 (Continuous) ---
    public ContinuousMode ContinuousMode { get; set; } = ContinuousMode.Hold;
    public EngageMode EngageMode { get; set; } = EngageMode.Toggle;

    /// <summary>Auto-click cadence in presses per minute (used when <see cref="ContinuousMode.AutoClick"/>).</summary>
    public int RatePerMinute { get; set; } = 600;

    /// <summary>触发类/图像识别类 output: the list of timed key actions to fire (一对多).</summary>
    public List<RuleAction> Actions { get; set; } = new();

    /// <summary>Legacy single-output form, kept only to migrate v3.1.6 rules into <see cref="Actions"/>.</summary>
    public TriggerForm TriggerForm { get; set; } = TriggerForm.Tap;

    // --- 图像识别类 (ImageMatch) condition ---
    public int RegionX1 { get; set; }
    public int RegionY1 { get; set; }
    public int RegionX2 { get; set; } = 200;
    public int RegionY2 { get; set; } = 200;
    public string TargetColor { get; set; } = "0x000000";
    public int ColorTolerance { get; set; } = 30;

    /// <summary>How many consecutive matches at the same spot are needed before firing (debounce).</summary>
    public int HitStreakRequired { get; set; } = 3;

    /// <summary>Minimum gap between fires (ms).</summary>
    public int CooldownMs { get; set; } = 300;

    /// <summary>Idle poll cadence (ms); also bounds the DXGI frame-wait.</summary>
    public int ScanIntervalMs { get; set; } = 50;

    // Back-compat: v3.1.5 step-1 sticky rules serialised a {Trigger:{Key}} object.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyKeyRef? Trigger { get; set; }

    /// <summary>Folds older rule shapes onto the current model; no-op for current rules.</summary>
    public void MigrateLegacyShapeIfNeeded()
    {
        if (Trigger is not null)
        {
            // Step-1 sticky (ToggleHold) → 持续类 + 长按 + 切换锁定.
            Category = RuleCategory.Continuous;
            ContinuousMode = ContinuousMode.Hold;
            EngageMode = EngageMode.Toggle;
            if (string.IsNullOrWhiteSpace(TriggerKey))
            {
                TriggerKey = Trigger.Key ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(TargetKey) && Actions.Count > 0)
            {
                TargetKey = Actions[0].TargetKey;
            }

            Actions = new();
            Trigger = null;
            return;
        }

        // v3.1.6 (single TargetKey + TriggerForm) → one immediate action.
        if ((Category == RuleCategory.Trigger || Category == RuleCategory.ImageMatch)
            && Actions.Count == 0
            && !string.IsNullOrWhiteSpace(TargetKey))
        {
            Actions.Add(new RuleAction { TargetKey = TargetKey, DelayMs = 0, Form = TriggerForm });
        }
    }

    public InputRule Clone() => new()
    {
        Name = Name,
        Enabled = Enabled,
        Category = Category,
        TriggerKey = TriggerKey,
        TargetKey = TargetKey,
        ContinuousMode = ContinuousMode,
        EngageMode = EngageMode,
        RatePerMinute = RatePerMinute,
        Actions = Actions.Select(a => a.Clone()).ToList(),
        TriggerForm = TriggerForm,
        RegionX1 = RegionX1,
        RegionY1 = RegionY1,
        RegionX2 = RegionX2,
        RegionY2 = RegionY2,
        TargetColor = TargetColor,
        ColorTolerance = ColorTolerance,
        HitStreakRequired = HitStreakRequired,
        CooldownMs = CooldownMs,
        ScanIntervalMs = ScanIntervalMs,
    };

    /// <summary>Minimal shape of the step-1 Trigger record, kept only for migration.</summary>
    public sealed class LegacyKeyRef
    {
        public string? Key { get; set; }
    }
}

namespace LegendaryCSharp.Services;

/// <summary>
/// 内置预设档案。首次启动时由 <see cref="ProfileStore.SeedBuiltInProfiles"/> 写入用户的 General 档案目录，
/// 让第一次上手的用户可以直接从档案列表里选一套现成配置，不用从零手搓规则。
///
/// 设计原则：这些预设只用「输入规则」（键位辅助），刻意不含任何图像识别坐标/颜色，也不开压枪、屏息等固定功能。
/// 原因是输入规则与屏幕分辨率、游戏画面完全无关，拷到任何人的机器上都能直接用；而图像识别依赖每个人各自的
/// 分辨率、UI 位置和取色，无法做成通用预设，只能由用户自行校准。每条规则都围绕降低手部负担：把「长时间按住」
/// 「快速连点」「多键连招」这些操作压缩成一键。
/// </summary>
internal static class BuiltInProfiles
{
    public sealed record GeneralPreset(string Name, AppSettings.MainSettingsDocument Document);

    public static IReadOnlyList<GeneralPreset> GeneralPresets { get; } = Build();

    /// <summary>
    /// 一份「干净」的主设置：总开关开、输入规则开，其余固定功能（压枪/屏息/半自动/3连切枪）一律关闭，
    /// 这样加载预设时只会启用其中的输入规则，不会给用户带来意料之外的行为。
    /// </summary>
    private static AppSettings.MainSettingsDocument BaseDoc(List<InputRule> rules) => new()
    {
        MasterEnabled = true,
        RecoilEnabled = false,
        BreathHoldEnabled = false,
        SemiAutoMode = false,
        Cut31Enabled = false,
        InputRulesEnabled = true,
        InputRules = rules,
    };

    /// <summary>长按保持 + 切换锁定：点一下触发键锁定按住目标键，再点一下松开。免去长时间按住。</summary>
    private static InputRule HoldToggle(string name, string triggerKey, string targetKey) => new()
    {
        Name = name,
        Enabled = true,
        Category = RuleCategory.Continuous,
        ContinuousMode = ContinuousMode.Hold,
        EngageMode = EngageMode.Toggle,
        TriggerKey = triggerKey,
        TargetKey = targetKey,
    };

    /// <summary>定速点击 + 按住触发：按住触发键时以固定频率连点目标键，松开即停。免去快速连点。</summary>
    private static InputRule AutoClickWhileHeld(string name, string triggerKey, string targetKey, int ratePerMinute) => new()
    {
        Name = name,
        Enabled = true,
        Category = RuleCategory.Continuous,
        ContinuousMode = ContinuousMode.AutoClick,
        EngageMode = EngageMode.WhileHeld,
        TriggerKey = triggerKey,
        TargetKey = targetKey,
        RatePerMinute = ratePerMinute,
    };

    /// <summary>一键连招：点一下触发键，按顺序、按各自延迟发出一串按键。免去手动连续快速按键。</summary>
    private static InputRule Combo(string name, string triggerKey, params (string Key, int DelayMs)[] steps)
    {
        var rule = new InputRule
        {
            Name = name,
            Enabled = true,
            Category = RuleCategory.Trigger,
            TriggerKey = triggerKey,
        };

        foreach (var (key, delay) in steps)
        {
            rule.Actions.Add(new RuleAction { TargetKey = key, DelayMs = delay, Form = TriggerForm.Tap });
        }

        return rule;
    }

    private static List<GeneralPreset> Build() =>
    [
        // —— ① 基础常用：不分题材，最通用的两条无障碍规则 ——
        new("预设·基础常用", BaseDoc(
        [
            HoldToggle("长按转切换·左键（点侧键2锁定/解锁）", "XButton2", "LButton"),
            AutoClickWhileHeld("定速连点·左键（按住侧键1，约10次/秒）", "XButton1", "LButton", 600),
        ])),

        // —— ② 射击：持续开火、瞄准保持、半自动连发 ——
        new("预设·射击", BaseDoc(
        [
            HoldToggle("开火锁定·长按转切换左键（点侧键2）", "XButton2", "LButton"),
            HoldToggle("瞄准保持·长按转切换右键（点 C）", "C", "RButton"),
            AutoClickWhileHeld("半自动定速连发·左键（按住侧键1，约8次/秒）", "XButton1", "LButton", 480),
        ])),

        // —— ③ MOBA：一键连招、补刀连点、走砍 ——
        new("预设·MOBA", BaseDoc(
        [
            Combo("一键连招 Q-W-E-R（点侧键2）", "XButton2",
                ("Q", 0), ("W", 120), ("E", 240), ("R", 360)),
            AutoClickWhileHeld("补刀定速连点·左键（按住侧键1，约6次/秒）", "XButton1", "LButton", 360),
            Combo("走砍 A+左键（点 C）", "C", ("A", 0), ("LButton", 30)),
        ])),

        // —— ④ 沙盒/生存：持续挖掘、自动前进、快速放置 ——
        new("预设·沙盒生存", BaseDoc(
        [
            HoldToggle("持续挖掘·长按转切换左键（点侧键2）", "XButton2", "LButton"),
            HoldToggle("自动前进·长按转切换 W（点侧键1）", "XButton1", "W"),
            AutoClickWhileHeld("快速放置·定速连点右键（按住 C，约5次/秒）", "C", "RButton", 300),
        ])),
    ];
}

using System.IO;
using System.Text.Json;
using LegendaryCSharp.Services;

namespace LegendaryCSharp;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string ExportFormat = "LegendaryCSharp.SettingsExport";
    private const string ExportAppVersion = "3.1.8";

    public string Language { get; set; } = Localization.Chinese;
    public string MasterHotkey { get; set; } = "PageDown";
    public bool MasterEnabled { get; set; } = true;
    public string TriggerSideKey { get; set; } = "XButton2";
    public int FireRate { get; set; } = 600;
    public int RecoilForce { get; set; } = 5;
    public int HorizontalRecoil { get; set; }
    public int HorizontalPattern { get; set; }
    public bool RecoilEnabled { get; set; } = true;
    public bool BreathHoldEnabled { get; set; }
    public string BreathHoldKey { get; set; } = "L";
    public bool SemiAutoMode { get; set; }
    public bool Cut31Enabled { get; set; } = true;
    public int Cut31IntervalMs { get; set; } = 60;
    public bool InputRulesEnabled { get; set; } = true;
    public List<InputRule> InputRules { get; set; } = new();
    public bool ImageRuleMigrated { get; set; }
    public bool ImageRecognitionEnabled { get; set; }
    public bool ImageRecognitionF2Enabled { get; set; } = true;
    public int SearchX1 { get; set; } = 0;
    public int SearchY1 { get; set; } = 0;
    public int SearchX2 { get; set; } = 200;
    public int SearchY2 { get; set; } = 200;
    public int ColorTolerance { get; set; } = 30;
    public int SearchIntervalMs { get; set; } = 50;
    public string TargetColor { get; set; } = "0x000000";
    public bool UseTargetColor { get; set; } = true;
    public int ColorPickOffsetX { get; set; } = 14;
    public int ColorPickOffsetY { get; set; } = 14;
    public string TriggerKey { get; set; } = "x";
    public int ImageHitStreakRequired { get; set; } = 3;
    public ImageTriggerMode ImageTriggerMode { get; set; }
    public int ImageTriggerCooldownMs { get; set; } = 300;
    public bool ImageDebug { get; set; }

    public static string SettingsPath => MainSettingsPath;

    public static string MainSettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "LegendaryCSharp.settings.json");

    public static string ImageSettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "LegendaryCSharp.image-recognition.json");

    public static string SettingsSummary =>
        Localization.T("Status.ConfigLoaded");

    public static AppSettings Load()
    {
        var settings = TryReadLegacySettings() ?? new AppSettings();

        if (TryReadJson<MainSettingsDocument>(MainSettingsPath) is { } mainSettings)
        {
            mainSettings.ApplyTo(settings);
        }

        if (TryReadJson<ImageRecognitionSettingsDocument>(ImageSettingsPath) is { } imageSettings)
        {
            imageSettings.ApplyTo(settings);
        }

        // Defensive: also migrate rules that arrived via the legacy single-file path.
        foreach (var rule in settings.InputRules)
        {
            rule.MigrateLegacyShapeIfNeeded();
        }

        MigrateImageConfigToRule(settings);

        return settings;
    }

    /// <summary>
    /// 图像识别"统一": one-time fold of the standalone image-recognition config into an
    /// <see cref="RuleCategory.ImageMatch"/> rule, then retire the standalone scanner so image
    /// recognition runs only through <see cref="RuleEngine"/>. Idempotent via <see cref="ImageRuleMigrated"/>.
    /// </summary>
    private static void MigrateImageConfigToRule(AppSettings settings)
    {
        if (settings.ImageRuleMigrated)
        {
            return;
        }

        settings.ImageRuleMigrated = true;

        var hasImageRule = settings.InputRules.Any(r => r.Category == RuleCategory.ImageMatch);
        if (settings.ImageRecognitionEnabled
            && !hasImageRule
            && ColorUtilities.TryParseHexColor(settings.TargetColor, out _)
            && !string.IsNullOrWhiteSpace(settings.TriggerKey))
        {
            var imageRule = new InputRule
            {
                Enabled = true,
                Category = RuleCategory.ImageMatch,
                Name = "图像(迁移)",
                RegionX1 = settings.SearchX1,
                RegionY1 = settings.SearchY1,
                RegionX2 = settings.SearchX2,
                RegionY2 = settings.SearchY2,
                TargetColor = settings.TargetColor,
                ColorTolerance = settings.ColorTolerance,
                HitStreakRequired = Math.Max(1, settings.ImageHitStreakRequired),
                CooldownMs = Math.Max(0, settings.ImageTriggerCooldownMs),
                ScanIntervalMs = settings.SearchIntervalMs,
            };
            imageRule.Actions.Add(new RuleAction
            {
                TargetKey = settings.TriggerKey,
                DelayMs = 0,
                Form = settings.ImageTriggerMode switch
                {
                    ImageTriggerMode.Down => TriggerForm.Down,
                    ImageTriggerMode.Up => TriggerForm.Up,
                    _ => TriggerForm.Tap,
                },
            });
            settings.InputRules.Add(imageRule);
            settings.InputRulesEnabled = true;
        }

        // Retire the standalone scanner regardless: image behaviour now lives in the rule engine.
        settings.ImageRecognitionEnabled = false;
    }

    private static AppSettings? TryReadLegacySettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(ImageRecognitionEnabled), out _))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AppSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        WriteJson(MainSettingsPath, MainSettingsDocument.From(this));
        WriteJson(ImageSettingsPath, ImageRecognitionSettingsDocument.From(this));
    }

    public void ExportToFile(string path)
    {
        WriteJson(path, SettingsExportDocument.From(this));
    }

    public static bool TryImportFromFile(string path, out AppSettings settings)
    {
        settings = new AppSettings();

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty(nameof(SettingsExportDocument.Format), out _)
                || root.TryGetProperty(nameof(SettingsExportDocument.Main), out _)
                || root.TryGetProperty(nameof(SettingsExportDocument.ImageRecognition), out _))
            {
                var export = JsonSerializer.Deserialize<SettingsExportDocument>(json);
                if (export is null)
                {
                    return false;
                }

                export.ApplyTo(settings);
                NormalizeImportedSettings(settings);
                return true;
            }

            if (root.TryGetProperty(nameof(ImageRecognitionEnabled), out _)
                && root.TryGetProperty(nameof(MasterHotkey), out _))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                NormalizeImportedSettings(settings);
                return true;
            }

            if (root.TryGetProperty(nameof(MainSettingsDocument.MasterHotkey), out _)
                || root.TryGetProperty(nameof(MainSettingsDocument.TriggerSideKey), out _))
            {
                var main = JsonSerializer.Deserialize<MainSettingsDocument>(json);
                if (main is null)
                {
                    return false;
                }

                main.ApplyTo(settings);
                NormalizeImportedSettings(settings);
                return true;
            }

            if (root.TryGetProperty(nameof(ImageRecognitionSettingsDocument.TargetColor), out _)
                || root.TryGetProperty(nameof(ImageRecognitionSettingsDocument.SearchX1), out _))
            {
                var image = JsonSerializer.Deserialize<ImageRecognitionSettingsDocument>(json);
                if (image is null)
                {
                    return false;
                }

                image.ApplyTo(settings);
                NormalizeImportedSettings(settings);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static T? TryReadJson<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static void NormalizeImportedSettings(AppSettings settings)
    {
        settings.Language = Localization.NormalizeLanguage(settings.Language);
        settings.UseTargetColor = true;
    }

    public sealed class SettingsExportDocument
    {
        public string Format { get; set; } = ExportFormat;
        public string AppVersion { get; set; } = ExportAppVersion;
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
        public MainSettingsDocument? Main { get; set; }
        public ImageRecognitionSettingsDocument? ImageRecognition { get; set; }

        public static SettingsExportDocument From(AppSettings settings) => new()
        {
            Main = MainSettingsDocument.From(settings),
            ImageRecognition = ImageRecognitionSettingsDocument.From(settings)
        };

        public void ApplyTo(AppSettings settings)
        {
            Main?.ApplyTo(settings);
            ImageRecognition?.ApplyTo(settings);
        }
    }

    public sealed class MainSettingsDocument
    {
        public string MasterHotkey { get; set; } = "PageDown";
        public string Language { get; set; } = Localization.Chinese;
        public bool MasterEnabled { get; set; } = true;
        public string TriggerSideKey { get; set; } = "XButton2";
        public int FireRate { get; set; } = 600;
        public int RecoilForce { get; set; } = 5;
        public int HorizontalRecoil { get; set; }
        public int HorizontalPattern { get; set; }
        public bool RecoilEnabled { get; set; } = true;
        public bool BreathHoldEnabled { get; set; }
        public string BreathHoldKey { get; set; } = "L";
        public bool SemiAutoMode { get; set; }
        public bool Cut31Enabled { get; set; } = true;
        public int Cut31IntervalMs { get; set; } = 60;
        public bool InputRulesEnabled { get; set; } = true;
        public List<InputRule> InputRules { get; set; } = new();
        public bool ImageRuleMigrated { get; set; }

        public static MainSettingsDocument From(AppSettings settings) => new()
        {
            MasterHotkey = settings.MasterHotkey,
            Language = Localization.NormalizeLanguage(settings.Language),
            MasterEnabled = settings.MasterEnabled,
            TriggerSideKey = settings.TriggerSideKey,
            FireRate = settings.FireRate,
            RecoilForce = settings.RecoilForce,
            HorizontalRecoil = settings.HorizontalRecoil,
            HorizontalPattern = settings.HorizontalPattern,
            RecoilEnabled = settings.RecoilEnabled,
            BreathHoldEnabled = settings.BreathHoldEnabled,
            BreathHoldKey = settings.BreathHoldKey,
            SemiAutoMode = settings.SemiAutoMode,
            Cut31Enabled = settings.Cut31Enabled,
            Cut31IntervalMs = settings.Cut31IntervalMs,
            InputRulesEnabled = settings.InputRulesEnabled,
            InputRules = settings.InputRules.Select(r => r.Clone()).ToList(),
            ImageRuleMigrated = settings.ImageRuleMigrated
        };

        public void ApplyTo(AppSettings settings, bool applyLanguage = true)
        {
            settings.MasterHotkey = MasterHotkey;
            if (applyLanguage)
            {
                settings.Language = Localization.NormalizeLanguage(Language);
            }

            settings.MasterEnabled = MasterEnabled;
            settings.TriggerSideKey = TriggerSideKey;
            settings.FireRate = FireRate;
            settings.RecoilForce = RecoilForce;
            settings.HorizontalRecoil = HorizontalRecoil;
            settings.HorizontalPattern = HorizontalPattern;
            settings.RecoilEnabled = RecoilEnabled;
            settings.BreathHoldEnabled = BreathHoldEnabled;
            settings.BreathHoldKey = BreathHoldKey;
            settings.SemiAutoMode = SemiAutoMode;
            settings.Cut31Enabled = Cut31Enabled;
            settings.Cut31IntervalMs = Cut31IntervalMs;
            settings.InputRulesEnabled = InputRulesEnabled;
            settings.ImageRuleMigrated = ImageRuleMigrated;
            settings.InputRules = InputRules.Select(r =>
            {
                // Fold any step-1 (Trigger/Actions) rules onto the current model before cloning,
                // so users upgrading from v3.1.5 keep their saved 黏连 rules.
                r.MigrateLegacyShapeIfNeeded();
                return r.Clone();
            }).ToList();
        }
    }

    public sealed class ImageRecognitionSettingsDocument
    {
        public bool ImageRecognitionEnabled { get; set; }
        public bool ImageRecognitionF2Enabled { get; set; } = true;
        public int SearchX1 { get; set; } = 0;
        public int SearchY1 { get; set; } = 0;
        public int SearchX2 { get; set; } = 200;
        public int SearchY2 { get; set; } = 200;
        public int ColorTolerance { get; set; } = 30;
        public int SearchIntervalMs { get; set; } = 50;
        public string TargetColor { get; set; } = "0x000000";
        public bool UseTargetColor { get; set; } = true;
        public int ColorPickOffsetX { get; set; } = 14;
        public int ColorPickOffsetY { get; set; } = 14;
        public string TriggerKey { get; set; } = "x";
        public int ImageHitStreakRequired { get; set; } = 3;
        public ImageTriggerMode ImageTriggerMode { get; set; }
        public int ImageTriggerCooldownMs { get; set; } = 300;
        public bool ImageDebug { get; set; }

        public static ImageRecognitionSettingsDocument From(AppSettings settings) => new()
        {
            ImageRecognitionEnabled = settings.ImageRecognitionEnabled,
            ImageRecognitionF2Enabled = settings.ImageRecognitionF2Enabled,
            SearchX1 = settings.SearchX1,
            SearchY1 = settings.SearchY1,
            SearchX2 = settings.SearchX2,
            SearchY2 = settings.SearchY2,
            ColorTolerance = settings.ColorTolerance,
            SearchIntervalMs = settings.SearchIntervalMs,
            TargetColor = settings.TargetColor,
            UseTargetColor = true,
            ColorPickOffsetX = settings.ColorPickOffsetX,
            ColorPickOffsetY = settings.ColorPickOffsetY,
            TriggerKey = settings.TriggerKey,
            ImageHitStreakRequired = settings.ImageHitStreakRequired,
            ImageTriggerMode = settings.ImageTriggerMode,
            ImageTriggerCooldownMs = settings.ImageTriggerCooldownMs,
            ImageDebug = settings.ImageDebug
        };

        public void ApplyTo(AppSettings settings)
        {
            settings.ImageRecognitionEnabled = ImageRecognitionEnabled;
            settings.ImageRecognitionF2Enabled = ImageRecognitionF2Enabled;
            settings.SearchX1 = SearchX1;
            settings.SearchY1 = SearchY1;
            settings.SearchX2 = SearchX2;
            settings.SearchY2 = SearchY2;
            settings.ColorTolerance = ColorTolerance;
            settings.SearchIntervalMs = SearchIntervalMs;
            settings.TargetColor = TargetColor;
            settings.UseTargetColor = true;
            settings.ColorPickOffsetX = ColorPickOffsetX;
            settings.ColorPickOffsetY = ColorPickOffsetY;
            settings.TriggerKey = TriggerKey;
            settings.ImageHitStreakRequired = ImageHitStreakRequired;
            settings.ImageTriggerMode = ImageTriggerMode;
            settings.ImageTriggerCooldownMs = ImageTriggerCooldownMs;
            settings.ImageDebug = ImageDebug;
        }
    }
}

public enum ImageTriggerMode
{
    Tap = 0,
    Down = 1,
    Up = 2,
    Auto = 3
}

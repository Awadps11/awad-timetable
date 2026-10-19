using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AwadTimetable.Core.Constants;

namespace AwadTimetable.UI.Services;

/// <summary>
/// خدمة الإعدادات - تدير إعدادات المستخدم وحفظها في ملف JSON
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly Dictionary<string, object> _settings = new();
    private readonly string _settingsFilePath;

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AwadTimetable");
        
        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "settings.json");
    }

    public T Get<T>(string key, T defaultValue = default!)
    {
        if (!_settings.TryGetValue(key, out var value))
            return defaultValue;

        switch (value)
        {
            case T typed:
                return typed;
            case JsonElement element:
                return FromJson(element, defaultValue);
            default:
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    return defaultValue;
                }
        }
    }

    /// <summary>
    /// القيم المحمّلة من ملف JSON تصل كـ JsonElement وليس كنوعها المكتوب
    /// </summary>
    private static T FromJson<T>(JsonElement element, T defaultValue)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return defaultValue;

        try
        {
            var converted = JsonSerializer.Deserialize<T>(element.GetRawText());
            return converted is null ? defaultValue : converted;
        }
        catch
        {
            try
            {
                return (T)Convert.ChangeType(element.ToString(), typeof(T), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        var oldValue = _settings.TryGetValue(key, out var old) ? old : null;
        _settings[key] = value!;
        
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs
        {
            Key = key,
            OldValue = oldValue,
            NewValue = value
        });
        
        _logger.LogDebug("تم تحديث الإعداد: {Key} = {Value}", key, value);
    }

    public async Task SaveAsync()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            
            var json = JsonSerializer.Serialize(_settings, options);
            await File.WriteAllTextAsync(_settingsFilePath, json);
            _logger.LogInformation("تم حفظ الإعدادات في: {Path}", _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل في حفظ الإعدادات");
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInformation("ملف الإعدادات غير موجود، سيتم استخدام القيم الافتراضية");
                ApplyDefaults();
                return;
            }

            var json = await File.ReadAllTextAsync(_settingsFilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            
            if (loaded != null)
            {
                foreach (var kvp in loaded)
                {
                    _settings[kvp.Key] = kvp.Value;
                }

                MigrateLegacyKeys(_settings);
            }
            
            _logger.LogInformation("تم تحميل الإعدادات من: {Path}", _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل في تحميل الإعدادات، سيتم استخدام القيم الافتراضية");
            ApplyDefaults();
        }
    }

    public void Reset()
    {
        _settings.Clear();
        ApplyDefaults();
        _logger.LogInformation("تم إعادة تعيين الإعدادات للقيم الافتراضية");
    }

    private void ApplyDefaults()
    {
        Set(SettingsKeys.Theme, "Light");
        Set(SettingsKeys.Language, AppConstants.ArabicCultureCode);
        Set(SettingsKeys.AutoSave, true);
        Set(SettingsKeys.AutoSaveIntervalMinutes, 5);
        Set(SettingsKeys.ShowWatermark, true);
        Set(SettingsKeys.WatermarkText, "جميع الحقوق محفوظة © " + DateTime.Now.Year + " - " + AppConstants.DeveloperName);
        Set(SettingsKeys.DefaultFontSize, 13);
        Set(SettingsKeys.DefaultFontFamily, AppConstants.DefaultArabicFont);
        Set(SettingsKeys.ShowGridLines, true);
        Set(SettingsKeys.EnableAnimations, true);
        Set(SettingsKeys.AutoBackup, true);
        Set(SettingsKeys.BackupIntervalHours, 24);
        Set(SettingsKeys.MaxBackupCount, 10);
        Set(SettingsKeys.AutoSaveEnabled, true);
        Set(SettingsKeys.BackupEnabled, true);
        Set(SettingsKeys.PrintWatermarkText, "جميع الحقوق محفوظة © " + DateTime.Now.Year + " - عوض لافي الزبيدي");
        Set(SettingsKeys.PrintCopyrightText, "جميع الحقوق محفوظة © " + DateTime.Now.Year + " - عوض لافي الزبيدي");
        Set(SettingsKeys.PrintFontSize, 7);
        Set(SettingsKeys.PrintFontFamily, AppConstants.DefaultArabicFont);
        Set(SettingsKeys.PrintMarginTop, 20);
        Set(SettingsKeys.PrintMarginBottom, 20);
        Set(SettingsKeys.PrintMarginLeft, 20);
        Set(SettingsKeys.PrintMarginRight, 20);
        Set(SettingsKeys.EnableAi, true);
        Set(SettingsKeys.AiModel, "phi3.5:mini");
        Set(SettingsKeys.AiTemperature, 0.1f);
        Set(SettingsKeys.OllamaBaseUrl, "http://localhost:11434");
        Set(SettingsKeys.GenerationComplexity, "Normal");
        Set(SettingsKeys.GenerationTimeLimitMinutes, 5);
        Set(SettingsKeys.GenerationSeed, 0);
        Set(SettingsKeys.GenerationVariants, 1);
        Set(SettingsKeys.DraftMode, false);
        Set(SettingsKeys.RelaxConstraints, false);
        Set(SettingsKeys.MaxConflictsAllowed, 0);
    }

    /// <summary>المفاتيح القديمة التي استخدمتها الواجهة قبل التوحيد على SettingsKeys</summary>
    private static readonly (string Legacy, string Standard)[] LegacyKeyMap =
    {
        ("FontFamily", SettingsKeys.DefaultFontFamily),
        ("FontSize", SettingsKeys.DefaultFontSize),
        ("AutoSaveInterval", SettingsKeys.AutoSaveIntervalMinutes),
        ("OllamaUrl", SettingsKeys.OllamaBaseUrl),
        ("OllamaModel", SettingsKeys.AiModel),
        ("EnableAI", SettingsKeys.EnableAi),
    };

    private static void MigrateLegacyKeys(Dictionary<string, object> settings)
    {
        foreach (var (legacy, standard) in LegacyKeyMap)
        {
            if (settings.TryGetValue(legacy, out var value))
            {
                settings[standard] = value;
                settings.Remove(legacy);
            }
        }
    }
}

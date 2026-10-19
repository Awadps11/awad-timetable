using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AwadTimetable.AI.Clients;
using AwadTimetable.UI.Services;
using AwadTimetable.Core.Constants;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// نموذج عرض الإعدادات
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly OllamaClient? _ollamaClient;

    [ObservableProperty]
    private string _theme = "Light"; // Light, Dark, HighContrast

    [ObservableProperty]
    private string _language = "ar"; // ar

    [ObservableProperty]
    private string _fontFamily = AppConstants.DefaultArabicFont;

    [ObservableProperty]
    private int _fontSize = 13;

    [ObservableProperty]
    private bool _autoSave = true;

    [ObservableProperty]
    private int _autoSaveInterval = 5; // دقائق

    [ObservableProperty]
    private string _ollamaUrl = "http://localhost:11434";

    [ObservableProperty]
    private string _ollamaModel = App.DefaultAiModel;

    [ObservableProperty]
    private bool _enableAI = true;

    // مزوّد النموذج: ollama (محلي) أو openai (أي خدمة متوافقة)
    [ObservableProperty]
    private string _aiProvider = "ollama";

    [ObservableProperty]
    private string _openAiBaseUrl = "http://localhost:1234/v1";

    [ObservableProperty]
    private string _openAiApiKey = string.Empty;

    // خيارات التوليد
    [ObservableProperty]
    private string _generationComplexity = "Normal"; // Normal | Large | Huge

    [ObservableProperty]
    private int _generationTimeLimitMinutes = 5;

    [ObservableProperty]
    private int _generationSeed = 0; // 0 = عشوائي

    [ObservableProperty]
    private int _generationVariants = 1;

    [ObservableProperty]
    private bool _draftMode = false;

    [ObservableProperty]
    private bool _relaxConstraints = false;

    [ObservableProperty]
    private int _maxConflictsAllowed = 0;

    public IReadOnlyList<string> ComplexityOptions { get; } = new[] { "Normal", "Large", "Huge" };

    /// <summary>النماذج المثبّتة حاليًا في Ollama</summary>
    public ObservableCollection<string> InstalledModels { get; } = new();

    [ObservableProperty]
    private string _downloadModelName = "qwen2.5:7b";

    [ObservableProperty]
    private string _modelStatus = "";

    [ObservableProperty]
    private double _modelProgress = 0; // 0..100، و -1 عند عدم توفر إجمالي

    [ObservableProperty]
    private bool _isModelBusy = false;

    /// <summary>مجلد نماذج التطبيق المحلي (لتخزين ملفات GGUF عند الرغبة)</summary>
    public string ModelsFolder { get; } =
        Path.Combine(AppContext.BaseDirectory, "Models");

    public IReadOnlyList<string> AvailableFonts { get; } =
        Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name)
            .ToList();

    public SettingsViewModel(ISettingsService settingsService, OllamaClient? ollamaClient = null)
    {
        _settingsService = settingsService;
        _ollamaClient = ollamaClient;
        LoadSettings();
        _ = RefreshInstalledModelsAsync();
    }

    /// <summary>تحديث قائمة النماذج المثبّتة من Ollama</summary>
    [RelayCommand]
    private async Task RefreshInstalledModelsAsync()
    {
        if (_ollamaClient == null) return;
        try
        {
            var models = await _ollamaClient.ListModelsAsync();
            InstalledModels.Clear();
            foreach (var m in models.OrderBy(m => m)) InstalledModels.Add(m);
            ModelStatus = models.Count > 0
                ? $"النماذج المثبّتة: {models.Count}"
                : "لا يوجد نموذج مثبّت — نزّل نموذجًا من الأسفل.";
        }
        catch
        {
            ModelStatus = "تعذّر الوصول إلى Ollama (تأكد أنه يعمل على http://localhost:11434).";
        }
    }

    /// <summary>تنزيل أو تحديث النموذج المحدَّد عبر Ollama مع شريط تقدّم</summary>
    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        if (_ollamaClient == null || IsModelBusy) return;
        if (string.IsNullOrWhiteSpace(DownloadModelName))
        {
            ModelStatus = "اكتب اسم النموذج المطلوب تنزيله.";
            return;
        }

        IsModelBusy = true;
        ModelProgress = 0;
        try
        {
            Directory.CreateDirectory(ModelsFolder);

            var progress = new Progress<OllamaPullProgress>(p =>
            {
                var pct = p.Percent;
                ModelProgress = pct >= 0 ? pct : 0;
                ModelStatus = pct >= 0
                    ? $"{p.Status} — {pct:0.0}%"
                    : p.Status;
            });

            await _ollamaClient.PullModelAsync(DownloadModelName, progress);
            ModelStatus = $"تم تنزيل/تحديث النموذج {DownloadModelName} بنجاح.";
            await RefreshInstalledModelsAsync();

            // اجعل النموذج المنزَّل هو المستخدم
            OllamaModel = DownloadModelName;
        }
        catch (Exception ex)
        {
            ModelStatus = $"فشل تنزيل النموذج: {ex.Message}";
        }
        finally
        {
            IsModelBusy = false;
        }
    }

    private void LoadSettings()
    {
        Theme = _settingsService.Get<string>(SettingsKeys.Theme, "Light") ?? "Light";
        Language = _settingsService.Get<string>(SettingsKeys.Language, AppConstants.ArabicCultureCode) ?? AppConstants.ArabicCultureCode;
        FontFamily = ResolveFont(_settingsService.Get<string>(SettingsKeys.DefaultFontFamily, AppConstants.DefaultArabicFont));
        FontSize = _settingsService.Get<int>(SettingsKeys.DefaultFontSize, 13);
        AutoSave = _settingsService.Get<bool>(SettingsKeys.AutoSave, true);
        AutoSaveInterval = _settingsService.Get<int>(SettingsKeys.AutoSaveIntervalMinutes, 5);
        OllamaUrl = _settingsService.Get<string>(SettingsKeys.OllamaBaseUrl, "http://localhost:11434") ?? "http://localhost:11434";
        OllamaModel = _settingsService.Get<string>(SettingsKeys.AiModel, App.DefaultAiModel) ?? App.DefaultAiModel;
        EnableAI = _settingsService.Get<bool>(SettingsKeys.EnableAi, true);
        AiProvider = _settingsService.Get<string>(LlmProviderSettings.ProviderKey, "ollama") ?? "ollama";
        OpenAiBaseUrl = _settingsService.Get<string>(LlmProviderSettings.OpenAiBaseUrlKey, "http://localhost:1234/v1") ?? "http://localhost:1234/v1";
        OpenAiApiKey = _settingsService.Get<string>(LlmProviderSettings.OpenAiApiKeyKey, string.Empty) ?? string.Empty;

        GenerationComplexity = _settingsService.Get<string>(SettingsKeys.GenerationComplexity, "Normal") ?? "Normal";
        GenerationTimeLimitMinutes = _settingsService.Get<int>(SettingsKeys.GenerationTimeLimitMinutes, 5);
        GenerationSeed = _settingsService.Get<int>(SettingsKeys.GenerationSeed, 0);
        GenerationVariants = _settingsService.Get<int>(SettingsKeys.GenerationVariants, 1);
        DraftMode = _settingsService.Get<bool>(SettingsKeys.DraftMode, false);
        RelaxConstraints = _settingsService.Get<bool>(SettingsKeys.RelaxConstraints, false);
        MaxConflictsAllowed = _settingsService.Get<int>(SettingsKeys.MaxConflictsAllowed, 0);
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        _settingsService.Set(SettingsKeys.Theme, Theme);
        _settingsService.Set(SettingsKeys.Language, Language);
        _settingsService.Set(SettingsKeys.DefaultFontFamily, FontFamily);
        _settingsService.Set(SettingsKeys.DefaultFontSize, FontSize);
        _settingsService.Set(SettingsKeys.AutoSave, AutoSave);
        _settingsService.Set(SettingsKeys.AutoSaveIntervalMinutes, AutoSaveInterval);
        _settingsService.Set(SettingsKeys.OllamaBaseUrl, OllamaUrl);
        _settingsService.Set(SettingsKeys.AiModel, OllamaModel);
        _settingsService.Set(SettingsKeys.EnableAi, EnableAI);
        _settingsService.Set(LlmProviderSettings.ProviderKey, AiProvider);
        _settingsService.Set(LlmProviderSettings.OpenAiBaseUrlKey, OpenAiBaseUrl);
        _settingsService.Set(LlmProviderSettings.OpenAiApiKeyKey, OpenAiApiKey);
        _settingsService.Set(SettingsKeys.GenerationComplexity, GenerationComplexity);
        _settingsService.Set(SettingsKeys.GenerationTimeLimitMinutes, GenerationTimeLimitMinutes);
        _settingsService.Set(SettingsKeys.GenerationSeed, GenerationSeed);
        _settingsService.Set(SettingsKeys.GenerationVariants, GenerationVariants);
        _settingsService.Set(SettingsKeys.DraftMode, DraftMode);
        _settingsService.Set(SettingsKeys.RelaxConstraints, RelaxConstraints);
        _settingsService.Set(SettingsKeys.MaxConflictsAllowed, MaxConflictsAllowed);
        await _settingsService.SaveAsync();
    }

    [RelayCommand]
    private async Task ResetToDefaults()
    {
        Theme = "Light";
        Language = "ar";
        FontFamily = AppConstants.DefaultArabicFont;
        FontSize = 13;
        AutoSave = true;
        AutoSaveInterval = 5;
        OllamaUrl = "http://localhost:11434";
        OllamaModel = App.DefaultAiModel;
        EnableAI = true;
        GenerationComplexity = "Normal";
        GenerationTimeLimitMinutes = 5;
        GenerationSeed = 0;
        GenerationVariants = 1;
        DraftMode = false;
        RelaxConstraints = false;
        MaxConflictsAllowed = 0;
        await SaveSettings();
    }

    /// <summary>الخط القديم Cairo غير مضمَّن ولا مثبَّت في النظام → يعود إلى الخط الافتراضي</summary>
    private static string ResolveFont(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved)) return AppConstants.DefaultArabicFont;
        if (saved.StartsWith("Cairo", StringComparison.OrdinalIgnoreCase)) return AppConstants.DefaultArabicFont;
        return saved;
    }
}

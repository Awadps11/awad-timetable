using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwadTimetable.AI.Clients;
using Microsoft.Extensions.Logging;

namespace AwadTimetable.UI.Services;

/// <summary>حالة المساعد الذكي: مفعّل؟ Ollama متاح؟ النموذج محمّل؟</summary>
public sealed record AiModelStatus(bool Enabled, bool OllamaReachable, bool ModelInstalled, string Model, string Message)
{
    public bool Ready => Enabled && OllamaReachable && ModelInstalled;
}

/// <summary>
/// إعداد نموذج المساعد: فحص التوفّر، وتنزيل النموذج عند أول تشغيل (اختياري).
/// المفاتيح: EnableAi، AiModel، Ai.AutoDownloadModel (افتراضي true)، Ai.SetupHandled.
/// </summary>
public sealed class AiModelSetupService
{
    public const string AutoDownloadKey = "Ai.AutoDownloadModel";
    public const string SetupHandledKey = "Ai.SetupHandled";

    private readonly ISettingsService _settings;
    private readonly OllamaClient? _ollama;
    private readonly ILogger<AiModelSetupService> _logger;

    public AiModelSetupService(ISettingsService settings, ILogger<AiModelSetupService> logger, OllamaClient? ollama = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ollama = ollama;
    }

    public bool Enabled => _settings.Get(AiSettings.EnableKey, true);
    public bool AutoDownload => _settings.Get(AutoDownloadKey, true);
    public bool SetupHandled => _settings.Get(SetupHandledKey, false);
    public string Model => _settings.Get(AiSettings.ModelKey, "qwen2.5:7b");

    public void MarkSetupHandled()
    {
        _settings.Set(SetupHandledKey, true);
        _ = _settings.SaveAsync();
    }

    public async Task<AiModelStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!Enabled)
            return new AiModelStatus(false, false, false, Model, "المساعد الذكي معطّل من الإعدادات.");

        if (_ollama is null)
            return new AiModelStatus(true, false, false, Model, "خدمة Ollama غير مهيّأة.");

        bool healthy;
        try { healthy = await _ollama.IsHealthyAsync(ct); }
        catch { healthy = false; }

        if (!healthy)
            return new AiModelStatus(true, false, false, Model,
                "تعذر الاتصال بـ Ollama. شغّله من ollama.com ثم أعد الفحص.");

        bool installed = false;
        try
        {
            var models = await _ollama.ListModelsAsync(ct);
            installed = models.Any(m => string.Equals(m, Model, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر قراءة قائمة نماذج Ollama");
        }

        return new AiModelStatus(true, true, installed, Model,
            installed ? $"المساعد جاهز — النموذج: {Model}" : $"النموذج «{Model}» غير محمّل بعد.");
    }

    public Task PullAsync(IProgress<OllamaPullProgress>? progress, CancellationToken ct = default)
    {
        if (_ollama is null) throw new InvalidOperationException("خدمة Ollama غير مهيّأة.");
        return _ollama.PullModelAsync(Model, progress, ct);
    }
}

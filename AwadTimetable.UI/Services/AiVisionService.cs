using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwadTimetable.Core.Services;

namespace AwadTimetable.UI.Services;

/// <summary>مفاتيح إعدادات الذكاء الاصطناعي المشتركة</summary>
public static class AiSettings
{
    public const string EnableKey = "EnableAi";
    public const string BaseUrlKey = "OllamaBaseUrl";
    public const string ModelKey = "AiModel";
    public const string DefaultBaseUrl = "http://localhost:11434";
}

public interface IAiVisionService
{
    bool IsEnabled { get; }

    /// <summary>يستخرج قائمة المواد من صورة (يدعم قراءة قائمة مواد أو جدول).</summary>
    Task<IReadOnlyList<SubjectImportRow>> ExtractSubjectsFromImageAsync(string imagePath, CancellationToken ct = default);

    /// <summary>يستخرج أسماء أشخاص (معلمين/طلاب) من صورة كقائمة مكوّنات.</summary>
    Task<IReadOnlyList<ComponentRow>> ExtractPeopleFromImageAsync(string imagePath, string kind, CancellationToken ct = default);
}

/// <summary>
/// خدمة رؤية عبر Ollama: ترسل الصورة إلى النموذج وتقرأ CSV المواد الناتج.
/// تفشل برسالة واضحة إذا كان الذكاء الاصطناعي معطّلاً أو الخدمة غير متاحة.
/// </summary>
public sealed class AiVisionService : IAiVisionService
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;

    public AiVisionService(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public bool IsEnabled => _settings.Get(AiSettings.EnableKey, true);

    public async Task<IReadOnlyList<SubjectImportRow>> ExtractSubjectsFromImageAsync(string imagePath, CancellationToken ct = default)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("الذكاء الاصطناعي معطّل. فعّله من الإعدادات أولاً.");

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("لم يُعثر على صورة", imagePath);

        var baseUrl = _settings.Get(AiSettings.BaseUrlKey, AiSettings.DefaultBaseUrl);
        var model = _settings.Get(AiSettings.ModelKey, "gemma4:26b");
        var imageB64 = Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, ct));

        var prompt =
            "هذه صورة تحتوي على قائمة مواد دراسية أو جدول حصص. " +
            "استخرج أسماء المواد فقط. " +
            "أخرج CSV فقط بلا أي شرح ولا رؤوس ولا علامات ``` : كل سطر بالشكل: اسم المادة,الاختصار,عدد الحصص الأسبوعي. " +
            "الاختصار وعدد الحصص اختياريان واتركهما فارغين إن لم يظهرا. لا تكرر مادة مرتين.";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            images = new[] { imageB64 },
            options = new { temperature = 0 }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{baseUrl}/api/generate", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var snippet = body.Length > 300 ? body[..300] : body;
            throw new InvalidOperationException($"تعذر الاتصال بخدمة الذكاء الاصطناعي (HTTP {(int)resp.StatusCode}): {snippet}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("response", out var responseEl))
            throw new InvalidOperationException("لا يوجد رد من النموذج");

        var text = (responseEl.GetString() ?? string.Empty).Trim();

        // إزالة أسوار الكود إن وُجدت
        if (text.StartsWith("```"))
        {
            var nl = text.IndexOf('\n');
            if (nl > 0) text = text[(nl + 1)..];
            var fence = text.IndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) text = text[..fence];
            text = text.Trim();
        }

        return SubjectListImporter.ParseCsv(text);
    }

    public async Task<IReadOnlyList<ComponentRow>> ExtractPeopleFromImageAsync(string imagePath, string kind, CancellationToken ct = default)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("الذكاء الاصطناعي معطّل. فعّله من الإعدادات أولاً.");

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("لم يُعثر على صورة", imagePath);

        var baseUrl = _settings.Get(AiSettings.BaseUrlKey, AiSettings.DefaultBaseUrl);
        var model = _settings.Get(AiSettings.ModelKey, "gemma4:26b");
        var imageB64 = Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, ct));

        var label = kind switch
        {
            "students" => "الطلاب",
            _ => "المعلمين",
        };

        var prompt =
            $"هذه صورة تحتوي على قائمة {label}. استخرج الأسماء فقط. " +
            "أخرج CSV فقط بلا أي شرح ولا رؤوس ولا علامات ``` : كل سطر: الاسم الكامل,الاختصار. " +
            "الاختصار اختياري. لا تكرر اسماً.";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            images = new[] { imageB64 },
            options = new { temperature = 0 }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{baseUrl}/api/generate", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var snippet = body.Length > 300 ? body[..300] : body;
            throw new InvalidOperationException($"تعذر الاتصال بخدمة الذكاء الاصطناعي (HTTP {(int)resp.StatusCode}): {snippet}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("response", out var responseEl))
            throw new InvalidOperationException("لا يوجد رد من النموذج");

        var text = (responseEl.GetString() ?? string.Empty).Trim();
        if (text.StartsWith("```"))
        {
            var nl = text.IndexOf('\n');
            if (nl > 0) text = text[(nl + 1)..];
            var fence = text.IndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) text = text[..fence];
            text = text.Trim();
        }

        return ComponentTransfer.FromCsv(text);
    }
}

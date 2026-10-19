using System;
using System.Net.Http;
using AwadTimetable.AI.Clients;
using AwadTimetable.AI.Interfaces;

namespace AwadTimetable.UI.Services;

/// <summary>
/// يبني عميل النموذج حسب الإعدادات: Ollama المحلي أو أي مزوّد متوافق مع OpenAI.
/// مفاتيح: Ai.Provider = ollama|openai، Ai.OpenAiBaseUrl، Ai.OpenAiApiKey، AiModel، OllamaBaseUrl.
/// </summary>
public static class LlmProviderSettings
{
    public const string ProviderKey = "Ai.Provider";
    public const string OpenAiBaseUrlKey = "Ai.OpenAiBaseUrl";
    public const string OpenAiApiKeyKey = "Ai.OpenAiApiKey";
}

public static class LlmClientFactory
{
    public static ILlmClient Create(ISettingsService settings, IHttpClientFactory? httpFactory = null)
    {
        var provider = (settings.Get(LlmProviderSettings.ProviderKey, "ollama") ?? "ollama").Trim().ToLowerInvariant();
        var model = settings.Get(AiSettings.ModelKey, "qwen2.5:7b");

        var http = httpFactory?.CreateClient() ?? new HttpClient();

        if (provider == "openai")
        {
            var baseUrl = settings.Get(LlmProviderSettings.OpenAiBaseUrlKey, "http://localhost:1234/v1");
            var key = settings.Get(LlmProviderSettings.OpenAiApiKeyKey, string.Empty);
            return new OpenAiCompatibleClient(http, baseUrl ?? "http://localhost:1234/v1", key, model ?? "qwen2.5:7b");
        }

        var ollamaUrl = settings.Get(AiSettings.BaseUrlKey, AiSettings.DefaultBaseUrl);
        return new OllamaClient(http, ollamaUrl ?? AiSettings.DefaultBaseUrl, model ?? "qwen2.5:7b");
    }
}

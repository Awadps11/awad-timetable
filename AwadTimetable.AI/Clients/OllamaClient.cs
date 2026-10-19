using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AwadTimetable.AI.Interfaces;

namespace AwadTimetable.AI.Clients;

/// <summary>
/// عميل Ollama المحلي
/// </summary>
public class OllamaClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private string _defaultModel;
    private readonly JsonSerializerOptions _jsonOptions;

    public string ModelName { get => _defaultModel; set => _defaultModel = value; }
    public bool IsLocal => true;
    public bool SupportsFunctionCalling => true;
    public bool SupportsStreaming => true;

    public OllamaClient(HttpClient httpClient, string baseUrl = "http://localhost:11434", string defaultModel = "phi3.5:mini")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = baseUrl.TrimEnd('/');
        _defaultModel = defaultModel;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _httpClient.BaseAddress = new Uri(_baseUrl + "/");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var ollamaRequest = new OllamaChatRequest
        {
            Model = string.IsNullOrWhiteSpace(request.Model) ? _defaultModel : request.Model,
            Messages = request.Messages.Select(m => new OllamaMessage
            {
                Role = m.Role,
                Content = m.Content,
                ToolCalls = m.ToolCalls?.Select(tc => new OllamaToolCall
                {
                    Id = tc.Id,
                    Type = tc.Type,
                    Function = new OllamaFunctionCall
                    {
                        Name = tc.Function.Name,
                        Arguments = tc.Function.Arguments
                    }
                }).ToList() ?? new List<OllamaToolCall>()
            }).ToList(),
            Tools = request.Tools?.Select(t => new OllamaFunctionDefinition
            {
                Type = t.Type,
                Function = new OllamaFunctionSchema
                {
                    Name = t.Function.Name,
                    Description = t.Function.Description,
                    Parameters = JsonSerializer.SerializeToElement(t.Function.Parameters)
                }
            }).ToList() ?? new List<OllamaFunctionDefinition>(),
            ToolChoice = request.ToolChoice,
            Options = new OllamaOptions
            {
                Temperature = request.Temperature,
                TopP = request.TopP,
                NumPredict = request.MaxTokens,
                Seed = request.Seed
            },
            Stream = request.Stream
        };

        var response = await _httpClient.PostAsJsonAsync("api/chat", ollamaRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"فشل طلب Ollama [{ (int)response.StatusCode }] للنموذج '{ollamaRequest.Model}': {errorBody}");
        }

        var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"استجابة فارغة من Ollama للنموذج '{ollamaRequest.Model}'.");

        return new ChatResponse
        {
            Id = Guid.NewGuid().ToString(),
            Model = ollamaResponse.Model ?? _defaultModel,
            CreatedAt = DateTime.UtcNow,
            Message = new ChatMessage
            {
                Role = ollamaResponse.Message?.Role ?? "assistant",
                Content = ollamaResponse.Message?.Content ?? string.Empty,
                ToolCalls = ollamaResponse.Message?.ToolCalls?.Select(tc => new ToolCall
                {
                    Id = tc.Id,
                    Type = tc.Type,
                    Function = new FunctionCall
                    {
                        Name = tc.Function.Name,
                        Arguments = tc.Function.Arguments
                    }
                }).ToArray() ?? Array.Empty<ToolCall>()
            },
            Usage = new Usage
            {
                PromptTokens = ollamaResponse.PromptEvalCount,
                CompletionTokens = ollamaResponse.EvalCount,
                TotalTokens = ollamaResponse.PromptEvalCount + ollamaResponse.EvalCount
            },
            FinishReason = ollamaResponse.DoneReason ?? "stop"
        };
    }

    public async Task<EmbeddingResponse> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var request = new OllamaEmbeddingRequest
        {
            Model = _defaultModel,
            Prompt = text
        };

        var response = await _httpClient.PostAsJsonAsync("api/embeddings", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: cancellationToken);

        return new EmbeddingResponse
        {
            Model = _defaultModel,
            Embedding = result?.Embedding?.ToArray() ?? Array.Empty<float>(),
            Usage = new Usage { TotalTokens = 0 }
        };
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<OllamaModelsResponse>("api/tags", cancellationToken);
        return response?.Models.Select(m => m.Name).ToList() ?? new List<string>();
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>تفاصيل النماذج المثبتة (الاسم والحجم وتاريخ التعديل)</summary>
    public async Task<IReadOnlyList<OllamaModelDetails>> ListModelDetailsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<OllamaModelsResponse>("api/tags", cancellationToken);
        return response?.Models
            .Select(m => new OllamaModelDetails(m.Name, m.Size, m.ModifiedAt, m.Digest))
            .ToList()
            ?? new List<OllamaModelDetails>();
    }

    /// <summary>
    /// تنزيل أو تحديث نموذج عبر Ollama (/api/pull) مع تتبّع التقدّم.
    /// يقرأ استجابة NDJSON ويبلّغ عن نسبة التقدّم عند توفر الإجمالي.
    /// </summary>
    public async Task PullModelAsync(string model, IProgress<OllamaPullProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("اسم النموذج مطلوب", nameof(model));

        var body = new OllamaPullRequest { Model = model, Stream = true };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull")
        {
            Content = JsonContent.Create(body)
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            OllamaPullChunk? chunk;
            try { chunk = JsonSerializer.Deserialize<OllamaPullChunk>(line, _jsonOptions); }
            catch { continue; }

            if (chunk == null) continue;
            if (!string.IsNullOrEmpty(chunk.Error))
                throw new InvalidOperationException($"فشل تنزيل النموذج: {chunk.Error}");

            progress?.Report(new OllamaPullProgress(model, chunk.Status ?? "", chunk.Completed, chunk.Total));
        }
    }

    /// <summary>حذف نموذج مثبّت</summary>
    public async Task DeleteModelAsync(string model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("اسم النموذج مطلوب", nameof(model));

        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/delete")
        {
            Content = JsonContent.Create(new OllamaPullRequest { Model = model, Stream = false })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// إنشاء نموذج من ملف GGUF محلي (داخل مجلد التطبيق) عبر Modelfile مؤقت و/api/create
    /// </summary>
    public async Task CreateModelFromGgufAsync(
        string modelName,
        string ggufPath,
        string? systemPrompt = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("اسم النموذج مطلوب", nameof(modelName));
        if (!System.IO.File.Exists(ggufPath)) throw new System.IO.FileNotFoundException("ملف GGUF غير موجود", ggufPath);

        var modelfile = new System.Text.StringBuilder();
        modelfile.AppendLine($"FROM {ggufPath}");
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            modelfile.AppendLine($"SYSTEM {systemPrompt}");

        progress?.Report("جارٍ إنشاء النموذج من الملف المحلي…");

        var body = new OllamaCreateRequest
        {
            Model = modelName,
            Modelfile = modelfile.ToString(),
            Stream = false
        };

        using var response = await _httpClient.PostAsJsonAsync("api/create", body, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        progress?.Report("تم إنشاء النموذج من ملف GGUF المحلي.");
    }

    /// <summary>أنواع Ollama الداخلية</summary>
    private sealed record OllamaChatRequest
    {
        public string Model { get; init; } = string.Empty;
        public List<OllamaMessage> Messages { get; init; } = new();
        public List<OllamaFunctionDefinition> Tools { get; init; } = new();
        public string ToolChoice { get; init; } = "auto";
        public OllamaOptions Options { get; init; } = new();
        public bool Stream { get; init; } = false;
    }

    private sealed record OllamaMessage
    {
        public string Role { get; init; } = "user";
        public string Content { get; init; } = string.Empty;
        public List<OllamaToolCall> ToolCalls { get; init; } = new();
    }

    private sealed record OllamaToolCall
    {
        public string Id { get; init; } = string.Empty;
        public string Type { get; init; } = "function";
        public OllamaFunctionCall Function { get; init; } = new();
    }

    private sealed record OllamaFunctionCall
    {
        public string Name { get; init; } = string.Empty;
        public string Arguments { get; init; } = "{}";
    }

    private sealed record OllamaFunctionDefinition
    {
        public string Type { get; init; } = "function";
        public OllamaFunctionSchema Function { get; init; } = new();
    }

    private sealed record OllamaFunctionSchema
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public object Parameters { get; init; } = new();
    }

    private sealed record OllamaOptions
    {
        public float Temperature { get; init; } = 0.1f;
        public float TopP { get; init; } = 0.9f;
        public int NumPredict { get; init; } = 4096;
        public int? Seed { get; init; }
    }

    private sealed record OllamaChatResponse
    {
        public string Model { get; init; } = string.Empty;
        public OllamaMessage? Message { get; init; }
        public bool Done { get; init; }
        public string? DoneReason { get; init; }
        public int PromptEvalCount { get; init; }
        public int EvalCount { get; init; }
    }

    private sealed record OllamaEmbeddingRequest
    {
        public string Model { get; init; } = string.Empty;
        public string Prompt { get; init; } = string.Empty;
    }

    private sealed record OllamaEmbeddingResponse
    {
        public List<float> Embedding { get; init; } = new();
    }

    private sealed record OllamaModelsResponse
    {
        public List<OllamaModelInfo> Models { get; init; } = new();
    }

    private sealed record OllamaModelInfo
    {
        public string Name { get; init; } = string.Empty;
        public DateTime ModifiedAt { get; init; }
        public long Size { get; init; }
        public string Digest { get; init; } = string.Empty;
    }

    private sealed record OllamaPullRequest
    {
        public string Model { get; init; } = string.Empty;
        public bool Stream { get; init; } = true;
    }

    private sealed record OllamaCreateRequest
    {
        public string Model { get; init; } = string.Empty;
        public string Modelfile { get; init; } = string.Empty;
        public bool Stream { get; init; } = false;
    }

    private sealed record OllamaPullChunk
    {
        public string? Status { get; init; }
        public string? Digest { get; init; }
        public long? Total { get; init; }
        public long? Completed { get; init; }
        public string? Error { get; init; }
    }
}

/// <summary>تفاصيل نموذج مثبّت في Ollama</summary>
public sealed record OllamaModelDetails(string Name, long Size, DateTime ModifiedAt, string Digest);

/// <summary>تقدّم تنزيل نموذج</summary>
public sealed record OllamaPullProgress(string Model, string Status, long? Completed, long? Total)
{
    /// <summary>النسبة المئوية 0..100 إن توفر الإجمالي، وإلا -1</summary>
    public double Percent => (Total.HasValue && Total.Value > 0 && Completed.HasValue)
        ? (Completed.Value * 100.0 / Total.Value)
        : -1;
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AwadTimetable.AI.Interfaces;

namespace AwadTimetable.AI.Clients;

/// <summary>
/// عميل متوافق مع OpenAI Chat Completions (LM Studio، LocalAI، vLLM، OpenAI…).
/// يسمح بربط أي نموذج مستقبلي يوفر واجهة /v1/chat/completions دون تغيير التطبيق.
/// </summary>
public sealed class OpenAiCompatibleClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    public OpenAiCompatibleClient(HttpClient http, string baseUrl, string? apiKey, string model)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUrl = (baseUrl ?? "http://localhost:1234/v1").TrimEnd('/');
        _model = string.IsNullOrWhiteSpace(model) ? "qwen2.5:7b" : model;

        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public string ModelName => _model;
    public bool IsLocal => true;
    public bool SupportsFunctionCalling => true;
    public bool SupportsStreaming => false;

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model,
            ["messages"] = request.Messages.Select(m => new Dictionary<string, object?>
            {
                ["role"] = m.Role,
                ["content"] = m.Content,
                ["tool_call_id"] = m.ToolCallId,
                ["tool_calls"] = m.ToolCalls.Count == 0 ? null : m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Function.Name, arguments = tc.Function.Arguments }
                })
            }).ToList(),
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = false,
        };

        if (request.Tools.Count > 0)
        {
            payload["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Function.Name,
                    description = t.Function.Description,
                    parameters = ToSchema(t.Function.Parameters),
                }
            }).ToList();
            payload["tool_choice"] = request.ToolChoice;
        }

        using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/chat/completions", payload, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"فشل اتصال النموذج (HTTP {(int)resp.StatusCode}): {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var choice = root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
            ? choices[0]
            : default;

        var message = choice.ValueKind == JsonValueKind.Object && choice.TryGetProperty("message", out var m)
            ? m
            : default;

        var content = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var c)
            ? (c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : c.ToString())
            : "";

        var toolCalls = new List<ToolCall>();
        if (message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("tool_calls", out var calls) &&
            calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in calls.EnumerateArray())
            {
                var id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                var fn = tc.TryGetProperty("function", out var f) ? f : default;
                var name = fn.ValueKind == JsonValueKind.Object && fn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var args = fn.ValueKind == JsonValueKind.Object && fn.TryGetProperty("arguments", out var a)
                    ? (a.ValueKind == JsonValueKind.String ? a.GetString() ?? "{}" : a.ToString())
                    : "{}";

                if (!string.IsNullOrWhiteSpace(name))
                    toolCalls.Add(new ToolCall
                    {
                        Id = id,
                        Type = "function",
                        Function = new FunctionCall { Name = name, Arguments = args },
                    });
            }
        }

        var usage = new Usage();
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            usage = new Usage
            {
                PromptTokens = u.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                CompletionTokens = u.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0,
                TotalTokens = u.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : 0,
            };
        }

        return new ChatResponse
        {
            Model = _model,
            Message = new ChatMessage
            {
                Role = "assistant",
                Content = content ?? "",
                ToolCalls = toolCalls,
            },
            Usage = usage,
            FinishReason = choice.ValueKind == JsonValueKind.Object && choice.TryGetProperty("finish_reason", out var fr)
                ? fr.GetString() ?? "stop"
                : "stop",
        };
    }

    public async Task<EmbeddingResponse> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/embeddings", new { model = _model, input = text }, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"فشل التضمين: {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        var arr = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
        var values = arr.EnumerateArray().Select(e => e.GetSingle()).ToArray();

        return new EmbeddingResponse { Model = _model, Embedding = values };
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{_baseUrl}/models", cancellationToken);
            if (!resp.IsSuccessStatusCode) return new List<string>();

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return new List<string>();

            return data.EnumerateArray()
                .Select(e => e.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "")
                .Where(s => s.Length > 0)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{_baseUrl}/models", cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static object ToSchema(JsonSchema schema)
    {
        var props = new Dictionary<string, object?>();
        foreach (var (name, prop) in schema.Properties)
        {
            var p = new Dictionary<string, object?>
            {
                ["type"] = prop.Type,
                ["description"] = prop.Description,
            };
            if (prop.Enum.Count > 0) p["enum"] = prop.Enum;
            if (prop.Items is not null) p["items"] = ToSchema(prop.Items);
            props[name] = p;
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = schema.Required,
        };
    }

    private static string Truncate(string text)
        => text.Length > 300 ? text[..300] : text;
}

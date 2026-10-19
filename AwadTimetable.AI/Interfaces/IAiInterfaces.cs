using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Services;

namespace AwadTimetable.AI.Interfaces;

/// <summary>
/// واجهة عميل النموذج اللغوي
/// </summary>
public interface ILlmClient
{
    string ModelName { get; }
    bool IsLocal { get; }
    bool SupportsFunctionCalling { get; }
    bool SupportsStreaming { get; }

    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default);
    Task<EmbeddingResponse> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<List<string>> ListModelsAsync(CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// طلب محادثة
/// </summary>
public sealed record ChatRequest
{
    public string Model { get; init; } = string.Empty;
    public IReadOnlyList<ChatMessage> Messages { get; init; } = Array.Empty<ChatMessage>();
    public IReadOnlyList<FunctionDefinition> Tools { get; init; } = Array.Empty<FunctionDefinition>();
    public string ToolChoice { get; init; } = "auto";
    public float Temperature { get; init; } = 0.1f;
    public int MaxTokens { get; init; } = 4096;
    public float TopP { get; init; } = 0.9f;
    public int Seed { get; init; }
    public bool Stream { get; init; } = false;
}

/// <summary>
/// رسالة محادثة
/// </summary>
public sealed record ChatMessage
{
    public string Role { get; init; } = "user"; // system, user, assistant, tool
    public string Content { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? ToolCallId { get; init; }
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = Array.Empty<ToolCall>();
}

/// <summary>
/// استدعاء أداة
/// </summary>
public sealed record ToolCall
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Type { get; init; } = "function";
    public FunctionCall Function { get; init; } = new();
}

/// <summary>
/// استدعاء دالة
/// </summary>
public sealed record FunctionCall
{
    public string Name { get; init; } = string.Empty;
    public string Arguments { get; init; } = "{}";
}

/// <summary>
/// تعريف دالة
/// </summary>
public sealed record FunctionDefinition
{
    public string Type { get; init; } = "function";
    public FunctionSchema Function { get; init; } = new();
}

/// <summary>
/// مخطط دالة
/// </summary>
public sealed record FunctionSchema
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public JsonSchema Parameters { get; init; } = new();
}

/// <summary>
/// مخطط JSON
/// </summary>
public sealed record JsonSchema
{
    public string Type { get; init; } = "object";
    public Dictionary<string, JsonSchemaProperty> Properties { get; init; } = new();
    public IReadOnlyList<string> Required { get; init; } = Array.Empty<string>();
}

/// <summary>
/// خاصية مخطط JSON
/// </summary>
public sealed record JsonSchemaProperty
{
    public string Type { get; init; } = "string";
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Enum { get; init; } = Array.Empty<string>();
    public JsonSchema? Items { get; init; }
}

/// <summary>
/// رد محادثة
/// </summary>
public sealed record ChatResponse
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Model { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public ChatMessage Message { get; init; } = new();
    public Usage Usage { get; init; } = new();
    public string FinishReason { get; init; } = "stop";
}

/// <summary>
/// استخدام التوكنز
/// </summary>
public sealed record Usage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
}

/// <summary>
/// رد التضمين (Embedding)
/// </summary>
public sealed record EmbeddingResponse
{
    public string Model { get; init; } = string.Empty;
    public IReadOnlyList<float> Embedding { get; init; } = Array.Empty<float>();
    public Usage Usage { get; init; } = new();
}

/// <summary>
/// واجهة الأدوات (Function Calling)
/// </summary>
public interface IAiTool
{
    string Name { get; }
    string Description { get; }
    JsonSchema Schema { get; }
    Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken = default);
}

/// <summary>
/// نتيجة تنفيذ أداة
/// </summary>
public sealed record ToolResult
{
    public bool IsSuccess { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? Error { get; init; }
    public bool RequiresConfirmation { get; init; }
    public object? Data { get; init; }

    public static ToolResult Ok(string content, object? data = null, bool requiresConfirmation = false)
        => new ToolResult { IsSuccess = true, Content = content, Data = data, RequiresConfirmation = requiresConfirmation };

    public static ToolResult Fail(string error)
        => new ToolResult { IsSuccess = false, Error = error };
}

/// <summary>
/// سجل الأدوات
/// </summary>
public interface IToolRegistry
{
    void Register(IAiTool tool);
    void Unregister(string name);
    IAiTool? GetTool(string name);
    IReadOnlyList<IAiTool> GetAllTools();
    IReadOnlyList<FunctionDefinition> GetSchemas();
    Task<ToolResult> ExecuteAsync(string name, object parameters, CancellationToken cancellationToken = default);
}

/// <summary>
/// ذاكرة المحادثة
/// </summary>
public interface IConversationMemory
{
    Task AddAsync(string userMessage, string assistantResponse);
    Task<List<ChatMessage>> GetContextAsync(int maxMessages = 20);
    Task ClearAsync();
    Task<string> BuildContextAsync(string currentQuery, int maxTokens = 4000);
}

/// <summary>
/// وكيل الذكاء الاصطناعي للجدولة
/// </summary>
public interface ISchedulingAgent
{
    Task<AgentResponse> ProcessAsync(string userInput, CancellationToken cancellationToken = default);
    Task<AgentResponse> ProcessWithContextAsync(string userInput, TimetableContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// رد الوكيل
/// </summary>
public sealed record AgentResponse
{
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<ExecutedAction> ExecutedActions { get; init; } = Array.Empty<ExecutedAction>();
    public bool RequiresConfirmation { get; init; }
    public IReadOnlyList<PendingAction> PendingActions { get; init; } = Array.Empty<PendingAction>();
    public AgentState State { get; init; }
}

/// <summary>
/// إجراء منفذ
/// </summary>
public sealed record ExecutedAction
{
    public string ToolName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public object? Result { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// إجراء معلق
/// </summary>
public sealed record PendingAction
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ToolName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public object Parameters { get; init; } = new();
    public bool RequiresConfirmation { get; init; }
}

/// <summary>
/// حالة الوكيل
/// </summary>
public enum AgentState
{
    Idle,
    Thinking,
    Executing,
    WaitingForConfirmation,
    Completed,
    Error,
}

/// <summary>
/// واجهة معالج أوامر الذكاء الاصطناعي
/// </summary>
public interface IAICommandHandler
{
    Task<CommandResult> HandleAsync(string command, TimetableContext context, CancellationToken cancellationToken = default);
    IReadOnlyList<CommandDefinition> GetAvailableCommands();
}

/// <summary>
/// تعريف أمر
/// </summary>
public sealed record CommandDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;
    public IReadOnlyList<CommandParameter> Parameters { get; init; } = Array.Empty<CommandParameter>();
}

/// <summary>
/// معامل أمر
/// </summary>
public sealed record CommandParameter
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = "string";
    public string Description { get; init; } = string.Empty;
    public bool Required { get; init; } = true;
    public string? DefaultValue { get; init; }
}

/// <summary>
/// نتيجة أمر
/// </summary>
public sealed record CommandResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public object? Data { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// سياق وكيل الذكاء الاصطناعي
/// </summary>
public sealed record AgentContext
{
    public TimetableContext TimetableContext { get; init; } = new();
    public IReadOnlyList<ChatMessage> ConversationHistory { get; init; } = Array.Empty<ChatMessage>();
    public Dictionary<string, object> Variables { get; init; } = new();
    public AgentConfiguration Configuration { get; init; } = new();
}

/// <summary>
/// إعدادات الوكيل
/// </summary>
public sealed record AgentConfiguration
{
    public float Temperature { get; init; } = 0.1f;
    public int MaxTokens { get; init; } = 4096;
    public int MaxIterations { get; init; } = 10;
    public float ConfidenceThreshold { get; init; } = 0.7f;
    public bool RequireConfirmationForMutations { get; init; } = true;
    public bool AutoExecuteSafeActions { get; init; } = false;
}
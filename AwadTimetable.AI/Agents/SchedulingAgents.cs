using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwadTimetable.AI.Interfaces;
using AwadTimetable.AI.Tools;
using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;

namespace AwadTimetable.AI.Agents;

/// <summary>
/// وكيل الجدولة الذكي
/// </summary>
public class SchedulingAgent : ISchedulingAgent
{
    private readonly ILlmClient _llm;
    private readonly IToolRegistry _tools;
    private readonly IConversationMemory _memory;
    private readonly ITimetableGenerator _generator;
    private readonly IConstraintValidator _validator;
    private readonly IConflictDetector _detector;
    private readonly AgentConfiguration _config;

    private readonly string _systemPrompt = @"
انت وكيل ذكي متخصص في جدولة الحصص المدرسية (نظام عوض للجداول).
مهمتك: فهم طلبات المستخدم بلغة عربية طبيعية وتحويلها إلى إجراءات جدولة صحيحة.

القدرات المتاحة:
- إنشاء/تعديل/حذف الحصص (Lessons)
- إضافة/إزالة القيود (Constraints)
- كشف التضاربات (Conflicts)
- تحسين الجدول (Optimization)
- شرح قرارات الجدولة
- استيراد بيانات من Excel/صور

قواعد صارمة:
1. لا تنفذ أي إجراء دون تأكيد المستخدم للتغييرات الحرجة
2. اشرح سبب كل قرار بلغة عربية بسيطة
3. إذا كان الطلب غامضاً، اطلب توضيحاً
4. احترم القيود الصلبة (Hard Constraints) دائماً
5. قدم بدائل عند تعذر الطلب
6. استخدم اللغة العربية فقط في جميع الردود

تنسيق الرد:
- ابدأ بملخص مختصر لما فهمته
- اذكر الإجراءات المقترحة
- اطلب التأكيد قبل التنفيذ
";

    public SchedulingAgent(
        ILlmClient llm,
        IToolRegistry tools,
        IConversationMemory memory,
        ITimetableGenerator generator,
        IConstraintValidator validator,
        IConflictDetector detector,
        AgentConfiguration config)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _config = config;
    }

    public async Task<AgentResponse> ProcessAsync(string userInput, CancellationToken cancellationToken = default)
    {
        var context = await _memory.GetContextAsync();
        return await ProcessWithContextAsync(userInput, new TimetableContext(), cancellationToken);
    }

    public async Task<AgentResponse> ProcessWithContextAsync(string userInput, TimetableContext context, CancellationToken cancellationToken = default)
    {
        var conversationContext = await _memory.GetContextAsync();

        var messages = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = _systemPrompt }
        };

        messages.AddRange(conversationContext);
        messages.Add(new ChatMessage { Role = "user", Content = userInput });

        var request = new ChatRequest
        {
            Messages = messages,
            Tools = _tools.GetSchemas(),
            ToolChoice = "auto",
            Temperature = _config.Temperature,
            MaxTokens = _config.MaxTokens
        };

        var response = await _llm.ChatAsync(request, cancellationToken);

        var toolResults = new List<ToolResult>();
        var pendingActions = new List<PendingAction>();

        foreach (var toolCall in response.Message.ToolCalls ?? Array.Empty<ToolCall>())
        {
            var args = JsonSerializer.Deserialize<object>(toolCall.Function.Arguments) ?? new object();
            var result = await _tools.ExecuteAsync(toolCall.Function.Name, args, cancellationToken);

            toolResults.Add(result);

            if (result.IsSuccess && result.RequiresConfirmation)
            {
                pendingActions.Add(new PendingAction
                {
                    ToolName = toolCall.Function.Name,
                    Description = "تنفيذ: " + toolCall.Function.Name,
                    Parameters = JsonSerializer.Deserialize<object>(toolCall.Function.Arguments)!,
                    RequiresConfirmation = true
                });
            }
        }

        var finalResponse = await _llm.ChatAsync(new ChatRequest
        {
            Messages = messages
                .Concat(new[] { new ChatMessage { Role = "assistant", Content = response.Message.Content ?? string.Empty, ToolCalls = response.Message.ToolCalls ?? Array.Empty<ToolCall>() } })
                .Concat(toolResults.Select(tr => new ChatMessage
                {
                    Role = "tool",
                    Content = tr.Content,
                    ToolCallId = ""
                }))
                .ToList(),
            Tools = _tools.GetSchemas(),
            ToolChoice = "auto",
            Temperature = _config.Temperature
        }, cancellationToken);

        await _memory.AddAsync(userInput, finalResponse.Message.Content);

        return new AgentResponse
        {
            Message = finalResponse.Message.Content,
            ExecutedActions = toolResults.Select(tr => new ExecutedAction
            {
                Description = tr.Content,
                Result = tr.Data
            }).ToList(),
            RequiresConfirmation = pendingActions.Any(p => p.RequiresConfirmation),
            PendingActions = pendingActions,
            State = pendingActions.Any() ? AgentState.WaitingForConfirmation : AgentState.Completed
        };
    }

    /// <summary>
    /// وكيل تحليل القيود
    /// </summary>
    public class ConstraintParserAgent : IAICommandHandler
    {
        private readonly ILlmClient _llm;
        private readonly IToolRegistry _tools;

        public ConstraintParserAgent(ILlmClient llm, IToolRegistry tools)
        {
            _llm = llm;
            _tools = tools;
        }

        public async Task<CommandResult> HandleAsync(string command, TimetableContext context, CancellationToken cancellationToken = default)
        {
            var prompt = $@"
تحليل الطلب التالي وتحويله إلى قيود جدولة:
'{command}'

السياق الحالي:
- عدد الحصص: {context.Lessons.Count}
- عدد المعلمين: {context.Teachers.Count}
- عدد الغرف: {context.Rooms.Count}
- عدد الفصول: {context.Classes.Count}

أعد قائمة بالقيود المقترحة مع نوعها (Hard/Soft) وأولويتها.
";

            var request = new ChatRequest
            {
                Messages = new[]
                {
                    new ChatMessage { Role = "system", Content = "أنت محلل قيود جدولة خبير. رد بتنسيق JSON فقط." },
                    new ChatMessage { Role = "user", Content = prompt }
                },
                Temperature = 0.1f
            };

            var response = await _llm.ChatAsync(request);

            return new CommandResult
            {
                Success = true,
                Message = response.Message.Content,
                Data = response.Message.Content
            };
        }

        public IReadOnlyList<CommandDefinition> GetAvailableCommands()
        {
            return Array.Empty<CommandDefinition>();
        }
    }

    /// <summary>
    /// وكيل حل التضاربات
    /// </summary>
    public class ConflictResolutionAgent : IAICommandHandler
    {
        private readonly IConflictDetector _detector;
        private readonly ITimetableGenerator _generator;

        public ConflictResolutionAgent(IConflictDetector detector, ITimetableGenerator generator)
        {
            _detector = detector;
            _generator = generator;
        }

        public Task<CommandResult> HandleAsync(string command, TimetableContext context, CancellationToken cancellationToken = default)
        {
            var conflicts = _detector.DetectConflicts(new Timetable
            {
                Lessons = context.Lessons.ToList()
            });

            var suggestions = new List<string>();

            foreach (var conflict in conflicts)
            {
                suggestions.Add("تضارب " + conflict.Type + ": " + conflict.Description);
            }

            return Task.FromResult(new CommandResult
            {
                Success = true,
                Message = "تم العثور على " + conflicts.Count + " تضارب. الاقتراحات: " + string.Join("; ", suggestions),
                Data = conflicts
            });
        }

        public IReadOnlyList<CommandDefinition> GetAvailableCommands()
        {
            return new[]
            {
                new CommandDefinition
                {
                    Name = "resolve_conflicts",
                    Description = "حل التضاربات تلقائياً",
                    Usage = "resolve_conflicts [timetableId]"
                }
            };
        }
    }
}
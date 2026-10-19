using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using AwadTimetable.AI.Interfaces;
using AwadTimetable.AI.Tools;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.UI.Services;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// نموذج عرض لوحة المحادثة الذكية
/// </summary>
public partial class AIChatPanelViewModel : ObservableObject
{
    private readonly ILlmClient _llmClient;
    private readonly IToolRegistry _toolRegistry;
    private readonly IConversationMemory _memory;
    private readonly ISettingsService _settings;
    private readonly ILogger<AIChatPanelViewModel> _logger;

    private string? _modelOverride;

    /// <summary>
    /// تعليمات النظام: تُرسل في بداية كل محادثة. تمنح المساعد صلاحية كاملة لتنفيذ
    /// العمليات على التطبيق وقاعدة البيانات عبر الأدوات، وتمنعه من الاعتذار بعدم الصلاحية.
    /// </summary>
    private const string SystemPrompt =
        "أنت «عوض»، المساعد الذكي المدمج في نظام إدارة الجداول المدرسية. تتحدث العربية دائمًا وبإيجاز.\n" +
        "لديك صلاحية كاملة على التطبيق وقاعدة البيانات: القراءة والإضافة والتعديل والحذف والربط في المعلمين والمواد والفصول والقاعات والحصص والإسناد والقيود.\n" +
        "قواعد إلزامية:\n" +
        "1) لا تقل أبدًا «ليس لدي صلاحية» أو «لا أستطيع». لديك كل الصلاحيات.\n" +
        "2) نفّذ طلب المستخدم فعليًا باستدعاء الأدوات المناسبة (function calling). لا تخترع نتائج ولا تدّعِ التنفيذ دون استدعاء أداة.\n" +
        "3) الأسماء بالعربية مقبولة: مرّر الاسم كما ذكره المستخدم (معلم/مادة/فصل/قاعة) وستُطابق الأداة تلقائيًا. استخدم المعرّف (Guid) فقط إن توفّر.\n" +
        "4) إن كانت معلومة أساسية ناقصة، اسأل سؤالًا واحدًا موجزًا ثم نفّذ فور ورود الجواب.\n" +
        "5) عند الحذف ستُطلب موافقة المستخدم تلقائيًا — لا تتردد في استدعاء أداة الحذف.\n" +
        "6) بعد التنفيذ أخبر المستخدم بما فعلته فعليًا في سطر أو سطرين.\n" +
        "الأدوات المتاحة: إدارة المعلمين/المواد/الفصول/القاعات (إضافة، تعديل، حذف، عرض)، إدارة الحصص (إضافة، تحريك، حذف)، إدارة الإسناد (إضافة، حذف، تعطيل، توليد الحصص)، إدارة بنود القواعد (وقت انتهاء، ربط معلمين… مع الأولوية والنقل بين المعلمين)، القيود، فحص التضاربات، شرح الجدول، تحسين الجدول، توليد الجدول (generate_timetable)، تصميم قوالب الطباعة (design_print_template).\n" +
        "معرفة التطبيق وآلية العمل:\n" +
        "- مسار العمل: جدول جديد (اسم/مدرسة/سنة/مراحل) ← أيام الدراسة والفترات (توقيت لكل مرحلة وفسح) ← المعلمون (اسم مختصر ولون) ← المواد ← الفصول/الشعب ← الإسناد (معلم+مادة+شعب+عدد حصص) ← «توليد الحصص من الإسنادات» ← القواعد/القيود ← «توليد الجدول».\n" +
        "- الانتظار: خطة انتظار (مرحلة/فئة 1-2-3/معلم/عدد حصص) تُنشئ شعب انتظار تُجدول مع الجدول. الإشراف (Dozor) لفسح القاعات.\n" +
        "- الطباعة: 8 قوالب جاهزة (aSc كلاسيكي، حديث، أنيق، مضغوط، فارغ، بطاقة معلم/فصل/قاعة) + قوالب مخصصة. عند طلب تخصيص الطباعة استدعِ design_print_template واملأ الألوان/الخط/الحجم/الخيارات من كلام المستخدم (مثل «أزرق داكن»، «بلا ملخص»، «A3 عرضي»، «خط كبير») ثم تُفتح المعاينة.\n" +
        "- الملفات: قاعدة SQLite في LocalApplicationData\\AwadTimetable، إعدادات settings.json، قوالب الطباعة print-templates.json، واستيراد/تصدير CSV/XLSX/XML/iCal/HTML.\n" +
        "- القواعد المتقدمة (manage_rules): مرّر حقل parameters بنص JSON: {days,periods} لحجب أوقات، {afterPeriod} لمنع الحصص بعد فترة، {maxPerDay} لحد المادة اليومي، {maxConsecutive} لأقصى حصص متتالية، {maxGapsPerDay} لأقصى فجوات يومية، {minPerDay,maxPerDay} للعبء اليومي.\n" +
        "- المحركات: CP-SAT (افتراضي)، FET عبر fet-cl، aSc بالتعاون (تصدير/استيراد).";

    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messages = new();

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private bool _showSuggestions = true;

    private readonly AiModelSetupService? _aiSetup;

    /// <summary>نص حالة المساعد (متاح/النموذج غير محمّل/غير متصل)</summary>
    [ObservableProperty]
    private string _aiStatusText = "جارٍ فحص المساعد…";

    [ObservableProperty]
    private bool _isAiReady;

    public AIChatPanelViewModel(
        ILlmClient llmClient,
        IToolRegistry toolRegistry,
        IConversationMemory memory,
        ISettingsService settings,
        ILogger<AIChatPanelViewModel> logger,
        AiModelSetupService? aiSetup = null)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _aiSetup = aiSetup;

        _ = RefreshAiStatusAsync();

        // رسالة ترحيب
        Messages.Add(new ChatMessageViewModel
        {
            Role = "assistant",
            Content = "مرحباً! أنا مساعدك الذكي للجداول المدرسية. كيف يمكنني مساعدتك اليوم؟",
            Timestamp = DateTime.Now,
            IsFromUser = false
        });
    }

    /// <summary>إعادة فحص توفّر المساعد والنموذج</summary>
    [RelayCommand]
    private async Task RefreshAiStatusAsync()
    {
        try
        {
            if (_aiSetup is null)
            {
                AiStatusText = "المساعد: غير مهيّأ";
                IsAiReady = false;
                return;
            }

            var status = await _aiSetup.GetStatusAsync();
            AiStatusText = status.Message;
            IsAiReady = status.Ready;
        }
        catch
        {
            AiStatusText = "تعذر فحص المساعد الذكي";
            IsAiReady = false;
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsProcessing) return;

        // احترام مفتاح تفعيل المساعد من الإعدادات
        if (!_settings.Get<bool>(SettingsKeys.EnableAi, true))
        {
            InputText = string.Empty;
            Messages.Add(new ChatMessageViewModel
            {
                Role = "assistant",
                Content = "المساعد الذكي معطل حاليًا. يمكنك تفعيله من: الإعدادات ← الذكاء الاصطناعي ← تفعيل المساعد الذكي.",
                Timestamp = DateTime.Now,
                IsFromUser = false,
                IsError = true
            });
            return;
        }

        var userMessage = InputText;
        InputText = string.Empty;
        IsProcessing = true;

        // إضافة رسالة المستخدم
        var userMsg = new ChatMessageViewModel
        {
            Role = "user",
            Content = userMessage,
            Timestamp = DateTime.Now,
            IsFromUser = true
        };
        Messages.Add(userMsg);

        try
        {
            // تأكد من توفر النموذج المطلوب (يختار نموذجًا مثبّتًا إن غاب المحدَّد)
            await EnsureModelAvailableAsync();

            // بناء السياق من سجل المحادثة الظاهر (نتجاهل نتائج الأدوات القديمة)
            var messages = new List<ChatMessage>
            {
                // تعليمات النظام أولًا: تمنح المساعد صلاحية كاملة وتوجّهه لاستخدام الأدوات
                new ChatMessage { Role = "system", Content = SystemPrompt }
            };
            messages.AddRange(Messages
                .Where(m => !m.IsToolResult)
                .Select(m => new ChatMessage
                {
                    Role = m.IsFromUser ? "user" : "assistant",
                    Content = m.Content
                }));

            var schemas = _toolRegistry.GetSchemas();
            _logger.LogInformation("محادثة المساعد: النموذج={Model} عدد الأدوات={ToolCount}", _modelOverride, schemas.Count);

            ChatRequest BuildRequest(List<ChatMessage> msgs) => new()
            {
                Model = _modelOverride ?? string.Empty,
                Messages = msgs,
                Tools = schemas,
                ToolChoice = "auto",
                Temperature = 0.1f,
                MaxTokens = 4096
            };

            var response = await _llmClient.ChatAsync(BuildRequest(messages));

            // حلقة تنفيذ الأدوات متعددة الدورات: حتى يقدّم النموذج ردًا نهائيًا (بحد أقصى 4 دورات)
            var iterations = 0;
            while (response.Message.ToolCalls is { Count: > 0 } && iterations++ < 8)
            {
                // إعادة رسالة المساعد الحاملة لاستدعاءات الأدوات ضرورية ليربط Ollama النتائج بها
                messages.Add(response.Message);

                foreach (var toolCall in response.Message.ToolCalls)
                {
                    object args;
                    try { args = System.Text.Json.JsonSerializer.Deserialize<object>(toolCall.Function.Arguments) ?? new { }; }
                    catch { args = new { }; }

                    var result = await _toolRegistry.ExecuteAsync(toolCall.Function.Name, args);
                    _logger.LogInformation("نفّذ المساعد الأداة {Tool} → نجاح={Ok}", toolCall.Function.Name, result.IsSuccess);

                    messages.Add(new ChatMessage
                    {
                        Role = "tool",
                        Content = string.IsNullOrWhiteSpace(result.Content) ? (result.Error ?? string.Empty) : result.Content,
                        ToolCallId = toolCall.Id,
                        Name = toolCall.Function.Name
                    });

                    Messages.Add(new ChatMessageViewModel
                    {
                        Role = "tool",
                        Content = string.IsNullOrWhiteSpace(result.Content) ? (result.Error ?? string.Empty) : result.Content,
                        Timestamp = DateTime.Now,
                        IsFromUser = false,
                        IsToolResult = true,
                        ToolName = toolCall.Function.Name
                    });
                }

                response = await _llmClient.ChatAsync(BuildRequest(messages));
            }

            // رد المساعد النهائي
            Messages.Add(new ChatMessageViewModel
            {
                Role = "assistant",
                Content = string.IsNullOrWhiteSpace(response.Message.Content)
                    ? "تم تنفيذ العملية."
                    : response.Message.Content,
                Timestamp = DateTime.Now,
                IsFromUser = false
            });

            ScrollToBottom();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "فشل إرسال رسالة للمساعد الذكي");
            Messages.Add(new ChatMessageViewModel
            {
                Role = "assistant",
                Content = $"عذراً، حدث خطأ: {ex.Message}",
                Timestamp = DateTime.Now,
                IsFromUser = false,
                IsError = true
            });
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// يختار أفضل نموذج مثبّت متعدد اللغات (جيد في العربية) حسب ترتيب تفضيل.
    /// </summary>
    private static string? PreferredOf(IReadOnlyList<string> installed)
    {
        // ترتيب تفضيلي: نماذج متعددة اللغات قوية في العربية أولاً
        var preferences = new[] { "gemma", "qwen2.5:", "qwen2.5-", "llama3", "llama", "mistral", "aya", "command-r" };
        foreach (var pref in preferences)
        {
            var hit = installed.FirstOrDefault(m => m.Contains(pref, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>
    /// يضمن وجود نموذج صالح: إن لم يكن النموذج المحدَّد مثبّتًا في Ollama،
    /// يختار أول نموذج مثبّت ويحفظه في الإعدادات (شامل التنزيل/التحديث اليدوي من الإعدادات).
    /// </summary>
    private async Task EnsureModelAvailableAsync()
    {
        try
        {
            var configured = _settings.Get<string>(SettingsKeys.AiModel, App.DefaultAiModel) ?? App.DefaultAiModel;
            var installed = await _llmClient.ListModelsAsync();

            if (installed.Count == 0)
            {
                throw new InvalidOperationException(
                    "لا يوجد نموذج مثبّت في Ollama. افتح الإعدادات ← الذكاء الاصطناعي ونزّل نموذجًا (مثلاً qwen2.5-coder:7b).");
            }

            // مطابقة دقيقة أو مطابقة الأساس دون الوسم (مثال: qwen2.5-coder:7b-latest)
            var match = installed.FirstOrDefault(m => string.Equals(m, configured, StringComparison.OrdinalIgnoreCase))
                        ?? installed.FirstOrDefault(m => m.StartsWith(configured.Split(':')[0], StringComparison.OrdinalIgnoreCase));

            // تفضيل النماذج متعددة اللغات الجيدة في العربية عند عدم وجود المحدَّد
            var effective = match ?? PreferredOf(installed) ?? installed[0];

            // حدّث النموذج الفعّال على العميل (مشترك singleton) والإعدادات
            if (_llmClient is AwadTimetable.AI.Clients.OllamaClient oc)
                oc.ModelName = effective;
            _modelOverride = effective;

            if (!string.Equals(effective, configured, StringComparison.OrdinalIgnoreCase))
            {
                _settings.Set(SettingsKeys.AiModel, effective);
                await _settings.SaveAsync();
                _logger.LogInformation("تم تبديل نموذج المساعد تلقائيًا إلى {Model}", effective);
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر التحقق من النماذج المثبّتة");
            throw new InvalidOperationException(
                "تعذّر الاتصال بـ Ollama. تأكد من تشغيله على http://localhost:11434 ثم أعد المحاولة.", ex);
        }
    }

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        Messages.Add(new ChatMessageViewModel
        {
            Role = "assistant",
            Content = "تم مسح المحادثة. كيف يمكنني مساعدتك؟",
            Timestamp = DateTime.Now,
            IsFromUser = false
        });
    }

    [RelayCommand]
    private void ToggleSuggestions()
    {
        ShowSuggestions = !ShowSuggestions;
    }

    private void ScrollToBottom()
    {
        // سيتم التعامل معه في الكود الخلفي
    }
}

/// <summary>
/// نموذج عرض رسالة المحادثة
/// </summary>
public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _role = "user";

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private DateTime _timestamp = DateTime.Now;

    [ObservableProperty]
    private bool _isFromUser = true;

    [ObservableProperty]
    private bool _isToolResult = false;

    [ObservableProperty]
    private bool _isError = false;

    [ObservableProperty]
    private string? _toolName;

    public bool IsFromAssistant => !IsFromUser && !IsToolResult;
    public bool IsFromTool => IsToolResult;
}

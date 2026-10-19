using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Services;
using AwadTimetable.Data;
using AwadTimetable.Data.Repositories;
using AwadTimetable.Solver;
using AwadTimetable.AI;
using AwadTimetable.AI.Interfaces;
using AwadTimetable.AI.Clients;
using AwadTimetable.AI.Tools;
using AwadTimetable.AI.Agents;
using AwadTimetable.UI.ViewModels;
using AwadTimetable.UI.Views;
using AwadTimetable.UI.Services;

namespace AwadTimetable.UI;

/// <summary>
/// نقطة دخول التطبيق
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private ISettingsService? _appSettings;
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>النموذج الافتراضي المفضّل للمساعد الذكي (متعدد اللغات، جيد في العربية)</summary>
    public const string DefaultAiModel = "qwen2.5:7b";

    /// <summary>
    /// يسجّل أدوات المساعد الذكي الحقيقية (التي تعمل فعليًا على البيانات) في سجل الأدوات.
    /// لا تُسجَّل الأدوات الوهمية التي تُرجع نتائج ثابتة.
    /// </summary>
    /// <summary>
    /// عند أول تشغيل (أو غياب النموذج): يتحقق من Ollama ويعرض نافذة الإعداد اختيارياً.
    /// لا يُظهر شيئاً إذا كان النموذج جاهزاً أو إذا سبق أن اختار المستخدم «لاحقاً».
    /// </summary>
    private async Task CheckAiModelOnStartupAsync()
    {
        try
        {
            var setup = _host?.Services.GetService<Services.AiModelSetupService>();
            if (setup is null || !setup.Enabled) return;

            var status = await setup.GetStatusAsync();
            Log.Information("حالة المساعد الذكي: {Message}", status.Message);

            if (status.ModelInstalled) return;
            if (setup.SetupHandled && !status.OllamaReachable) return;
            if (setup.SetupHandled && !setup.AutoDownload) return;

            await Dispatcher.InvokeAsync(() =>
            {
                var settings = _host!.Services.GetRequiredService<ISettingsService>();
                var dialog = new Views.AiSetupDialog(setup, settings)
                {
                    Owner = Application.Current.MainWindow
                };
                dialog.ShowDialog();
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "تعذر فحص المساعد الذكي عند بدء التشغيل");
        }
    }

    private static void RegisterAiTools(IServiceProvider services)
    {
        try
        {
            var registry = services.GetRequiredService<AwadTimetable.AI.Interfaces.IToolRegistry>();
            registry.Register(services.GetRequiredService<Services.SchoolQueryTool>());
            registry.Register(services.GetRequiredService<Services.CheckConflictsAiTool>());
            registry.Register(services.GetRequiredService<Services.ExplainScheduleAiTool>());
            registry.Register(services.GetRequiredService<Services.CreateLessonAiTool>());
            registry.Register(services.GetRequiredService<Services.OptimizeTimetableAiTool>());
            registry.Register(services.GetRequiredService<Services.AddConstraintAiTool>());
            // أدوات الإدارة الكاملة (CRUD) — تمنح المساعد صلاحية كاملة على البيانات
            registry.Register(services.GetRequiredService<Services.ManageTeachersAiTool>());
            registry.Register(services.GetRequiredService<Services.ManageSubjectsAiTool>());
            registry.Register(services.GetRequiredService<Services.ManageClassesAiTool>());
            registry.Register(services.GetRequiredService<Services.ManageRoomsAiTool>());
            registry.Register(services.GetRequiredService<Services.ManageLessonsAiTool>());
            registry.Register(services.GetRequiredService<Services.ManageAssignmentsAiTool>());
            registry.Register(services.GetRequiredService<Services.ManageConstraintsAiTool>());
            registry.Register(services.GetRequiredService<Services.ManageRulesAiTool>());
            registry.Register(services.GetRequiredService<Services.GenerateTimetableAiTool>());
            registry.Register(services.GetRequiredService<Services.DesignPrintTemplateAiTool>());
            registry.Register(services.GetRequiredService<Services.SplitLessonAiTool>());
            Log.Information("تم تسجيل أدوات المساعد الذكي: {Tools}",
                string.Join(", ", registry.GetAllTools().Select(t => t.Name)));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "تعذّر تسجيل أدوات المساعد الذكي");
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // إعداد Serilog
        ConfigureLogging();

        // حماية: تسجيل الأخطاء غير المعالجة وعدم إغلاق التطبيق فجأة
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "خطأ غير معالجة في واجهة المستخدم");
            MessageBox.Show($"حدث خطأ غير متوقع:\n{args.Exception.Message}\n\n(تم تسجيل التفاصيل في السجل)",
                "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log.Error(args.Exception, "خطأ غير معالجة في مهمة خلفية");
            args.SetObserved();
        };

        Log.Information("بدء تشغيل {ApplicationName} v{Version}", AppConstants.ApplicationName, AppConstants.Version);

        try
        {
            _host = CreateHostBuilder().Build();
            await _host.StartAsync();

            Services = _host.Services;

            // ترقية مخطط قاعدة البيانات قبل أي تحميل للمعلمين (أعمدة البديل الثلاثة)
            try
            {
                using var schemaScope = Services.CreateScope();
                var schemaDb = schemaScope.ServiceProvider.GetRequiredService<TimetableDbContext>();
                TimetableStorageService.EnsureDatabaseSchema(schemaDb);
            }
            catch (Exception schemaEx)
            {
                Log.Error(schemaEx, "فشل ترقية مخطط قاعدة البيانات عند بدء التشغيل");
                throw new InvalidOperationException(
                    "تعذر تجهيز قاعدة البيانات. أُوقف التطبيق لتجنب العمل على مخطط غير مكتمل.",
                    schemaEx);
            }

            // إعداد الثقافة العربية
            SetupArabicCulture();

            // تسجيل أدوات المساعد الذكي الحقيقية في السجل
            RegisterAiTools(Services);

            // تحميل الإعدادات المحفوظة وتطبيقها على واجهة التطبيق
            var settings = Services.GetRequiredService<ISettingsService>();
            _appSettings = settings;
            await settings.LoadAsync();
            ApplyDisplaySettings(settings);
            settings.SettingsChanged += (_, args) =>
            {
                if (args.Key is SettingsKeys.DefaultFontFamily or SettingsKeys.DefaultFontSize)
                    Dispatcher.Invoke(() => ApplyDisplaySettings(settings));
            };

            // عرض النافذة الرئيسية
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // فحص المساعد الذكي وتنزيل النموذج عند أول تشغيل (اختياري)
            _ = CheckAiModelOnStartupAsync();

            // تسجيل حدث إغلاق النافذة لإيقاف المضيف
            mainWindow.Closed += async (s, e) => 
            {
                if (_host != null)
                {
                    try
                    {
                        await _host.StopAsync(TimeSpan.FromSeconds(5));
                        _host.Dispose();
                    }
                    catch (Exception hostEx)
                    {
                        Log.Warning(hostEx, "تعذر إيقاف المضيف عند إغلاق النافذة");
                    }
                    finally
                    {
                        _host = null;
                    }
                }
                Log.CloseAndFlush();
            };

            Log.Information("تم تشغيل التطبيق بنجاح");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "فشل في تشغيل التطبيق");
            MessageBox.Show($"فشل في تشغيل التطبيق:\n{ex.Message}", "خطأ فادح", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // حفظ الإعدادات قبل إغلاق المضيف (المزوّد يصبح غير صالح بعد Dispose)
        if (_appSettings != null)
        {
            try
            {
                await _appSettings.SaveAsync();
            }
            catch (Exception saveEx)
            {
                Log.Warning(saveEx, "تعذر حفظ الإعدادات عند الخروج");
            }
        }

        if (_host != null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
            catch (Exception hostEx)
            {
                Log.Warning(hostEx, "تعذر إيقاف المضيف عند الخروج");
            }
            finally
            {
                _host = null;
            }
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void ConfigureLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AwadTimetable", "Logs", "log-.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            // لا تُسجّل استعلامات EF وطلبات HTTP بالتفصيل: تُضخّم السجل دون قيمة تشخيصية
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();
    }

    private void SetupArabicCulture()
    {
        var culture = new System.Globalization.CultureInfo(AppConstants.ArabicCultureCode);
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
    }

    public App()
    {
        // أي نافذة تُنشأ لاحقًا (حوارات/نوافذ فرعية) تحمل نفس إعدادات الخط
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window) ApplyDisplayToWindow(window);
            }));
    }

    private FontFamily _appFontFamily = new(AppConstants.DefaultArabicFont);
    private double _appFontSize = 13;
    private bool _displaySettingsLoaded;

    /// <summary>
    /// يطبّق إعدادات العرض (الخط وحجمه) على كل النوافذ - الخط المضمَّن Cairo غير متاح فنعود للخط الافتراضي
    /// </summary>
    private void ApplyDisplaySettings(ISettingsService settings)
    {
        var saved = settings.Get<string>(SettingsKeys.DefaultFontFamily, AppConstants.DefaultArabicFont);
        var name = string.IsNullOrWhiteSpace(saved) || saved.StartsWith("Cairo", StringComparison.OrdinalIgnoreCase)
            ? AppConstants.DefaultArabicFont
            : saved;

        _appFontFamily = new FontFamily(name);
        _appFontSize = settings.Get<int>(SettingsKeys.DefaultFontSize, 13);
        _displaySettingsLoaded = true;

        // مورد حي: كل عنصر مرتبط بـ ArabicFont يتغيّر فورًا دون إعادة تشغيل
        Resources["ArabicFont"] = _appFontFamily;

        foreach (Window window in Windows)
            ApplyDisplayToWindow(window);
    }

    private void ApplyDisplayToWindow(Window window)
    {
        if (!_displaySettingsLoaded) return;
        window.FontFamily = _appFontFamily;
        window.FontSize = _appFontSize;
    }

    private IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            // تسجيل الخدمات الأساسية - استخدام المحلل من مشروع Solver
            services.AddSingleton<CpSatTimetableSolver>();
            services.AddSingleton<ITimetableGenerator>(sp => sp.GetRequiredService<CpSatTimetableSolver>());
            services.AddSingleton<IConstraintValidator, ConstraintValidator>();
            services.AddSingleton<IConflictDetector, ConflictDetector>();

            // محرك FET الخارجي: مسار fet-cl.exe يُقرأ من الإعدادات عند كل توليد
            services.AddSingleton<FetEngine>(sp => new FetEngine(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FetEngine>>(),
                sp.GetRequiredService<IConstraintValidator>(),
                sp.GetRequiredService<ISettingsService>().Get<string>(FetSettings.FetClPathKey, string.Empty)));

            // محرك aSc بالتعاون: مسار roz.exe ومجلد التصدير من الإعدادات
            services.AddSingleton<AscEngine>(sp =>
            {
                var settings = sp.GetRequiredService<ISettingsService>();
                var exportFolder = settings.Get<string>(AscSettings.ExportFolderKey, string.Empty);
                if (string.IsNullOrWhiteSpace(exportFolder))
                {
                    exportFolder = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AwadTimetable", "aSc-export");
                }
                return new AscEngine(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AscEngine>>(),
                    sp.GetRequiredService<IConstraintValidator>(),
                    settings.Get<string>(AscSettings.AscExePathKey, string.Empty),
                    exportFolder);
            });

            services.AddSingleton<IGeneratorResolver, GeneratorResolver>();
            services.AddSingleton<IAiVisionService, AiVisionService>();
            services.AddSingleton<AiModelSetupService>();

            // خدمات قاعدة البيانات
            services.AddDbContext<TimetableDbContext>();
            services.AddScoped<ISchoolRepository, SchoolRepository>();
            services.AddScoped<ITimetableRepository, TimetableRepository>();
            services.AddScoped<ITeacherRepository, TeacherRepository>();
            services.AddScoped<ISubjectRepository, SubjectRepository>();
            services.AddScoped<IRoomRepository, RoomRepository>();
            services.AddScoped<IClassRepository, ClassRepository>();
            services.AddScoped<ILessonRepository, LessonRepository>();

            // خدمات الذكاء الاصطناعي
            services.AddHttpClient<OllamaClient>(client =>
            {
                client.BaseAddress = new Uri("http://localhost:11434/");
                client.Timeout = TimeSpan.FromMinutes(5);
            });
            // عميل LLM يقرأ العنوان والنموذج من الإعدادات المحفوظة
            services.AddSingleton<OllamaClient>(sp =>
            {
                var settings = sp.GetRequiredService<ISettingsService>();
                var url = settings.Get<string>(SettingsKeys.OllamaBaseUrl, "http://localhost:11434") ?? "http://localhost:11434";
                var model = settings.Get<string>(SettingsKeys.AiModel, DefaultAiModel) ?? DefaultAiModel;
                var http = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(nameof(OllamaClient));
                return new OllamaClient(http, url, model);
            });
            services.AddSingleton<ILlmClient>(sp => LlmClientFactory.Create(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetService<IHttpClientFactory>()));
            services.AddSingleton<IConversationMemory, InMemoryConversationMemory>();
            services.AddSingleton<IToolRegistry, ToolRegistry>();

            // أدوات المساعد الذكي الحقيقية (تعمل على الجدول المفتوح فعليًا)
            services.AddSingleton<Services.AssistantToolContext>();
            services.AddTransient<Services.SchoolQueryTool>();
            services.AddTransient<Services.CheckConflictsAiTool>();
            services.AddTransient<Services.ExplainScheduleAiTool>();
            services.AddTransient<Services.CreateLessonAiTool>();
            services.AddTransient<Services.OptimizeTimetableAiTool>();
            services.AddTransient<Services.AddConstraintAiTool>();
            services.AddTransient<Services.ManageTeachersAiTool>();
            services.AddTransient<Services.ManageSubjectsAiTool>();
            services.AddTransient<Services.ManageClassesAiTool>();
            services.AddTransient<Services.ManageRoomsAiTool>();
            services.AddTransient<Services.ManageLessonsAiTool>();
            services.AddTransient<Services.ManageAssignmentsAiTool>();
            services.AddTransient<Services.ManageConstraintsAiTool>();
            services.AddTransient<Services.ManageRulesAiTool>();
            services.AddTransient<Services.GenerateTimetableAiTool>();
            services.AddTransient<Services.DesignPrintTemplateAiTool>();
            services.AddTransient<Services.SplitLessonAiTool>();
            services.AddTransient<ISchedulingAgent, SchedulingAgent>();

            // ViewModels
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<TimetableViewModel>();
            services.AddTransient<TeacherManagementViewModel>();
            services.AddTransient<SubjectManagementViewModel>();
            services.AddTransient<RoomManagementViewModel>();
            services.AddTransient<ClassManagementViewModel>();
            services.AddTransient<ConstraintEditorViewModel>();
            services.AddTransient<AIChatViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<AIChatPanelViewModel>();

            // Views
            services.AddTransient<MainWindow>();
            services.AddTransient<TimetableView>();
            services.AddTransient<AIChatPanel>();

            // إعدادات
            services.AddSingleton<ISettingsService, SettingsService>();

            // خدمة المدرسة الحالية
            services.AddSingleton<CurrentSchoolService>();

            // الطباعة/PDF
            services.AddSingleton<AwadTimetable.Printing.Services.IPrintService, AwadTimetable.Printing.Services.PrintService>();

            // تقرير الويب (HTML كامل بأسلوب aSc)
            services.AddSingleton<AwadTimetable.Printing.Services.IWebReportService, AwadTimetable.Printing.Services.WebReportService>();

            // حفظ/فتح الجداول في قاعدة البيانات
            services.AddSingleton<TimetableStorageService>();
            services.AddSingleton<SubstitutionStorageService>();
            services.AddSingleton<Services.WorkspaceService>();

            // خادم المشاركة عبر الرابط المحلي (QR)
            services.AddSingleton<ShareServerService>();

            // استيراد aSc XML
            services.AddSingleton<AscXmlImportService>();

            // مظهر المعلمين (اسم مختصر + لون مخصص) — محفوظ خارج قاعدة البيانات
            services.AddSingleton<TeacherAppearanceService>();
        });
}
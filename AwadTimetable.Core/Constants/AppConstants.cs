namespace AwadTimetable.Core.Constants;

/// <summary>
/// الثوابت العامة للتطبيق
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// اسم التطبيق
    /// </summary>
    public const string ApplicationName = "Awad Timetable";

    /// <summary>
    /// اسم المطور
    /// </summary>
    public const string DeveloperName = "عوض لافي الزبيدي";

    /// <summary>
    /// حقوق النشر الديناميكية (السنة الحالية)
    /// </summary>
    public static string CopyrightNotice => $"جميع الحقوق محفوظة © {DateTime.Now.Year} - {DeveloperName}";

    /// <summary>
    /// إصدار التطبيق
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// سنة البناء
    /// </summary>
    public const int BuildYear = 2026;

    /// <summary>
    /// اسم ملف قاعدة البيانات
    /// </summary>
    public const string DatabaseFileName = "awad_timetable.db";

    /// <summary>
    /// امتداد ملف الجدول
    /// </summary>
    public const string TimetableFileExtension = ".awad";

    /// <summary>
    /// اسم الخط العربي الافتراضي
    /// </summary>
    public const string DefaultArabicFont = "Segoe UI";

    /// <summary>
    /// كود اللغة العربية (السعودية)
    /// </summary>
    public const string ArabicCultureCode = "ar-SA";

    /// <summary>
    /// عدد الأيام في الأسبوع
    /// </summary>
    public const int DaysPerWeek = 7;

    /// <summary>
    /// الحد الأقصى لعدد الحصص في اليوم
    /// </summary>
    public const int MaxPeriodsPerDay = 12;

    /// <summary>
    /// الحد الأدنى لمدة الحصة (بالدقائق)
    /// </summary>
    public const int MinLessonDurationMinutes = 30;

    /// <summary>
    /// الحد الأقصى لمدة الحصة (بالدقائق)
    /// </summary>
    public const int MaxLessonDurationMinutes = 120;

    /// <summary>
    /// مفتاح الترخيص في السجل
    /// </summary>
    public const string RegistryLicenseKey = "LicenseKey";

    /// <summary>
    /// مفتاح كود التسجيل في السجل
    /// </summary>
    public const string RegistryRegistrationCode = "RegistrationCode";

    /// <summary>
    /// مسار السجل
    /// </summary>
    public const string RegistryPath = @"SOFTWARE\AwadTimetable";

    /// <summary>
    /// رابط الدعم التقني
    /// </summary>
    public const string SupportEmail = "support@awadtimetable.com";

    /// <summary>
    /// رابط الموقع الإلكتروني
    /// </summary>
    public const string WebsiteUrl = "https://awadtimetable.com";
}

/// <summary>
/// مفاتيح الإعدادات
/// </summary>
public static class SettingsKeys
{
    public const string Theme = "Theme";
    public const string Language = "Language";
    public const string AutoSave = "AutoSave";
    public const string AutoSaveIntervalMinutes = "AutoSaveIntervalMinutes";
    public const string ShowWatermark = "ShowWatermark";
    public const string WatermarkText = "WatermarkText";
    public const string DefaultFontSize = "DefaultFontSize";
    public const string DefaultFontFamily = "DefaultFontFamily";
    public const string ShowGridLines = "ShowGridLines";
    public const string EnableAnimations = "EnableAnimations";
    public const string AutoBackup = "AutoBackup";
    public const string BackupIntervalHours = "BackupIntervalHours";
    public const string MaxBackupCount = "MaxBackupCount";
    public const string WindowState = "WindowState";
    public const string WindowWidth = "WindowWidth";
    public const string WindowHeight = "WindowHeight";
    public const string WindowLeft = "WindowLeft";
    public const string WindowTop = "WindowTop";
    public const string RecentFiles = "RecentFiles";
    public const string RecentTimetables = "RecentTimetables";
    public const string LastOpenedTimetable = "LastOpenedTimetable";
    public const string AiModel = "AiModel";
    public const string AiTemperature = "AiTemperature";
    public const string OllamaBaseUrl = "OllamaBaseUrl";
    public const string EnableAi = "EnableAi";
    public const string PrintWatermarkText = "PrintWatermarkText";
    public const string PrintCopyrightText = "PrintCopyrightText";
    public const string PrintFontSize = "PrintFontSize";
    public const string PrintFontFamily = "PrintFontFamily";
    public const string PrintMarginTop = "PrintMarginTop";
    public const string PrintMarginBottom = "PrintMarginBottom";
    public const string PrintMarginLeft = "PrintMarginLeft";
    public const string PrintMarginRight = "PrintMarginRight";
    public const string AutoSaveEnabled = "AutoSaveEnabled";
    public const string BackupEnabled = "BackupEnabled";
    public const string LeftPanelVisible = "LeftPanelVisible";
    public const string RightPanelVisible = "RightPanelVisible";
    public const string ZoomLevel = "ZoomLevel";
    public const string GenerationComplexity = "GenerationComplexity";
    public const string GenerationTimeLimitMinutes = "GenerationTimeLimitMinutes";
    public const string GenerationSeed = "GenerationSeed";
    public const string GenerationVariants = "GenerationVariants";
    public const string DraftMode = "DraftMode";
    public const string RelaxConstraints = "RelaxConstraints";
    public const string MaxConflictsAllowed = "MaxConflictsAllowed";
}

/// <summary>
/// قوالب الطباعة الافتراضية
/// </summary>
public static class DefaultPrintTemplates
{
    public const string Grid = "GridTemplate";
    public const string Teacher = "TeacherTemplate";
    public const string Class = "ClassTemplate";
    public const string Student = "StudentTemplate";
    public const string Summary = "SummaryTemplate";
}
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AwadTimetable.Solver;

/// <summary>
/// محرك aSc بالتعاون: لا يُولِّد الجدول آلياً (aSc لا يملك وضع توليد من سطر الأوامر).
/// يكتب الجدول بصيغة aSc، ويفتح aSc إن أمكن، ثم يُعيد للمستخدم تعليمات الاستيراد.
/// النتيجة تُستورد لاحقاً عبر مستورِد aSc المعتاد.
/// </summary>
public sealed class AscEngine : ITimetableGenerator
{
    private readonly ILogger<AscEngine> _logger;
    private readonly IConstraintValidator _validator;
    private readonly string _ascExePath;
    private readonly string _exportFolder;

    public AscEngine(ILogger<AscEngine> logger, IConstraintValidator validator, string ascExePath, string exportFolder)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _ascExePath = ascExePath ?? string.Empty;
        _exportFolder = string.IsNullOrWhiteSpace(exportFolder)
            ? throw new ArgumentException("مجلد التصدير مطلوب", nameof(exportFolder))
            : exportFolder;
    }

    public Task<GenerationResult> GenerateAsync(
        Timetable timetable,
        GenerationOptions options,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(timetable);

        if (timetable.Lessons.Count == 0)
            return Task.FromResult(GenerationResult.Failure("لا توجد حصص في الجدول — أضف الحصص أولاً."));

        Directory.CreateDirectory(_exportFolder);
        var path = Path.Combine(_exportFolder, $"{SafeName(timetable.Name)}-asc.xml");

        progress?.Report("تصدير الجدول بصيغة aSc…");
        AscXmlFormat.Export(timetable, path);

        var opened = TryOpenAsc(path);

        var message = opened
            ? "تم تصدير الجدول وفتح aSc. ولّد الجدول داخل aSc، ثم صدّره إلى ملف XML واستورده هنا."
            : $"تم تصدير الجدول إلى:\n{path}\nافتح aSc من هذا الملف، ولّد الجدول داخله، ثم صدّره واستورده.";

        _logger.LogInformation("تم تصدير الجدول لـ aSc: {Path} (فتح aSc: {Opened})", path, opened);

        // لم يُولَّد جدول جديد بعد، لذا نُبقي الجدول كما هو ونعيد حالة واضحة
        return Task.FromResult(new GenerationResult
        {
            Success = false,
            Timetable = timetable,
            ErrorMessage = message,
            Duration = TimeSpan.Zero,
            Warnings = new System.Collections.Generic.List<string>(),
        });
    }

    public Task<OptimizationResult> OptimizeAsync(
        Timetable timetable,
        OptimizationOptions options,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
        => Task.FromResult(OptimizationResult.NoImprovement(Score.Zero, TimeSpan.Zero));

    public Task<ValidationResult> ValidateAsync(
        Timetable timetable,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
        => _validator.ValidateAsync(TimetableContextFactory.Build(timetable), null, cancellationToken);

    private bool TryOpenAsc(string xmlPath)
    {
        if (string.IsNullOrWhiteSpace(_ascExePath) || !File.Exists(_ascExePath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(_ascExePath) { Arguments = $"\"{xmlPath}\"", UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر فتح aSc");
            return false;
        }
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "timetable" : cleaned;
    }
}

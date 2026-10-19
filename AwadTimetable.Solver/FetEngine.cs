using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using Microsoft.Extensions.Logging;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Solver;

/// <summary>
/// محرك FET خارجي: يصدّر الجدول إلى ملف .fet، يشغّل fet-cl، ثم يقرأ نتيجة الحصص من XML.
/// لا يُدمج أي شيفرة من FET؛ التكامل عبر سطر الأوامر فقط.
/// </summary>
public sealed class FetEngine : ITimetableGenerator
{
    private readonly ILogger<FetEngine> _logger;
    private readonly IConstraintValidator _validator;
    private readonly string _fetClPath;

    public FetEngine(ILogger<FetEngine> logger, IConstraintValidator validator, string fetClPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _fetClPath = string.IsNullOrWhiteSpace(fetClPath)
            ? throw new ArgumentException("مسار fet-cl مطلوب", nameof(fetClPath))
            : fetClPath;
    }

    public async Task<GenerationResult> GenerateAsync(
        Timetable timetable,
        GenerationOptions options,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(timetable);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();

        if (!File.Exists(_fetClPath))
            return GenerationResult.Failure($"لم يُعثر على fet-cl في: {_fetClPath}. ثبّت FET أو حدّد مساره في الإعدادات.");

        if (timetable.Lessons.Count == 0)
            return GenerationResult.Failure("لا توجد حصص في الجدول — أضف الحصص أولًا ثم أعد التوليد.");

        var workDir = Path.Combine(Path.GetTempPath(), "awad-fet", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var inputPath = Path.Combine(workDir, "input.fet");
            var outputDir = Path.Combine(workDir, "out");
            Directory.CreateDirectory(outputDir);

            progress?.Report("تصدير الجدول إلى صيغة FET…");
            var fetXml = FetExporter.Build(timetable);
            fetXml.Save(inputPath);

            var seconds = Math.Max(1, (int)options.TimeLimit.TotalSeconds);
            progress?.Report($"تشغيل FET ({seconds} ث)…");

            var run = await RunFetClAsync(inputPath, outputDir, seconds, cancellationToken);
            if (run.Cancelled)
                return GenerationResult.Failure("تم إلغاء توليد الجدول من قبل المستخدم.");

            if (run.ExitCode != 0)
            {
                _logger.LogWarning("fet-cl انتهى برمز {Code}: {Output}", run.ExitCode, run.Output);
                return GenerationResult.Failure($"فشل FET (الرمز {run.ExitCode}):\n{Tail(run.Output)}");
            }

            var placements = FetResultReader.ReadActivities(outputDir, timetable.Name);
            if (placements.Count == 0)
                return GenerationResult.Failure("أنهى FET بلا خطأ لكن لم يُعثر على نتائج الحصص في الخرج.");

            var applied = ApplyPlacements(timetable, placements);
            if (applied < timetable.Lessons.Count)
            {
                _logger.LogWarning("FET أعاد {Applied} من {Total} حصة فقط", applied, timetable.Lessons.Count);
            }

            var validation = _validator.Validate(TimetableContextFactory.Build(timetable));
            timetable.UpdateStatistics(timetable.Lessons.Count, validation.ConflictsCount, validation.QualityScore);
            timetable.Generate();

            stopwatch.Stop();
            return new GenerationResult
            {
                Success = true,
                Timetable = timetable,
                LessonsScheduled = applied,
                LessonsFailed = timetable.Lessons.Count - applied,
                ConflictsCount = validation.ConflictsCount,
                QualityScore = validation.QualityScore,
                Duration = stopwatch.Elapsed,
                Iterations = 0,
                Violations = validation.Violations,
                Warnings = new List<string>()
            };
        }
        catch (OperationCanceledException)
        {
            return GenerationResult.Failure("تم إلغاء توليد الجدول من قبل المستخدم.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في محرك FET");
            return GenerationResult.Failure($"خطأ في محرك FET: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* تنظيف مؤقت فقط */ }
        }
    }

    public Task<OptimizationResult> OptimizeAsync(
        Timetable timetable,
        OptimizationOptions options,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        // FET لا يُحسّن جدولاً قائماً بالطريقة نفسها؛ يُعاد توليده عبر GenerateAsync
        return Task.FromResult(OptimizationResult.NoImprovement(Score.Zero, TimeSpan.Zero));
    }

    public Task<ValidationResult> ValidateAsync(
        Timetable timetable,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
        => _validator.ValidateAsync(TimetableContextFactory.Build(timetable), null, cancellationToken);

    private static int ApplyPlacements(Timetable timetable, IReadOnlyDictionary<int, (DomainDayOfWeek day, int period)> placements)
    {
        var applied = 0;

        for (var i = 0; i < timetable.Lessons.Count; i++)
        {
            var lesson = timetable.Lessons.ElementAt(i);
            if (!placements.TryGetValue(i + 1, out var slot)) continue;

            lesson.Reschedule(slot.day, PeriodNumber.Create((byte)slot.period));
            applied++;
        }

        return applied;
    }

    private static string Tail(string text)
    {
        var lines = text.Split('\n');
        return string.Join("\n", lines.TakeLast(15));
    }

    private sealed record RunResult(int ExitCode, string Output, bool Cancelled);

    private async Task<RunResult> RunFetClAsync(string inputPath, string outputDir, int seconds, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _fetClPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add($"--inputfile={inputPath}");
        psi.ArgumentList.Add($"--outputdir={outputDir}");
        psi.ArgumentList.Add($"--timelimitseconds={seconds.ToString(CultureInfo.InvariantCulture)}");
        psi.ArgumentList.Add("--writetimetablesxml=true");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("تعذر تشغيل fet-cl");
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var reg = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* تم الإنهاء */ }
        });

        await process.WaitForExitAsync(CancellationToken.None);

        string text;
        lock (output) text = output.ToString();

        return new RunResult(process.ExitCode, text, ct.IsCancellationRequested);
    }
}

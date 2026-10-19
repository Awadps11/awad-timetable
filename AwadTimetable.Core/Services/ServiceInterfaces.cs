using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Core.Constraints;

namespace AwadTimetable.Core.Services;

/// <summary>
/// واجهة مولد الجدول الزمني
/// </summary>
public interface ITimetableGenerator
{
    Task<GenerationResult> GenerateAsync(
        Timetable timetable,
        GenerationOptions options,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null);

    Task<OptimizationResult> OptimizeAsync(
        Timetable timetable,
        OptimizationOptions options,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null);

    Task<ValidationResult> ValidateAsync(
        Timetable timetable,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null);
}

/// <summary>درجة تعقيد التوليد — تُضبط ميزانية البحث (الوقت/التكرارات/عدد المحاولات).</summary>
public enum GenerationComplexity
{
    Normal,
    Large,
    Huge
}

/// <summary>
/// خيارات التوليد
/// </summary>
public sealed class GenerationOptions
{
    public int MaxIterations { get; set; } = 1000;
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(5);
    public bool UseParallel { get; set; } = true;
    public int RandomSeed { get; set; } = Environment.TickCount;
    public bool RespectFixedLessons { get; set; } = true;
    public bool AllowPartialSolution { get; set; } = false;
    public int MaxConflictsAllowed { get; set; } = 0;
    public bool EnableIncremental { get; set; } = true;

    /// <summary>درجة التعقيد: Normal / Large / Huge (تُضاعف ميزانية البحث).</summary>
    public GenerationComplexity Complexity { get; set; } = GenerationComplexity.Normal;

    /// <summary>عدد المتغيّرات المولَّدة لاختيار أفضلها (1 = متغيّر واحد فقط).</summary>
    public int VariantCount { get; set; } = 1;

    /// <summary>وضع المسودة: توليد سريع تقريبي لجدول مبدئي.</summary>
    public bool DraftMode { get; set; } = false;

    /// <summary>المهلة الفعلية بعد تطبيق درجة التعقيد (وضع المسودة يضغطها).</summary>
    public TimeSpan EffectiveTimeLimit
    {
        get
        {
            if (DraftMode)
                return TimeSpan.FromSeconds(Math.Min(20, Math.Max(5, TimeLimit.TotalSeconds / 6)));

            var factor = Complexity switch
            {
                GenerationComplexity.Huge => 3,
                GenerationComplexity.Large => 2,
                _ => 1
            };

            return TimeSpan.FromTicks(TimeLimit.Ticks * factor);
        }
    }

    /// <summary>أقصى عدد تكرارات فعلي بعد تطبيق درجة التعقيد.</summary>
    public int EffectiveMaxIterations => Complexity switch
    {
        GenerationComplexity.Huge => MaxIterations * 5,
        GenerationComplexity.Large => MaxIterations * 3,
        _ => MaxIterations
    };

    /// <summary>أقصى عدد محاولات (بذور) فعلية — وضع المسودة يحصرها بواحد.</summary>
    public int EffectiveVariantCount => DraftMode ? 1 : Math.Max(1, VariantCount);
}

/// <summary>
/// خيارات التحسين
/// </summary>
public sealed class OptimizationOptions
{
    public int MaxIterations { get; set; } = 5000;
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(10);
    public double ImprovementThreshold { get; set; } = 0.01;
    public bool UseLocalSearch { get; set; } = true;
    public bool UseSimulatedAnnealing { get; set; } = true;
    public double InitialTemperature { get; set; } = 100.0;
    public double CoolingRate { get; set; } = 0.95;
}

/// <summary>
/// نتيجة التوليد
/// </summary>
public sealed class GenerationResult
{
    public bool Success { get; init; }
    public Timetable? Timetable { get; init; }
    public int LessonsScheduled { get; init; }
    public int LessonsFailed { get; init; }
    public int ConflictsCount { get; init; }
    public Score QualityScore { get; init; } = null!;
    public TimeSpan Duration { get; init; }
    public int Iterations { get; init; }
    public IReadOnlyList<ConstraintViolation> Violations { get; init; } = Array.Empty<ConstraintViolation>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? ErrorMessage { get; init; }

    public static GenerationResult SuccessResult(Timetable timetable, int scheduled, int failed, int conflicts, Score score, TimeSpan duration, int iterations)
        => new GenerationResult
        {
            Success = true,
            Timetable = timetable,
            LessonsScheduled = scheduled,
            LessonsFailed = failed,
            ConflictsCount = conflicts,
            QualityScore = score,
            Duration = duration,
            Iterations = iterations
        };

    public static GenerationResult Failure(string errorMessage)
        => new GenerationResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// نتيجة التحسين
/// </summary>
public sealed class OptimizationResult
{
    public bool Improved { get; init; }
    public Score OldScore { get; init; } = null!;
    public Score NewScore { get; init; } = null!;
    public double ImprovementPercentage { get; init; }
    public int ConflictsReduced { get; init; }
    public TimeSpan Duration { get; init; }
    public int Iterations { get; init; }
    public IReadOnlyList<ConstraintViolation> RemainingViolations { get; init; } = Array.Empty<ConstraintViolation>();

    public static OptimizationResult NoImprovement(Score score, TimeSpan duration)
        => new OptimizationResult
        {
            Improved = false,
            OldScore = score,
            NewScore = score,
            ImprovementPercentage = 0,
            Duration = duration
        };
}

/// <summary>
/// نتيجة التحقق
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<ConstraintViolation> Violations { get; init; } = Array.Empty<ConstraintViolation>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public Score QualityScore { get; init; } = null!;
    public int TotalLessons { get; init; }
    public int ConflictsCount { get; init; }
}

/// <summary>
/// واجهة مدقق القيود
/// </summary>
public interface IConstraintValidator
{
    ValidationResult Validate(TimetableContext context, IEnumerable<IConstraint>? constraints = null);
    Task<ValidationResult> ValidateAsync(TimetableContext context, IEnumerable<IConstraint>? constraints = null, CancellationToken cancellationToken = default);
    IReadOnlyList<ConstraintViolation> CheckHardConstraints(TimetableContext context);
    IReadOnlyList<ConstraintViolation> CheckSoftConstraints(TimetableContext context);
}

/// <summary>
/// واجهة كاشف التضاربات
/// </summary>
public interface IConflictDetector
{
    IReadOnlyList<Conflict> DetectConflicts(Timetable timetable);
    Task<IReadOnlyList<Conflict>> DetectConflictsAsync(Timetable timetable, CancellationToken cancellationToken = default);
    IReadOnlyList<Conflict> DetectTeacherConflicts(Timetable timetable);
    IReadOnlyList<Conflict> DetectRoomConflicts(Timetable timetable);
    IReadOnlyList<Conflict> DetectCapacityConflicts(Timetable timetable);
}

/// <summary>
/// تمثيل التضارب
/// </summary>
public sealed record Conflict
{
    public ConflictType Type { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<Lesson> Lessons { get; init; } = Array.Empty<Lesson>();
    public ConflictSeverity Severity { get; init; }
    public IReadOnlyList<string> AffectedEntities { get; init; } = Array.Empty<string>();
}

/// <summary>
/// نوع التضارب
/// </summary>
public enum ConflictType
{
    TeacherConflict,
    RoomConflict,
    CapacityConflict,
    AvailabilityConflict,
    FixedLessonConflict,
    ConsecutivePeriodsConflict,
    DistributionConflict,
}

/// <summary>
/// شدة التضارب
/// </summary>
public enum ConflictSeverity
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// واجهة المجدول (Scheduler)
/// </summary>
public interface IScheduler
{
    Task<ScheduleResult> ScheduleAsync(
        Timetable timetable,
        ScheduleOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// خيارات الجدولة
/// </summary>
public sealed class ScheduleOptions
{
    public int MaxIterations { get; set; } = 1000;
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(2);
    public bool UseHeuristics { get; set; } = true;
    public bool BacktrackOnConflict { get; set; } = true;
    public int MaxBacktrackDepth { get; set; } = 100;
}

/// <summary>
/// نتيجة الجدولة
/// </summary>
public sealed class ScheduleResult
{
    public bool Success { get; init; }
    public int ScheduledLessons { get; init; }
    public int UnscheduledLessons { get; init; }
    public IReadOnlyList<Lesson> ScheduledLessonsList { get; init; } = Array.Empty<Lesson>();
    public IReadOnlyList<Lesson> UnscheduledLessonsList { get; init; } = Array.Empty<Lesson>();
    public TimeSpan Duration { get; init; }
}
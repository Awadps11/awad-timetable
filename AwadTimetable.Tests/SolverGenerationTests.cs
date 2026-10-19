using System.Diagnostics;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;
using TimetableStatus = AwadTimetable.Core.Enums.TimetableStatus;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبارات الحلّال: هل يُطبَّق الحل فعلًا على الجدول، وهل تُحترم القيود، وهل يُلغى فورًا.
/// </summary>
public class SolverGenerationTests
{
    private static CpSatTimetableSolver CreateSolver()
        => new(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());

    /// <summary>مهلة قصيرة: الحلول الممكنة تظهر خلال مللي ثانية ثم يستهلك الحلّال بقية المهلة في إثبات الأفضلية</summary>
    private static GenerationOptions Fast() => new()
    {
        TimeLimit = TimeSpan.FromSeconds(2),
        UseParallel = true
    };

    [Fact]
    public async Task GenerateAsync_AppliesSolutionToTimetable()
    {
        var timetable = SampleTimetable.Create(classCount: 3, subjectCount: 4);

        var result = await CreateSolver().GenerateAsync(timetable, Fast());

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Timetable);
        Assert.Same(timetable, result.Timetable);

        Assert.Equal(12, result.LessonsScheduled);
        Assert.Equal(0, result.LessonsFailed);
        Assert.Equal(0, result.ConflictsCount);
        Assert.Equal(12, timetable.Lessons.Count);
        Assert.Equal(TimetableStatus.Generated, timetable.Status);
        Assert.NotNull(timetable.GeneratedAt);

        Assert.True(timetable.QualityScore.Value > 0, $"درجة الجودة {timetable.QualityScore.Value}");
        Assert.Equal(timetable.Lessons.Count, timetable.TotalLessons);
        Assert.Equal(result.ConflictsCount, timetable.TotalConflicts);
        Assert.Equal(result.QualityScore.Value, timetable.QualityScore.Value, 3);
    }

    [Fact]
    public async Task GenerateAsync_ProducesTimetableWithoutConflicts()
    {
        var timetable = SampleTimetable.Create(classCount: 4, subjectCount: 5);

        var result = await CreateSolver().GenerateAsync(timetable, Fast());
        Assert.True(result.Success, result.ErrorMessage);

        var conflicts = new ConflictDetector().DetectConflicts(timetable);

        Assert.Empty(conflicts);
        Assert.Equal(0, result.ConflictsCount);
    }

    [Fact]
    public async Task GenerateAsync_NeverSchedulesOnFridayByDefault()
    {
        var timetable = SampleTimetable.Create(classCount: 3, subjectCount: 4);

        var result = await CreateSolver().GenerateAsync(timetable, Fast());
        Assert.True(result.Success, result.ErrorMessage);

        Assert.DoesNotContain(timetable.Lessons, l => l.Day == DomainDayOfWeek.Friday);
    }

    [Fact]
    public async Task GenerateAsync_RespectsConfiguredWorkDaysAndPeriodTimes()
    {
        var timetable = SampleTimetable.Create(classCount: 3, subjectCount: 4, gradeLevel: 1);

        timetable.PeriodTimes = SampleTimetable.GeneralPeriodTimes(1, 4);
        timetable.StageConfigs.Add(new StageScheduleConfig
        {
            Stage = "ابتدائي",
            WorkDays = new List<int> { 0, 1, 2, 3 },
            LessonsPerDay = 8
        });

        var result = await CreateSolver().GenerateAsync(timetable, Fast());

        Assert.True(result.Success, result.ErrorMessage);

        foreach (var lesson in timetable.Lessons)
        {
            Assert.InRange((int)lesson.Period.Value, 1, 4);
            Assert.InRange((int)lesson.Day, 0, 3);
        }
    }

    [Fact]
    public async Task GenerateAsync_FailsWithDiagnosis_WhenTeacherIsNeverAvailable()
    {
        var timetable = SampleTimetable.Create(classCount: 2, subjectCount: 3);
        var teacherId = timetable.Lessons[0].TeacherId;

        for (var day = 0; day <= 6; day++)
        {
            for (var p = 1; p <= 12; p++)
            {
                timetable.TeacherAvailabilities.Add(new TeacherAvailability(
                    teacherId, (DomainDayOfWeek)day, PeriodNumber.Create((byte)p), isAvailable: false));
            }
        }

        var result = await CreateSolver().GenerateAsync(timetable, Fast());

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Contains("حصة", result.ErrorMessage);
        Assert.Contains("متاح", result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateAsync_KeepsFixedLessonsInPlace()
    {
        var timetable = SampleTimetable.Create(classCount: 3, subjectCount: 4);

        var fixedLesson = timetable.Lessons[0];
        fixedLesson.Reschedule(DomainDayOfWeek.Monday, PeriodNumber.Create(4));
        fixedLesson.Fix();

        var result = await CreateSolver().GenerateAsync(timetable, Fast());

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(DomainDayOfWeek.Monday, fixedLesson.Day);
        Assert.Equal(4, fixedLesson.Period.Value);
        Assert.True(fixedLesson.IsFixed);
    }

    [Fact]
    public async Task GenerateAsync_HonoursTeacherAvailability()
    {
        var timetable = SampleTimetable.Create(classCount: 4, subjectCount: 5);
        var teacherId = timetable.Lessons[0].TeacherId;

        for (var p = 1; p <= 12; p++)
        {
            timetable.TeacherAvailabilities.Add(new TeacherAvailability(
                teacherId, DomainDayOfWeek.Wednesday, PeriodNumber.Create((byte)p), isAvailable: false));
        }

        var result = await CreateSolver().GenerateAsync(timetable, Fast());

        Assert.True(result.Success, result.ErrorMessage);
        Assert.DoesNotContain(
            timetable.Lessons,
            l => l.TeacherId == teacherId && l.Day == DomainDayOfWeek.Wednesday);
    }

    [Fact]
    public async Task GenerateAsync_ReportsFailureForEmptyTimetable()
    {
        var timetable = new Timetable(SchoolId.New(), "فارغ", 2026, 2027, 1);

        var result = await CreateSolver().GenerateAsync(timetable, Fast());

        Assert.False(result.Success);
        Assert.Contains("لا توجد حصص", result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateAsync_CancellationReturnsPromptly_WithoutThrowing()
    {
        // حجم كبير بما يكفي ليمنع إثبات الأفضلية قبل الإلغاء
        var timetable = SampleTimetable.Create(classCount: 12, subjectCount: 10);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        var stopwatch = Stopwatch.StartNew();

        var result = await CreateSolver().GenerateAsync(
            timetable,
            new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(10), UseParallel = true },
            cts.Token);

        stopwatch.Stop();

        Assert.False(result.Success);
        Assert.Contains("إلغاء", result.ErrorMessage);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"استغرق الإلغاء {stopwatch.Elapsed.TotalSeconds:0.0} ثانية");
    }

    [Fact]
    public async Task ValidateAsync_DetectsRealConflicts()
    {
        var timetable = SampleTimetable.Create(classCount: 3, subjectCount: 1);

        foreach (var lesson in timetable.Lessons)
            lesson.Reschedule(DomainDayOfWeek.Sunday, PeriodNumber.Create(6));

        var result = await CreateSolver().ValidateAsync(timetable);

        Assert.False(result.IsValid);
        Assert.True(result.ConflictsCount > 0);
        Assert.NotEmpty(result.Violations);
        Assert.InRange(result.QualityScore.Value, 0.0, 100.0);
        Assert.Equal(timetable.Lessons.Count, result.TotalLessons);
    }

    [Fact]
    public async Task ValidateAsync_GeneratedTimetableHasNoHardConflicts()
    {
        var timetable = SampleTimetable.Create(classCount: 3, subjectCount: 4);

        var generation = await CreateSolver().GenerateAsync(timetable, Fast());
        Assert.True(generation.Success, generation.ErrorMessage);

        var result = await CreateSolver().ValidateAsync(timetable);

        Assert.True(result.IsValid, string.Join("\n", result.Violations.Select(v => v.Message)));
        Assert.Equal(0, result.ConflictsCount);
        Assert.InRange(result.QualityScore.Value, 0.0, 100.0);
        Assert.Equal(generation.QualityScore.Value, result.QualityScore.Value, 3);
    }

    [Fact]
    public async Task OptimizeAsync_ReportsRealBeforeAndAfterScores()
    {
        var timetable = SampleTimetable.Create(classCount: 4, subjectCount: 5);

        var generation = await CreateSolver().GenerateAsync(timetable, Fast());
        Assert.True(generation.Success, generation.ErrorMessage);

        var result = await CreateSolver().OptimizeAsync(
            timetable, new OptimizationOptions { TimeLimit = TimeSpan.FromSeconds(2) });

        Assert.InRange(result.OldScore.Value, 0.0, 100.0);
        Assert.InRange(result.NewScore.Value, 0.0, 100.0);

        if (result.Improved)
            Assert.True(result.NewScore.Value > result.OldScore.Value);
        else
            Assert.Equal(result.OldScore.Value, result.NewScore.Value, 3);

        Assert.NotNull(result.RemainingViolations);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateAsync_QualityScoreMatchesIndependentValidation()
    {
        var timetable = SampleTimetable.Create(classCount: 3, subjectCount: 5);

        var result = await CreateSolver().GenerateAsync(timetable, Fast());
        Assert.True(result.Success, result.ErrorMessage);

        var independent = await CreateSolver().ValidateAsync(timetable);

        Assert.Equal(independent.ConflictsCount, result.ConflictsCount);
        Assert.Equal(independent.QualityScore.Value, result.QualityScore.Value, 3);
    }
}

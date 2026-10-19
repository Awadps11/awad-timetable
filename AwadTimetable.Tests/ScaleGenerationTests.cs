using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using Xunit;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبار مقياس مدرسة كبيرة واقعية: 12 فصلاً على مرحلتين، 40 مادة، 40 معلماً،
/// ثم توليد فعلي بمحلّل CP-SAT والتحقق من احترام القيود الصلبة.
/// </summary>
[Trait("Category", "Stress")]
public class ScaleGenerationTests
{
    private readonly ITestOutputHelper _output;

    public ScaleGenerationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly string[] Ordinals = { "أول", "ثاني", "ثالث", "رابع", "خامس", "سادس" };
    private static readonly string[] Stages = { "متوسط", "ثانوي" };

    /// <summary>الأنماط الأسبوعية: 5 مواد × 4 حصص + 5 مواد × 3 حصص = 35 حصة/فصل.</summary>
    private static readonly int[] Pattern35 = { 4, 4, 4, 4, 4, 3, 3, 3, 3, 3 };

    /// <summary>النمط المخفّف: 10 مواد × 3 حصص = 30 حصة/فصل.</summary>
    private static readonly int[] Pattern30 = { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 };

    private static (Timetable Timetable, List<Teacher> Teachers, List<Class> Classes) BuildSchool(int[] weeklyPattern)
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "مقياس", 2026, 2027, 1);

        timetable.StageConfigs = Stages.Select(stage => new StageScheduleConfig
        {
            Stage = stage,
            WorkDays = new List<int> { 0, 1, 2, 3, 4 },
            LessonsPerDay = 7,
        }).ToList();

        timetable.PeriodTimes = SampleTimetable.GeneralPeriodTimes(1, 7);

        var subjects = new List<Subject>();
        for (var i = 0; i < 40; i++)
            subjects.Add(new Subject(school, $"مادة{i}"));

        var teachers = new List<Teacher>();
        for (var i = 0; i < 40; i++)
            teachers.Add(new Teacher(school, $"معلم{i}", "اختباري"));

        var classes = new List<Class>();
        for (var c = 0; c < 12; c++)
        {
            var stage = Stages[c / 6];
            var grade = stage == "متوسط" ? 7 : 10;
            var name = $"{Ordinals[c % 6]} {stage} {c % 6 + 1}";
            var cls = new Class(school, name, grade, 'أ');
            classes.Add(cls);
            timetable.RegisterClass((Guid)cls.Id, cls.Name, cls.GradeLevel);
        }

        for (var c = 0; c < 12; c++)
        {
            for (var s = 0; s < weeklyPattern.Length; s++)
            {
                var subjectIndex = (c * 3 + s) % 40;
                var teacherIndex = (c * 7 + s) % 40;

                timetable.Assignments.Add(new TeachingAssignment
                {
                    TeacherId = (Guid)teachers[teacherIndex].Id,
                    SubjectId = (Guid)subjects[subjectIndex].Id,
                    ClassId = (Guid)classes[c].Id,
                    WeeklyPeriods = weeklyPattern[s],
                    IsEnabled = true,
                });
            }
        }

        var sync = TeachingAssignmentLessonSync.Sync(
            timetable,
            id => teachers.FirstOrDefault(x => (Guid)x.Id == id),
            id => subjects.FirstOrDefault(x => (Guid)x.Id == id),
            id => classes.FirstOrDefault(x => (Guid)x.Id == id));

        Assert.Equal(12 * weeklyPattern.Sum(), sync.Created);

        return (timetable, teachers, classes);
    }

    private static CpSatTimetableSolver CreateSolver()
        => new(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());

    private static GenerationOptions ScaleOptions() => new()
    {
        TimeLimit = TimeSpan.FromSeconds(120),
        UseParallel = true,
        AllowPartialSolution = false,
        RandomSeed = 12345,
    };

    private static void AssertScheduleIsValid(GenerationResult result, Timetable timetable, int expectedPerClass)
    {
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(0, result.LessonsFailed);
        Assert.Equal(0, result.ConflictsCount);

        Assert.All(timetable.Lessons, l => Assert.InRange((int)l.Day, 0, 4));
        Assert.All(timetable.Lessons, l => Assert.InRange((int)l.Period.Value, 1, 7));

        Assert.DoesNotContain(timetable.Lessons, l => l.Day == DomainDayOfWeek.Friday);

        Assert.Empty(new ConflictDetector().DetectConflicts(timetable));

        foreach (var group in timetable.Lessons.GroupBy(l => l.ClassId))
        {
            Assert.Equal(expectedPerClass, group.Count());
            foreach (var dayGroup in group.GroupBy(l => l.Day))
                Assert.True(dayGroup.Count() <= 7,
                    $"الفصل {group.Key} لديه {dayGroup.Count()} حصص في اليوم {(int)dayGroup.Key}");
        }
    }

    [Fact]
    public async Task ScaleGeneration_FullWeek_35Periods()
    {
        var (timetable, _, _) = BuildSchool(Pattern35);

        var result = await CreateSolver().GenerateAsync(timetable, ScaleOptions());

        _output.WriteLine(
            $"35-periods: scheduled={result.LessonsScheduled} failed={result.LessonsFailed} " +
            $"conflicts={result.ConflictsCount} duration={result.Duration.TotalSeconds:F1}s " +
            $"solutions={result.Iterations} score={result.QualityScore.Value:F1}");

        AssertScheduleIsValid(result, timetable, 35);
    }

    [Fact]
    public async Task ScaleGeneration_Relaxed_30Periods()
    {
        var (timetable, _, _) = BuildSchool(Pattern30);

        var result = await CreateSolver().GenerateAsync(timetable, ScaleOptions());

        _output.WriteLine(
            $"30-periods: scheduled={result.LessonsScheduled} failed={result.LessonsFailed} " +
            $"conflicts={result.ConflictsCount} duration={result.Duration.TotalSeconds:F1}s " +
            $"solutions={result.Iterations} score={result.QualityScore.Value:F1}");

        AssertScheduleIsValid(result, timetable, 30);
    }

    [Fact]
    public void TeacherWeeklyLoad_DoesNotExceed35()
    {
        var (timetable, teachers, _) = BuildSchool(Pattern35);

        var loads = timetable.Lessons
            .GroupBy(l => l.TeacherId)
            .ToDictionary(g => (Guid)g.Key, g => g.Count());

        Assert.All(teachers, t => Assert.True(
            loads.GetValueOrDefault((Guid)t.Id) <= 35,
            $"المعلم {t.FullName} عليه {loads.GetValueOrDefault((Guid)t.Id)} حصة/أسبوع"));
    }
}

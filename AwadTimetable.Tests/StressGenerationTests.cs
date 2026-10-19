using System.Diagnostics;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// سيناريوهات ضغط أقسى من الواقع: عدد فصول أكبر، حصص مزدحمة 100%، معلمون محمّلون،
/// وتفريغ صلب (TeacherAvailability) لبعض المعلمين. كلها يجب أن تُولّد جدولاً كاملاً بلا تضاربات.
/// </summary>
[Trait("Category", "Stress")]
public class StressGenerationTests
{
    private static readonly string[] Stages = { "ابتدائي", "متوسط", "ثانوي" };

    private sealed record Scenario(
        string Label,
        int Teachers,
        int Classes,
        int Subjects,
        int Days,
        int PeriodsPerDay,
        int SubjectsPerClass,
        int[]? StagePeriods = null,
        int[]? BlockedPeriodsEvenTeachers = null,
        int[]? BlockedDaysEvenTeachers = null,
        int BlockStride = 2,
        int TeacherPool = 0,
        int TimeLimitSeconds = 90);

    private static async Task RunAsync(Scenario sc)
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, sc.Label, 2026, 2027, 1);

        var stagePeriods = sc.StagePeriods ?? Enumerable.Repeat(sc.PeriodsPerDay, Stages.Length).ToArray();
        var maxPeriods = Math.Max(sc.PeriodsPerDay, stagePeriods.Max());

        timetable.StageConfigs = Stages.Select((stage, i) => new StageScheduleConfig
        {
            Stage = stage,
            WorkDays = Enumerable.Range(0, sc.Days).ToList(),
            LessonsPerDay = stagePeriods[i],
        }).ToList();

        timetable.PeriodTimes = SampleTimetable.GeneralPeriodTimes(1, maxPeriods);

        var subjects = Enumerable.Range(0, sc.Subjects).Select(i => new Subject(school, $"مادة{i + 1}")).ToList();
        var teachers = Enumerable.Range(0, sc.Teachers).Select(i => new Teacher(school, $"معلم{i + 1}", "اختباري")).ToList();

        var pool = sc.TeacherPool > 0 ? Math.Min(sc.TeacherPool, teachers.Count) : teachers.Count;
        var subjectTeacher = subjects.Select((s, idx) => (Subject: s, Teacher: teachers[idx % pool])).ToList();

        var classes = new List<Class>();
        for (var c = 0; c < sc.Classes; c++)
        {
            var stageIndex = c % Stages.Length;
            var cls = new Class(school, $"{Stages[stageIndex]} {c + 1}",
                StageHelper.ParseGrade($"{Stages[stageIndex]} {c + 1}"), 'أ');
            classes.Add(cls);
            timetable.RegisterClass((Guid)cls.Id, cls.Name, cls.GradeLevel);
        }

        // تفريغ صلب لبعض المعلمين (خطوة BlockStride)
        var blockedSlots = new Dictionary<Guid, int>();

        void Block(Guid teacherId, int day, int period)
        {
            timetable.TeacherAvailabilities.Add(new TeacherAvailability(
                (TeacherId)teacherId, (DomainDayOfWeek)day, PeriodNumber.Create((byte)period),
                isAvailable: false));
            blockedSlots[teacherId] = blockedSlots.GetValueOrDefault(teacherId) + 1;
        }

        for (var ti = 0; ti < teachers.Count; ti += sc.BlockStride)
        {
            var teacherId = (Guid)teachers[ti].Id;

            if (sc.BlockedDaysEvenTeachers is { Length: > 0 })
            {
                foreach (var day in sc.BlockedDaysEvenTeachers)
                    for (var p = 1; p <= maxPeriods; p++)
                        Block(teacherId, day, p);
            }

            if (sc.BlockedPeriodsEvenTeachers is { Length: > 0 })
            {
                foreach (var day in Enumerable.Range(0, sc.Days))
                    foreach (var p in sc.BlockedPeriodsEvenTeachers)
                        Block(teacherId, day, p);
            }
        }

        var usedSubjectsPerTeacher = new Dictionary<Guid, HashSet<Guid>>();

        for (var c = 0; c < classes.Count; c++)
        {
            var stageIndex = c % Stages.Length;
            var total = sc.Days * stagePeriods[stageIndex];
            var n = Math.Min(sc.SubjectsPerClass, total);
            var basePeriods = total / n;
            var remainder = total % n;

            for (var j = 0; j < n; j++)
            {
                var subjectIndex = (c * n + j) % sc.Subjects;
                var (subject, teacher) = subjectTeacher[subjectIndex];
                var periods = basePeriods + (j < remainder ? 1 : 0);
                if (periods <= 0) continue;

                timetable.Assignments.Add(new TeachingAssignment
                {
                    TeacherId = (Guid)teacher.Id,
                    SubjectId = (Guid)subject.Id,
                    ClassId = (Guid)classes[c].Id,
                    WeeklyPeriods = periods,
                });

                if (!usedSubjectsPerTeacher.TryGetValue((Guid)teacher.Id, out var set))
                    usedSubjectsPerTeacher[(Guid)teacher.Id] = set = new HashSet<Guid>();
                set.Add((Guid)subject.Id);
            }
        }

        var sync = TeachingAssignmentLessonSync.Sync(
            timetable,
            id => teachers.FirstOrDefault(t => (Guid)t.Id == id),
            id => subjects.FirstOrDefault(s => (Guid)s.Id == id),
            id => classes.FirstOrDefault(x => (Guid)x.Id == id));

        var expected = Enumerable.Range(0, classes.Count)
            .Sum(i => sc.Days * stagePeriods[i % Stages.Length]);
        Assert.Equal(expected, sync.Created);

        var multiSubject = usedSubjectsPerTeacher.Count(kv => kv.Value.Count > 1);
        Assert.True(multiSubject > 0, "يجب أن يدرّس بعض المعلمين أكثر من مادة");

        // فحص سعة كل معلم بعد التفريغ (يمنع سيناريو مستحيلاً أصلاً)
        var totalSlots = sc.Days * maxPeriods;
        foreach (var group in timetable.Lessons.GroupBy(l => (Guid)l.TeacherId))
        {
            var capacity = totalSlots - blockedSlots.GetValueOrDefault(group.Key);
            Assert.True(group.Count() <= capacity,
                $"[{sc.Label}] المعلم {group.Key} يحتاج {group.Count()} > سعته {capacity} بعد التفريغ");
        }

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var watch = Stopwatch.StartNew();
        var result = await solver.GenerateAsync(timetable, new GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(sc.TimeLimitSeconds),
            UseParallel = true,
            AllowPartialSolution = false,
            Complexity = GenerationComplexity.Huge,
            RandomSeed = 424242,
        });
        watch.Stop();

        Assert.True(result.Success, $"[{sc.Label}] فشل: {result.ErrorMessage}");
        Assert.Equal(0, result.LessonsFailed);
        Assert.Equal(0, result.ConflictsCount);
        Assert.All(timetable.Lessons, l => Assert.InRange(l.Period.Value, 1, maxPeriods));
        Assert.All(timetable.Lessons, l => Assert.InRange((int)l.Day, 0, sc.Days - 1));
        Assert.DoesNotContain(timetable.Lessons, l => l.Day == DomainDayOfWeek.Friday);
        Assert.Empty(new ConflictDetector().DetectConflicts(timetable));

        // لا تتجاوز حصص أي فصل سعة يومه
        foreach (var g in timetable.Lessons.GroupBy(l => ((Guid)l.ClassId, (int)l.Day)))
        {
            var classIndex = classes.FindIndex(c => (Guid)c.Id == g.Key.Item1);
            Assert.True(g.Count() <= stagePeriods[classIndex % Stages.Length], $"[{sc.Label}] يوم مزدحم");
        }

        Console.WriteLine(
            $"[{sc.Label}] classes={sc.Classes} lessons={expected} teachers={sc.Teachers} " +
            $"multiSubject={multiSubject} durationMs={watch.ElapsedMilliseconds} solutions={result.Iterations}");
    }

    [Fact]
    public Task Stress_20c_700_60t_80s() =>
        RunAsync(new Scenario("20c/700/60t/80s", 60, 20, 80, 5, 7, 10));

    [Fact]
    public Task Stress_20c_700_40t_80s_tightTeachers() =>
        RunAsync(new Scenario("20c/700/40t/80s", 40, 20, 80, 5, 7, 10));

    [Fact]
    public Task Stress_18c_630_36t_60s() =>
        RunAsync(new Scenario("18c/630/36t/60s", 36, 18, 60, 5, 7, 10));

    [Fact]
    public Task Stress_12c_420_24t_blockedPeriods() =>
        RunAsync(new Scenario("12c/420/24t/blockedP7", 24, 12, 50, 5, 7, 10,
            BlockedPeriodsEvenTeachers: new[] { 7 }, BlockStride: 2));

    [Fact]
    public Task Stress_12c_420_36t_blockedTwoDays() =>
        RunAsync(new Scenario("12c/420/36t/blocked2days", 36, 12, 50, 5, 7, 10,
            BlockedDaysEvenTeachers: new[] { 0, 1 }));

    [Fact]
    public Task Stress_MixedStages_18c_600_50t_70s() =>
        RunAsync(new Scenario("mixed/18c/600/50t/70s", 50, 18, 70, 5, 7, 10,
            StagePeriods: new[] { 6, 7, 7 }));

    [Fact]
    public Task Stress_25c_875_80t_120s() =>
        RunAsync(new Scenario("25c/875/80t/120s", 80, 25, 120, 5, 7, 10, TimeLimitSeconds: 120));
}

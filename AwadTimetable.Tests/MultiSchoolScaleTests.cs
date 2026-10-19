using System.Diagnostics;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبار مقياس متعدد: مدارس بسعات مختلفة (لا تقل عن 36 معلماً و12 فصلاً و50 مادة)،
/// بعض المعلمين يدرّسون أكثر من مادة. كل حالة تبني المدرسة وتولّد جدولها كاملاً وتتحقق من الصحة.
/// </summary>
[Trait("Category", "Stress")]
public class MultiSchoolScaleTests
{
    private static readonly string[] Stages = { "ابتدائي", "متوسط", "ثانوي" };

    /// <summary>يبني مدرسة بإعدادات مرنة ويعيد عدد الحصص المطلوبة وعدد المعلمين متعددي المواد.</summary>
    private static (Timetable Timetable, int Lessons, int MultiSubjectTeachers) Build(
        int teacherCount,
        int classCount,
        int subjectCount,
        int days,
        int periodsPerDay,
        int subjectsPerClass,
        string label)
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, label, 2026, 2027, 1);

        var stageList = Stages.Take(3).ToList();
        timetable.StageConfigs = stageList.Select(stage => new StageScheduleConfig
        {
            Stage = stage,
            WorkDays = Enumerable.Range(0, days).ToList(),
            LessonsPerDay = periodsPerDay,
        }).ToList();

        timetable.PeriodTimes = SampleTimetable.GeneralPeriodTimes(1, periodsPerDay);

        // المواد والمعلمون
        var subjects = Enumerable.Range(0, subjectCount).Select(i => new Subject(school, $"مادة{i + 1}")).ToList();
        var teachers = Enumerable.Range(0, teacherCount).Select(i => new Teacher(school, $"معلم{i + 1}", "اختباري")).ToList();

        // كل مادة لها معلم واحد: subjectTeacher[s] = s % teachers  ⇒ عند subjects>teachers يدرّس المعلم عدة مواد
        var subjectTeacher = subjects.Select((s, idx) => (Subject: s, Teacher: teachers[idx % teacherCount])).ToList();

        // الفصول موزّعة على المراحل
        var classes = new List<Class>();
        for (var c = 0; c < classCount; c++)
        {
            var stage = Stages[c % Stages.Length];
            var cls = new Class(school, $"{stage} {c + 1}", StageHelper.ParseGrade($"{stage} {c + 1}"), 'أ');
            classes.Add(cls);
            timetable.RegisterClass((Guid)cls.Id, cls.Name, cls.GradeLevel);
        }

        var totalPerClass = days * periodsPerDay;
        var perSubjectBase = totalPerClass / subjectsPerClass;
        var remainder = totalPerClass % subjectsPerClass;

        var usedSubjectsPerTeacher = new Dictionary<Guid, HashSet<Guid>>();

        for (var c = 0; c < classes.Count; c++)
        {
            for (var j = 0; j < subjectsPerClass; j++)
            {
                var subjectIndex = (c * subjectsPerClass + j) % subjectCount;
                var (subject, teacher) = subjectTeacher[subjectIndex];
                var periods = perSubjectBase + (j < remainder ? 1 : 0);

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

        var multiSubject = usedSubjectsPerTeacher.Count(kv => kv.Value.Count > 1);

        return (timetable, sync.Created, multiSubject);
    }

    private static async Task RunAsync(int teachers, int classes, int subjects, int days, int periodsPerDay, int subjectsPerClass, string label)
    {
        var (timetable, lessons, multiSubjectTeachers) = Build(teachers, classes, subjects, days, periodsPerDay, subjectsPerClass, label);

        Assert.Equal(classes * days * periodsPerDay, lessons);
        Assert.True(multiSubjectTeachers > 0, "يجب أن يدرّس بعض المعلمين أكثر من مادة في هذا السيناريو");

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var watch = Stopwatch.StartNew();

        var result = await solver.GenerateAsync(timetable, new GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(60),
            UseParallel = true,
            AllowPartialSolution = false,
            RandomSeed = 20261018,
        });

        watch.Stop();

        Assert.True(result.Success, $"[{label}] فشل التوليد: {result.ErrorMessage}");
        Assert.Equal(0, result.LessonsFailed);
        Assert.Equal(0, result.ConflictsCount);

        // كل فصل بعدد الحصص الكامل، وكل يوم لا يتجاوز حصص اليوم
        var perClass = timetable.Lessons.GroupBy(l => l.ClassId).ToList();
        Assert.Equal(classes, perClass.Count);
        Assert.All(perClass, g => Assert.Equal(days * periodsPerDay, g.Count()));

        var classDay = timetable.Lessons
            .GroupBy(l => ((Guid)l.ClassId, (int)l.Day))
            .ToList();
        Assert.All(classDay, g => Assert.True(g.Count() <= periodsPerDay));

        // لا حصص غير مُسندة ولا يوم جمعة
        Assert.All(timetable.Lessons, l => Assert.InRange(l.Period.Value, 1, periodsPerDay));
        Assert.All(timetable.Lessons, l => Assert.InRange((int)l.Day, 0, days - 1));
        Assert.DoesNotContain(timetable.Lessons, l => l.Day == DomainDayOfWeek.Friday);

        // لا تضارب معلم/فصل مستقل
        var conflicts = new ConflictDetector().DetectConflicts(timetable);
        Assert.Empty(conflicts);

        // طباعة ملخّص الحالة (يظهر في مخرجات الاختبار)
        Console.WriteLine(
            $"[{label}] teachers={teachers} classes={classes} subjects={subjects} grid={days}x{periodsPerDay} " +
            $"lessons={lessons} multiSubjectTeachers={multiSubjectTeachers} durationMs={watch.ElapsedMilliseconds} solutions={result.Iterations}");
    }

    [Fact]
    public Task School_36t_12c_50s_5x7() => RunAsync(36, 12, 50, 5, 7, 10, "36t/12c/50s/5x7");

    [Fact]
    public Task School_40t_12c_50s_5x7() => RunAsync(40, 12, 50, 5, 7, 10, "40t/12c/50s/5x7");

    [Fact]
    public Task School_45t_12c_60s_5x7() => RunAsync(45, 12, 60, 5, 7, 10, "45t/12c/60s/5x7");

    [Fact]
    public Task School_50t_12c_80s_5x7_12subjects() => RunAsync(50, 12, 80, 5, 7, 12, "50t/12c/80s/5x7/12subj");

    [Fact]
    public Task School_36t_12c_80s_5x7() => RunAsync(36, 12, 80, 5, 7, 10, "36t/12c/80s/5x7");

    [Fact]
    public Task School_60t_16c_70s_5x7() => RunAsync(60, 16, 70, 5, 7, 10, "60t/16c/70s/5x7");

    [Fact]
    public Task School_36t_12c_50s_6x6() => RunAsync(36, 12, 50, 6, 6, 12, "36t/12c/50s/6x6");

    [Fact]
    public Task School_48t_12c_100s_5x7() => RunAsync(48, 12, 100, 5, 7, 10, "48t/12c/100s/5x7");

    [Fact]
    public Task School_36t_12c_55s_5x7_14subjects() => RunAsync(36, 12, 55, 5, 7, 14, "36t/12c/55s/5x7/14subj");

    [Fact]
    public Task School_72t_12c_120s_5x7() => RunAsync(72, 12, 120, 5, 7, 10, "72t/12c/120s/5x7");
}

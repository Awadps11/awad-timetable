using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبارات مزامنة الإسنادات مع الحصص، وسيناريو توليد واقعي كامل الأسبوع.
/// </summary>
public class TeachingAssignmentSyncTests
{
    private static (Timetable Timetable, Dictionary<Guid, Teacher> Teachers, Dictionary<Guid, Subject> Subjects, Dictionary<Guid, Class> Classes) BuildSchool()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "مزامنة", 2026, 2027, 1);

        var teachers = new Dictionary<Guid, Teacher>();
        var subjects = new Dictionary<Guid, Subject>();
        var classes = new Dictionary<Guid, Class>();

        for (var s = 0; s < 3; s++)
        {
            var subject = new Subject(school, $"مادة{s + 1}");
            subjects[(Guid)subject.Id] = subject;
        }

        for (var c = 0; c < 3; c++)
        {
            var cls = new Class(school, $"فصل{c + 1}", 1, 'أ');
            classes[(Guid)cls.Id] = cls;
        }

        for (var t = 0; t < 3; t++)
        {
            var teacher = new Teacher(school, $"معلم{t + 1}", "اختباري");
            teachers[(Guid)teacher.Id] = teacher;
        }

        return (timetable, teachers, subjects, classes);
    }

    [Fact]
    public void Sync_CreatesUnscheduledLessonsFromAssignments()
    {
        var (timetable, teachers, subjects, classes) = BuildSchool();

        var teacherId = teachers.Keys.First();
        var subjectId = subjects.Keys.First();
        var classId = classes.Keys.First();

        timetable.Assignments.Add(new TeachingAssignment
        {
            TeacherId = teacherId,
            SubjectId = subjectId,
            ClassId = classId,
            WeeklyPeriods = 4,
            IsEnabled = true,
        });

        var result = TeachingAssignmentLessonSync.Sync(timetable,
            id => teachers.GetValueOrDefault(id),
            id => subjects.GetValueOrDefault(id),
            id => classes.GetValueOrDefault(id));

        Assert.Equal(4, result.Created);
        Assert.Equal(0, result.Removed);
        Assert.Equal(4, timetable.Lessons.Count);
        Assert.All(timetable.Lessons, l => Assert.Equal(0, l.Period.Value));
        Assert.All(timetable.Lessons, l => Assert.NotNull(l.Teacher));
        Assert.All(timetable.Lessons, l => Assert.NotNull(l.Subject));
        Assert.All(timetable.Lessons, l => Assert.NotNull(l.Class));
    }

    [Fact]
    public void Sync_IsIdempotent_AndRemovesExcess()
    {
        var (timetable, teachers, subjects, classes) = BuildSchool();

        var assignment = new TeachingAssignment
        {
            TeacherId = teachers.Keys.First(),
            SubjectId = subjects.Keys.First(),
            ClassId = classes.Keys.First(),
            WeeklyPeriods = 5,
        };
        timetable.Assignments.Add(assignment);

        Func<Guid, Teacher?> t = id => teachers.GetValueOrDefault(id);
        Func<Guid, Subject?> s = id => subjects.GetValueOrDefault(id);
        Func<Guid, Class?> c = id => classes.GetValueOrDefault(id);

        TeachingAssignmentLessonSync.Sync(timetable, t, s, c);
        var second = TeachingAssignmentLessonSync.Sync(timetable, t, s, c);

        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Removed);
        Assert.Equal(5, timetable.Lessons.Count);

        assignment.WeeklyPeriods = 3;
        var third = TeachingAssignmentLessonSync.Sync(timetable, t, s, c);

        Assert.Equal(2, third.Removed);
        Assert.Equal(3, timetable.Lessons.Count);
    }

    [Fact]
    public void Sync_DisabledAssignment_RemovesOnlyUnscheduled()
    {
        var (timetable, teachers, subjects, classes) = BuildSchool();

        var assignment = new TeachingAssignment
        {
            TeacherId = teachers.Keys.First(),
            SubjectId = subjects.Keys.First(),
            ClassId = classes.Keys.First(),
            WeeklyPeriods = 2,
        };
        timetable.Assignments.Add(assignment);

        Func<Guid, Teacher?> t = id => teachers.GetValueOrDefault(id);
        Func<Guid, Subject?> s = id => subjects.GetValueOrDefault(id);
        Func<Guid, Class?> c = id => classes.GetValueOrDefault(id);

        TeachingAssignmentLessonSync.Sync(timetable, t, s, c);
        timetable.Lessons[0].Reschedule(DomainDayOfWeek.Sunday, PeriodNumber.Create(1));

        assignment.IsEnabled = false;
        var result = TeachingAssignmentLessonSync.Sync(timetable, t, s, c);

        Assert.Equal(1, result.Removed);
        Assert.Single(timetable.Lessons);
        Assert.Equal(1, timetable.Lessons[0].Period.Value);
    }

    [Fact]
    public async Task Generate_ReportsCapacityProblem_WhenClassOverloaded()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "سعة", 2026, 2027, 1);

        timetable.StageConfigs = new List<StageScheduleConfig>
        {
            new() { Stage = "متوسط", WorkDays = new List<int> { 0, 1, 2, 3, 4 }, LessonsPerDay = 5 }
        };
        timetable.PeriodTimes = SampleTimetable.GeneralPeriodTimes(1, 5);

        var subject = new Subject(school, "مادة");
        var teacher = new Teacher(school, "معلم", "اختباري");
        var cls = new Class(school, "أول متوسط 1", 7, 'أ');
        timetable.RegisterClass((Guid)cls.Id, cls.Name, cls.GradeLevel);

        // 30 حصة > 5 أيام × 5 فترات = 25
        timetable.Assignments.Add(new TeachingAssignment
        {
            TeacherId = (Guid)teacher.Id,
            SubjectId = (Guid)subject.Id,
            ClassId = (Guid)cls.Id,
            WeeklyPeriods = 30,
        });

        TeachingAssignmentLessonSync.Sync(
            timetable,
            id => teacher,
            id => subject,
            id => cls);

        var solver = new CpSatTimetableSolver(
            NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());

        var result = await solver.GenerateAsync(timetable, new GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(5),
            AllowPartialSolution = false,
        });

        Assert.False(result.Success);
        Assert.Contains("السعة", result.ErrorMessage);
    }

    [Fact]
    public async Task Generate_RealisticFullWeek_Succeeds()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "أسبوع كامل", 2026, 2027, 1);

        const int days = 6;
        const int periodsPerDay = 7;
        const int lessonsPerClass = 30;

        var stages = new[] { "ابتدائي", "متوسط", "ثانوي" };
        timetable.StageConfigs = stages.Select(stage => new StageScheduleConfig
        {
            Stage = stage,
            WorkDays = Enumerable.Range(0, days).ToList(),
            LessonsPerDay = periodsPerDay,
        }).ToList();

        timetable.PeriodTimes = Enumerable.Range(1, periodsPerDay).Select(p => new PeriodTime
        {
            Period = (byte)p,
            StartMinutes = 7 * 60 + (p - 1) * 50,
            EndMinutes = 7 * 60 + (p - 1) * 50 + 45,
            Day = -1,
            Stage = null,
        }).ToList();

        var subjects = new List<Subject>();
        for (var i = 0; i < 6; i++)
            subjects.Add(new Subject(school, $"مادة{i + 1}"));

        var classes = new List<Class>();
        for (var c = 0; c < 12; c++)
        {
            var stage = stages[c / 4];
            var cls = new Class(school, $"{stage} {c % 4 + 1}", 1, 'أ');
            classes.Add(cls);
            timetable.RegisterClass((Guid)cls.Id, cls.Name, cls.GradeLevel);
        }

        // معلمان لكل مادة؛ كل معلم يدرّس 6 فصول (6 × 5 = 30 حصة/أسبوع = الحد الأقصى)
        var teachers = new List<Teacher>();
        for (var i = 0; i < 12; i++)
            teachers.Add(new Teacher(school, $"معلم{i + 1}", "اختباري"));

        var lessonsPerSubject = lessonsPerClass / subjects.Count;
        for (var c = 0; c < classes.Count; c++)
        {
            for (var s = 0; s < subjects.Count; s++)
            {
                var teacherIndex = s * 2 + (c / 6);
                timetable.Assignments.Add(new TeachingAssignment
                {
                    TeacherId = (Guid)teachers[teacherIndex].Id,
                    SubjectId = (Guid)subjects[s].Id,
                    ClassId = (Guid)classes[c].Id,
                    WeeklyPeriods = lessonsPerSubject,
                });
            }
        }

        var sync = TeachingAssignmentLessonSync.Sync(
            timetable,
            id => teachers.FirstOrDefault(x => (Guid)x.Id == id),
            id => subjects.FirstOrDefault(x => (Guid)x.Id == id),
            id => classes.FirstOrDefault(x => (Guid)x.Id == id));

        Assert.Equal(12 * lessonsPerClass, sync.Created);

        var solver = new CpSatTimetableSolver(
            NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());

        var result = await solver.GenerateAsync(timetable, new GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(20),
            UseParallel = true,
            AllowPartialSolution = false,
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(12 * lessonsPerClass, result.LessonsScheduled);
        Assert.Equal(0, result.LessonsFailed);
        Assert.Equal(0, result.ConflictsCount);
    }
}

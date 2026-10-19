using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class TeacherAvailabilityPriorityTests
{
    private static Timetable WithOneLesson(DomainDayOfWeek day, int period)
    {
        var tt = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = new List<int> { 0, 1, 2 }, LessonsPerDay = 7 });

        var school = tt.SchoolId;
        var teacher = new Teacher(school, "م", "علم");
        var cls = new Class(school, "فصل", 7, 'أ');
        var subject = new Subject(school, "مادة");
        tt.ClassGradeLevels[cls.Id] = 7;

        var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id, (ClassId)cls.Id, day, PeriodNumber.Create((byte)period));
        lesson.AttachReferences(subject, teacher, cls);
        tt.AddLesson(lesson);

        tt.TeacherAvailabilities.Add(new TeacherAvailability((TeacherId)teacher.Id, day, PeriodNumber.Create((byte)period), isAvailable: false, priority: AvailabilityPriority.High));
        return tt;
    }

    [Fact]
    public void HighPriorityBlock_IsHardViolation()
    {
        var tt = WithOneLesson(DomainDayOfWeek.Sunday, 2);

        var violation = new TeacherAvailabilityConstraint().Evaluate(TimetableContextFactory.Build(tt));

        Assert.NotNull(violation);
        Assert.Single(violation!.AffectedLessons);
    }

    [Fact]
    public void HighPriorityBlock_IsNotReportedByLowPriorityConstraint()
    {
        var tt = WithOneLesson(DomainDayOfWeek.Sunday, 2);

        var violation = new TeacherLowPriorityBlockConstraint().Evaluate(TimetableContextFactory.Build(tt));

        Assert.Null(violation);
    }

    [Fact]
    public void LowPriorityBlock_IsNotAHardViolation_ButIsSoft()
    {
        var tt = WithOneLesson(DomainDayOfWeek.Sunday, 2);
        var avail = tt.TeacherAvailabilities.First();
        avail.SetPriority(AvailabilityPriority.Low);

        var context = TimetableContextFactory.Build(tt);

        Assert.Null(new TeacherAvailabilityConstraint().Evaluate(context));
        Assert.NotNull(new TeacherLowPriorityBlockConstraint().Evaluate(context));
    }

    [Fact]
    public async Task Solver_NeverUses_HighPriorityBlockedSlot()
    {
        // يوم واحد، فترة واحدة فقط؛ الحصة في السبت والفترة 1 مغلقة إغلاقاً ملزماً → غير قابلة للجدولة
        var tt = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = new List<int> { 0 }, LessonsPerDay = 1 });
        tt.PeriodTimes.Add(new PeriodTime { Period = 1, Day = -1, Stage = null, StartMinutes = 420, EndMinutes = 465 });

        var school = tt.SchoolId;
        var teacher = new Teacher(school, "م", "علم");
        var cls = new Class(school, "فصل", 7, 'أ');
        var subject = new Subject(school, "مادة");
        tt.ClassGradeLevels[cls.Id] = 7;

        var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id, (ClassId)cls.Id,
            DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
        lesson.AttachReferences(subject, teacher, cls);
        tt.AddLesson(lesson);

        tt.TeacherAvailabilities.Add(new TeacherAvailability((TeacherId)teacher.Id, DomainDayOfWeek.Saturday,
            PeriodNumber.Create(1), isAvailable: false, priority: AvailabilityPriority.High));

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(3), UseParallel = false, AllowPartialSolution = false });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Solver_RespectsLowPriorityBlock_WhenAlternativesExist()
    {
        var tt = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = new List<int> { 0 }, LessonsPerDay = 4 });
        for (byte p = 1; p <= 3; p++)
            tt.PeriodTimes.Add(new PeriodTime { Period = p, Day = 0, Stage = null, StartMinutes = 420 + (p - 1) * 50, EndMinutes = 465 + (p - 1) * 50 });

        var school = tt.SchoolId;
        var teacher = new Teacher(school, "م", "علم");
        var cls = new Class(school, "فصل", 7, 'أ');
        var subject = new Subject(school, "مادة");
        tt.ClassGradeLevels[cls.Id] = 7;

        var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id, (ClassId)cls.Id,
            DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
        lesson.AttachReferences(subject, teacher, cls);
        tt.AddLesson(lesson);

        // أغلق الفترة 1 بأولوية منخفضة (يمكن التجاوز)
        tt.TeacherAvailabilities.Add(new TeacherAvailability((TeacherId)teacher.Id, DomainDayOfWeek.Saturday,
            PeriodNumber.Create(1), isAvailable: false, priority: AvailabilityPriority.Low));

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(3), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        // يوجد بديل، فيجب أن يتجنّب الفترة المغلقة
        Assert.NotEqual(1, tt.Lessons.Single().Period.Value);
    }
}

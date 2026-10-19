using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class ScheduleAdvisorTests
{
    private sealed record Ctx(SchoolId School, Teacher Teacher, Class Class);

    private static Timetable BaseTimetable(out Ctx ctx)
    {
        var timetable = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        timetable.StageConfigs.Add(new StageScheduleConfig
        {
            Stage = "ابتدائي",
            WorkDays = new List<int> { 0, 1, 2 },
            LessonsPerDay = 7,
        });

        var school = timetable.SchoolId;
        var teacher = new Teacher(school, "معلم", "أ");
        var cls = new Class(school, "فصل", 1, 'أ');
        timetable.ClassGradeLevels[cls.Id] = 1;
        ctx = new Ctx(school, teacher, cls);
        return timetable;
    }

    private static Lesson Add(Timetable timetable, Ctx ctx, Subject subject, DomainDayOfWeek day, int period,
        Room? room = null)
    {
        var lesson = new Lesson((TimetableId)timetable.Id, (SubjectId)subject.Id, (TeacherId)ctx.Teacher.Id, (ClassId)ctx.Class.Id,
            day, PeriodNumber.Create((byte)period));
        lesson.AttachReferences(subject, ctx.Teacher, ctx.Class, room);
        if (room is not null) lesson.AssignRoom((RoomId)room.Id);
        timetable.AddLesson(lesson);
        return lesson;
    }

    [Fact]
    public void Analyze_ReportsMissingRoom()
    {
        var timetable = BaseTimetable(out var ctx);
        var lesson = Add(timetable, ctx, new Subject(ctx.School, "مادة"), DomainDayOfWeek.Sunday, 1);
        lesson.AssignRoom(RoomId.New()); // قاعة غير موجودة (Room = null)

        var findings = ScheduleAdvisor.Analyze(TimetableContextFactory.Build(timetable));

        Assert.Contains(findings, f => f.Code == "room-missing");
    }

    [Fact]
    public void Analyze_ReportsRoomTooSmall()
    {
        var timetable = BaseTimetable(out var ctx);
        var room = new Room(ctx.School, "صغيرة", capacity: 5);
        ctx.Class.UpdateBasicInfo("فصل", "ف", "1/1", 40);
        Add(timetable, ctx, new Subject(ctx.School, "مادة"), DomainDayOfWeek.Sunday, 1, room);

        var findings = ScheduleAdvisor.Analyze(TimetableContextFactory.Build(timetable));

        Assert.Contains(findings, f => f.Code == "room-too-small");
    }

    [Fact]
    public void Analyze_ReportsSubjectMoreThanDays()
    {
        var timetable = BaseTimetable(out var ctx);
        var subject = new Subject(ctx.School, "رياضيات");

        // 3 أيام عمل، لكن 5 حصص لمادة واحدة
        for (var p = 1; p <= 5; p++)
            Add(timetable, ctx, subject, DomainDayOfWeek.Sunday, p);

        var findings = ScheduleAdvisor.Analyze(TimetableContextFactory.Build(timetable));

        Assert.Contains(findings, f => f.Code == "subject-more-than-days");
    }

    [Fact]
    public void Analyze_ReportsTeacherDayOverload()
    {
        var timetable = BaseTimetable(out var ctx);
        ctx.Teacher.UpdateEmploymentInfo(null, null, null, true, maxPeriodsPerDay: 2, maxPeriodsPerWeek: 30);

        for (var p = 1; p <= 4; p++)
            Add(timetable, ctx, new Subject(ctx.School, "مادة"), DomainDayOfWeek.Sunday, p);

        var findings = ScheduleAdvisor.Analyze(TimetableContextFactory.Build(timetable));

        Assert.Contains(findings, f => f.Code == "teacher-day-overload");
    }

    [Fact]
    public void Analyze_ReportsTeacherGaps()
    {
        var timetable = BaseTimetable(out var ctx);
        foreach (var p in new[] { 1, 6, 7 })
            Add(timetable, ctx, new Subject(ctx.School, "مادة"), DomainDayOfWeek.Sunday, p);

        var findings = ScheduleAdvisor.Analyze(TimetableContextFactory.Build(timetable));

        Assert.Contains(findings, f => f.Code == "teacher-gaps");
    }

    [Fact]
    public void Analyze_NoErrors_ForReasonableTimetable()
    {
        var timetable = BaseTimetable(out var ctx);
        var room = new Room(ctx.School, "قاعة 1", capacity: 40);
        ctx.Class.UpdateBasicInfo("فصل", "ف", "1/1", 30);

        for (var p = 1; p <= 3; p++)
            Add(timetable, ctx, new Subject(ctx.School, "مادة"), DomainDayOfWeek.Sunday, p, room);

        var findings = ScheduleAdvisor.Analyze(TimetableContextFactory.Build(timetable));

        Assert.DoesNotContain(findings, f => f.Severity == AdvisorSeverity.Error);
    }

    [Fact]
    public void Analyze_OrdersErrorsFirst()
    {
        var timetable = BaseTimetable(out var ctx);
        var lesson = Add(timetable, ctx, new Subject(ctx.School, "مادة"), DomainDayOfWeek.Sunday, 1);
        lesson.AssignRoom(RoomId.New()); // room-missing = Error
        for (var p = 1; p <= 4; p++)
            Add(timetable, ctx, new Subject(ctx.School, "أخرى"), DomainDayOfWeek.Sunday, p); // teacher overload = Warning

        var findings = ScheduleAdvisor.Analyze(TimetableContextFactory.Build(timetable));

        if (findings.Count > 1)
            Assert.True(findings[0].Severity >= findings[^1].Severity);
    }
}

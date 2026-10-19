using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبارات القيود: هوية القيود، الحصص بلا قاعة، والحد اليومي المُهيَّأ.
/// </summary>
public class ConstraintTests
{
    [Fact]
    public void Constraint_Identities_AreDistinct()
    {
        var all = ConstraintRegistry.CreateAll().ToList();

        Assert.Equal(19, all.Count);
        Assert.Equal(all.Count, all.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void EqualConstraintTypes_HaveDifferentIds()
    {
        var first = new NoTeacherConflictConstraint();
        var second = new NoTeacherConflictConstraint();

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Id, first.Id);
    }

    [Fact]
    public void NoRoomConflict_LessonsWithoutRoom_AreNotReported()
    {
        var timetable = SampleTimetable.Create(classCount: 2, subjectCount: 2);

        foreach (var lesson in timetable.Lessons)
            lesson.Reschedule(DomainDayOfWeek.Saturday, PeriodNumber.Create(1));

        var context = TimetableContextFactory.Build(timetable);

        // كل الحصص في نفس الوقت لكن لا أحد لديه قاعة محددة
        Assert.All(timetable.Lessons, l => Assert.True(l.RoomId is null));

        var violation = new NoRoomConflictConstraint().Evaluate(context);

        Assert.Null(violation);
    }

    [Fact]
    public void NoRoomConflict_SameRealRoomAtSameSlot_IsReported()
    {
        var timetable = SampleTimetable.Create(classCount: 2, subjectCount: 1);
        var room = RoomId.New();

        foreach (var lesson in timetable.Lessons)
        {
            lesson.Reschedule(DomainDayOfWeek.Sunday, PeriodNumber.Create(2));
            lesson.AssignRoom(room);
        }

        var context = TimetableContextFactory.Build(timetable);
        var violation = new NoRoomConflictConstraint().Evaluate(context);

        Assert.NotNull(violation);
        Assert.Equal(2, violation!.AffectedLessons.Count);
    }

    [Fact]
    public void NoTeacherConflict_DoubleBookedTeacher_IsReported()
    {
        var timetable = SampleTimetable.Create(classCount: 3, subjectCount: 1);

        foreach (var lesson in timetable.Lessons)
            lesson.Reschedule(DomainDayOfWeek.Monday, PeriodNumber.Create(3));

        var context = TimetableContextFactory.Build(timetable);
        var violation = new NoTeacherConflictConstraint().Evaluate(context);

        Assert.NotNull(violation);
        Assert.Equal(3, violation!.AffectedLessons.Count);
    }

    [Fact]
    public void MaxPeriodsPerDay_WithoutConfiguredLimit_ReportsNothing()
    {
        var timetable = SampleTimetable.Create(classCount: 1, subjectCount: 10);

        for (var p = 1; p <= 10; p++)
            timetable.Lessons[p - 1].Reschedule(DomainDayOfWeek.Sunday, PeriodNumber.Create((byte)p));

        Assert.Empty(timetable.StageConfigs);

        var context = TimetableContextFactory.Build(timetable);

        Assert.Null(new MaxPeriodsPerDayConstraint().Evaluate(context));
    }

    [Fact]
    public void MaxPeriodsPerDay_ExceedingConfiguredLimit_IsReported()
    {
        var timetable = SampleTimetable.Create(classCount: 1, subjectCount: 10);
        var stage = timetable.GetStageForClass(timetable.Lessons[0].ClassId);

        timetable.StageConfigs.Add(new StageScheduleConfig
        {
            Stage = stage!,
            WorkDays = new List<int> { 0, 1, 2, 3, 4, 5 },
            LessonsPerDay = 4
        });

        for (var p = 1; p <= 10; p++)
            timetable.Lessons[p - 1].Reschedule(DomainDayOfWeek.Sunday, PeriodNumber.Create((byte)p));

        var context = TimetableContextFactory.Build(timetable);
        var violation = new MaxPeriodsPerDayConstraint().Evaluate(context);

        Assert.NotNull(violation);
        Assert.Equal(10, violation!.AffectedLessons.Count);
    }

    [Fact]
    public void TeacherAvailability_MarkTeacherUnavailable_IsHonouredByValidator()
    {
        var timetable = SampleTimetable.Create(classCount: 2, subjectCount: 2);
        var teacherId = timetable.Lessons[0].TeacherId;

        foreach (var lesson in timetable.Lessons)
            lesson.Reschedule(DomainDayOfWeek.Tuesday, PeriodNumber.Create(5));

        for (var p = 1; p <= 12; p++)
        {
            timetable.TeacherAvailabilities.Add(new TeacherAvailability(
                teacherId, DomainDayOfWeek.Tuesday, PeriodNumber.Create((byte)p), isAvailable: false));
        }

        var context = TimetableContextFactory.Build(timetable);
        var violation = new TeacherAvailabilityConstraint().Evaluate(context);

        Assert.NotNull(violation);
    }
}

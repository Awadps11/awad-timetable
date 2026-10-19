using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class SubjectOncePerDayTests
{
    private static Lesson Make(Timetable timetable, Guid classId, Guid subjectId, DomainDayOfWeek day, int period)
    {
        var lesson = new Lesson(
            (TimetableId)timetable.Id,
            (SubjectId)subjectId,
            (TeacherId)Guid.NewGuid(),
            (ClassId)classId,
            day,
            PeriodNumber.Create((byte)period));
        timetable.AddLesson(lesson);
        return lesson;
    }

    [Fact]
    public void ReportsSameSubjectTwiceInOneDayForSameClass()
    {
        var timetable = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        var classId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        Make(timetable, classId, subjectId, DomainDayOfWeek.Monday, 1);
        Make(timetable, classId, subjectId, DomainDayOfWeek.Monday, 3);

        var violation = new SubjectOncePerDayConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.NotNull(violation);
        Assert.Equal(2, violation!.AffectedLessons.Count);
    }

    [Fact]
    public void AllowsSameSubjectOnDifferentDays()
    {
        var timetable = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        var classId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        Make(timetable, classId, subjectId, DomainDayOfWeek.Monday, 1);
        Make(timetable, classId, subjectId, DomainDayOfWeek.Tuesday, 1);

        var violation = new SubjectOncePerDayConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.Null(violation);
    }

    [Fact]
    public void AllowsDifferentSubjectsOnSameDay()
    {
        var timetable = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        var classId = Guid.NewGuid();
        Make(timetable, classId, Guid.NewGuid(), DomainDayOfWeek.Monday, 1);
        Make(timetable, classId, Guid.NewGuid(), DomainDayOfWeek.Monday, 2);

        var violation = new SubjectOncePerDayConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.Null(violation);
    }
}

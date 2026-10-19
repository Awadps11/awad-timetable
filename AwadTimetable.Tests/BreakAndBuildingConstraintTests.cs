using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class BreakAndBuildingConstraintTests
{
    private static SchoolId NewSchool() => SchoolId.New();

    private static Timetable MakeTimetable(int gradeLevel = 1)
    {
        var timetable = new Timetable(NewSchool(), "جدول", 2026, 2027, 1);
        timetable.StageConfigs.Add(new StageScheduleConfig
        {
            Stage = StageHelper.GetStage(gradeLevel),
            WorkDays = new List<int> { 1, 2, 3 },
            LessonsPerDay = 7,
            MinLessonsPerDay = 3
        });
        return timetable;
    }

    private static Lesson Add(Timetable timetable, Guid classId, Guid subjectId, Guid teacherId,
        DomainDayOfWeek day, int period, Room? room = null)
    {
        timetable.ClassGradeLevels.TryAdd(classId, 1);
        var lesson = new Lesson((TimetableId)timetable.Id, (SubjectId)subjectId, (TeacherId)teacherId,
            (ClassId)classId, day, PeriodNumber.Create((byte)period));
        if (room is not null)
        {
            lesson.AssignRoom((RoomId)room.Id);
            lesson.AttachReferences(null, null, null, room);
        }
        timetable.AddLesson(lesson);
        return lesson;
    }

    [Fact]
    public void NoLessonDuringBreak_ReportsLessonInsideBreak()
    {
        var timetable = MakeTimetable();
        timetable.PeriodTimes.Add(new PeriodTime { Period = 3, IsBreak = true, Day = -1, Stage = null });
        var classId = Guid.NewGuid();
        Add(timetable, classId, Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Monday, 3);
        Add(timetable, classId, Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Monday, 4);

        var violation = new NoLessonDuringBreakConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.NotNull(violation);
        Assert.Single(violation!.AffectedLessons);
    }

    [Fact]
    public void NoLessonDuringBreak_SilentWhenNoBreaks()
    {
        var timetable = MakeTimetable();
        var classId = Guid.NewGuid();
        Add(timetable, classId, Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Monday, 3);

        var violation = new NoLessonDuringBreakConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.Null(violation);
    }

    [Fact]
    public void MinPeriodsPerDay_ReportsDayBelowMinimum()
    {
        var timetable = MakeTimetable();
        var classId = Guid.NewGuid();
        Add(timetable, classId, Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Monday, 1);

        var violation = new MinPeriodsPerDayConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.NotNull(violation);
    }

    [Fact]
    public void MinPeriodsPerDay_SilentWhenMinimumMet()
    {
        var timetable = MakeTimetable();
        var classId = Guid.NewGuid();
        Add(timetable, classId, Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Monday, 1);
        Add(timetable, classId, Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Monday, 2);
        Add(timetable, classId, Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Monday, 3);

        var violation = new MinPeriodsPerDayConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.Null(violation);
    }

    [Fact]
    public void DoublePeriodAcrossBreak_ReportsPairSeparatedByBreak()
    {
        var timetable = MakeTimetable();
        timetable.PeriodTimes.Add(new PeriodTime { Period = 3, IsBreak = true, Day = -1, Stage = null });
        var classId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        Add(timetable, classId, subjectId, teacherId, DomainDayOfWeek.Monday, 2);
        Add(timetable, classId, subjectId, teacherId, DomainDayOfWeek.Monday, 4);

        var violation = new DoublePeriodAcrossBreakConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.NotNull(violation);
        Assert.Equal(2, violation!.AffectedLessons.Count);
    }

    [Fact]
    public void DoublePeriodAcrossBreak_SilentWhenAdjacent()
    {
        var timetable = MakeTimetable();
        timetable.PeriodTimes.Add(new PeriodTime { Period = 3, IsBreak = true, Day = -1, Stage = null });
        var classId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        Add(timetable, classId, subjectId, teacherId, DomainDayOfWeek.Monday, 1);
        Add(timetable, classId, subjectId, teacherId, DomainDayOfWeek.Monday, 2);

        var violation = new DoublePeriodAcrossBreakConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.Null(violation);
    }

    [Fact]
    public void TeacherBuildingChange_ReportsConsecutiveDifferentBuildings()
    {
        var timetable = MakeTimetable();
        var school = NewSchool();
        var roomA = new Room(school, "قاعة أ");
        roomA.UpdateLocation(0, "المبنى أ");
        var roomB = new Room(school, "قاعة ب");
        roomB.UpdateLocation(0, "المبنى ب");

        var classId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        Add(timetable, classId, Guid.NewGuid(), teacherId, DomainDayOfWeek.Monday, 1, roomA);
        Add(timetable, classId, Guid.NewGuid(), teacherId, DomainDayOfWeek.Monday, 2, roomB);

        var violation = new TeacherBuildingChangeConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.NotNull(violation);
    }

    [Fact]
    public void TeacherBuildingChange_SilentWhenSameBuilding()
    {
        var timetable = MakeTimetable();
        var school = NewSchool();
        var roomA = new Room(school, "قاعة أ");
        roomA.UpdateLocation(0, "المبنى أ");
        var roomB = new Room(school, "قاعة ب");
        roomB.UpdateLocation(0, "المبنى أ");

        var classId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        Add(timetable, classId, Guid.NewGuid(), teacherId, DomainDayOfWeek.Monday, 1, roomA);
        Add(timetable, classId, Guid.NewGuid(), teacherId, DomainDayOfWeek.Monday, 2, roomB);

        var violation = new TeacherBuildingChangeConstraint().Evaluate(TimetableContextFactory.Build(timetable));

        Assert.Null(violation);
    }

    [Fact]
    public async Task Solver_NeverPlacesLessonInBreak()
    {
        var timetable = MakeTimetable();
        // الفترة 1 استراحة في كل الأيام؛ الفترات 2..4 عادية
        timetable.PeriodTimes.Add(new PeriodTime { Period = 1, IsBreak = true, Day = -1, Stage = null });
        for (var p = 2; p <= 4; p++)
            timetable.PeriodTimes.Add(new PeriodTime { Period = (byte)p, Day = -1, Stage = null });

        var classId = Guid.NewGuid();
        for (var p = 1; p <= 2; p++)
            Add(timetable, classId, Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Sunday, p);

        var solver = new CpSatTimetableSolver(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CpSatTimetableSolver>.Instance,
            new ConstraintValidator());
        await solver.GenerateAsync(timetable, new AwadTimetable.Core.Services.GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(5),
            UseParallel = false
        });

        Assert.DoesNotContain(timetable.Lessons,
            l => timetable.IsBreakPeriod(l.Period.Value, (int)l.Day, timetable.GetStageForClass(l.ClassId)));
    }
}

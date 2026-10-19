using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class SupervisionGeneratorTests
{
    private static Timetable WithBreak(int dayIndex, byte period)
    {
        var timetable = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        timetable.PeriodTimes.Add(new PeriodTime { Period = period, IsBreak = true, Day = dayIndex, Stage = null });
        return timetable;
    }

    private static Teacher Teacher(SchoolId school, string first, string last)
        => new(school, first, last);

    [Fact]
    public void Generate_ReturnsEmpty_OnFriday()
    {
        var school = SchoolId.New();
        var timetable = WithBreak(-1, 3);
        var rooms = new List<Room> { new(school, "قاعة 1") };
        var teachers = new List<Teacher> { Teacher(school, "أ", "ب") };

        // 2026-10-30 يوم جمعة
        var result = SupervisionGenerator.Generate(timetable, teachers, rooms, Array.Empty<Guid>(), new DateOnly(2026, 10, 30));

        Assert.Empty(result);
    }

    [Fact]
    public void Generate_AssignsSupervisorPerRoomDuringBreak()
    {
        var school = SchoolId.New();
        var timetable = WithBreak(-1, 3);
        var rooms = new List<Room> { new(school, "قاعة 1"), new(school, "قاعة 2") };
        var teachers = new List<Teacher> { Teacher(school, "أ", "ب"), Teacher(school, "ج", "د") };

        // 2026-10-25 يوم أحد (dayIndex = 1)
        var result = SupervisionGenerator.Generate(timetable, teachers, rooms, Array.Empty<Guid>(), new DateOnly(2026, 10, 25));

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("معيّن", r.Status));
        // كل معلم يُكلَّف مرة واحدة في الفترة نفسها
        Assert.Equal(2, result.Select(r => r.SupervisorTeacherId).Distinct().Count());
    }

    [Fact]
    public void Generate_MarksWithoutSupervisor_WhenTeachersBusy()
    {
        var school = SchoolId.New();
        var timetable = WithBreak(-1, 3);
        var rooms = new List<Room> { new(school, "قاعة 1"), new(school, "قاعة 2") };
        var teachers = new List<Teacher> { Teacher(school, "أ", "ب") };

        var result = SupervisionGenerator.Generate(timetable, teachers, rooms, Array.Empty<Guid>(), new DateOnly(2026, 10, 25));

        Assert.Equal(2, result.Count);
        Assert.Single(result, r => r.Status == "معيّن");
        Assert.Single(result, r => r.Status == "بدون مشرف");
    }

    [Fact]
    public void Generate_SkipsAbsentTeacher()
    {
        var school = SchoolId.New();
        var timetable = WithBreak(-1, 3);
        var rooms = new List<Room> { new(school, "قاعة 1") };
        var absent = Teacher(school, "غ", "ائب");
        var present = Teacher(school, "ح", "اضر");
        var teachers = new List<Teacher> { absent, present };

        var result = SupervisionGenerator.Generate(timetable, teachers, rooms, new[] { absent.Id }, new DateOnly(2026, 10, 25));

        Assert.Single(result);
        Assert.Equal(present.Id, result[0].SupervisorTeacherId);
    }

    [Fact]
    public void Generate_SkipsTeacherWhoIsTeachingInBreakPeriod()
    {
        var school = SchoolId.New();
        var timetable = WithBreak(-1, 3);
        var rooms = new List<Room> { new(school, "قاعة 1") };
        var busy = Teacher(school, "م", "درس");
        var free = Teacher(school, "ف", "ارغ");

        var lesson = new Lesson((TimetableId)timetable.Id, SubjectId.New(), (TeacherId)busy.Id, ClassId.New(),
            DomainDayOfWeek.Sunday, PeriodNumber.Create(3));
        timetable.AddLesson(lesson);

        var result = SupervisionGenerator.Generate(timetable, new List<Teacher> { busy, free }, rooms, Array.Empty<Guid>(), new DateOnly(2026, 10, 25));

        Assert.Single(result);
        Assert.Equal(free.Id, result[0].SupervisorTeacherId);
    }

    [Fact]
    public void Generate_ReturnsEmpty_WhenNoBreaks()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(SchoolId.New(), "بلا استراحة", 2026, 2027, 1);
        var rooms = new List<Room> { new(school, "قاعة 1") };
        var teachers = new List<Teacher> { Teacher(school, "أ", "ب") };

        var result = SupervisionGenerator.Generate(timetable, teachers, rooms, Array.Empty<Guid>(), new DateOnly(2026, 10, 25));

        Assert.Empty(result);
    }
}

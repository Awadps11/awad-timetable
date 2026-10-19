using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>اختبارات إسناد قاعة لكل شعبة.</summary>
public class ClassRoomAssignerTests
{
    private static (Timetable Timetable, List<Class> Classes) Build(int classCount, int lessonsPerClass)
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "قاعات", 2026, 2027, 1);
        var classes = new List<Class>();

        for (var c = 0; c < classCount; c++)
        {
            var cls = new Class(school, $"فصل {c + 1}", 7, 'أ');
            classes.Add(cls);

            for (var i = 0; i < lessonsPerClass; i++)
            {
                var lesson = new Lesson((TimetableId)timetable.Id, SubjectId.New(), TeacherId.New(), (ClassId)cls.Id,
                    DomainDayOfWeek.Saturday, new PeriodNumber(0));
                lesson.AttachReferences(null, null, cls);
                timetable.AddLesson(lesson);
            }
        }

        return (timetable, classes);
    }

    [Fact]
    public void Assign_DoesNothing_WhenNoRooms()
    {
        var (timetable, classes) = Build(3, 5);

        var assigned = ClassRoomAssigner.Assign(timetable, Array.Empty<Room>(), classes);

        Assert.Equal(0, assigned);
        Assert.All(timetable.Lessons, l => Assert.Null(l.RoomId));
    }

    [Fact]
    public void Assign_GivesEachClassItsOwnRoom()
    {
        var school = SchoolId.New();
        var (timetable, classes) = Build(3, 5);

        var rooms = new List<Room>
        {
            new(school, "قاعة 1", "ق1", 30),
            new(school, "قاعة 2", "ق2", 30),
            new(school, "قاعة 3", "ق3", 30),
        };

        var assigned = ClassRoomAssigner.Assign(timetable, rooms, classes);

        Assert.Equal(15, assigned);

        // كل شعبة لها قاعة واحدة موحّدة لكل حصصها
        foreach (var cls in classes)
        {
            var roomIds = timetable.Lessons.Where(l => l.ClassId == cls.Id).Select(l => l.RoomId).Distinct().ToList();
            Assert.Single(roomIds);
            Assert.NotNull(roomIds[0]);
        }

        // لا تشترك شعبتان في القاعة عند كفاية العدد
        var distinct = timetable.Lessons.Select(l => l.RoomId!.Value).Distinct().Count();
        Assert.Equal(3, distinct);
    }

    [Fact]
    public void Assign_LeavesClassWithoutRoom_WhenRoomsAreFewer()
    {
        var school = SchoolId.New();
        var (timetable, classes) = Build(3, 2);

        var rooms = new List<Room> { new(school, "قاعة 1", "ق1", 30) };

        var assigned = ClassRoomAssigner.Assign(timetable, rooms, classes);

        Assert.Equal(2, assigned); // شعبة واحدة فقط حصلت على القاعة
    }

    [Fact]
    public void Assign_PrefersTeacherRoom_ForTeacherLessons()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "قاعة معلم", 2026, 2027, 1);

        var teacher = new Teacher(school, "سالم", "الراشدي");
        var cls = new Class(school, "أول متوسط 1", 7, 'أ');

        for (var i = 0; i < 3; i++)
        {
            var lesson = new Lesson((TimetableId)timetable.Id, SubjectId.New(), (TeacherId)teacher.Id, (ClassId)cls.Id,
                DomainDayOfWeek.Saturday, new PeriodNumber(0));
            lesson.AttachReferences(null, teacher, cls);
            timetable.AddLesson(lesson);
        }

        var teacherRoom = new Room(school, "قاعة سالم", "ق.س", 30);
        teacherRoom.SetTeacherAssignment((Guid)teacher.Id);
        var rooms = new List<Room> { teacherRoom, new(school, "قاعة 2", "ق2", 30) };

        var assigned = ClassRoomAssigner.Assign(timetable, rooms, new List<Class> { cls });

        Assert.Equal(3, assigned);
        Assert.All(timetable.Lessons, l => Assert.Equal((Guid)teacherRoom.Id, l.RoomId!.Value));
    }

    [Fact]
    public void Assign_UsesSharedRoomsAsFallback_WhenEnough()
    {
        var school = SchoolId.New();
        var (timetable, classes) = Build(2, 2);

        var shared1 = new Room(school, "مشتركة 1", "م1", 30); shared1.SetShared(true);
        var shared2 = new Room(school, "مشتركة 2", "م2", 30); shared2.SetShared(true);

        var assigned = ClassRoomAssigner.Assign(timetable, new List<Room> { shared1, shared2 }, classes);

        Assert.Equal(4, assigned);
        foreach (var cls in classes)
        {
            var rooms = timetable.Lessons.Where(l => l.ClassId == cls.Id).Select(l => l.RoomId!.Value).Distinct().ToList();
            Assert.Single(rooms);
        }
    }

    [Fact]
    public void Assign_DoesNotUseSharedRooms_WhenFewerThanClasses()
    {
        var school = SchoolId.New();
        var (timetable, classes) = Build(3, 2);

        var shared1 = new Room(school, "مشتركة 1", "م1", 30); shared1.SetShared(true);

        var assigned = ClassRoomAssigner.Assign(timetable, new List<Room> { shared1 }, classes);

        Assert.Equal(0, assigned); // قاعة واحدة لثلاث شعب: لا إسناد لتجنّب الاستحالة
    }
}

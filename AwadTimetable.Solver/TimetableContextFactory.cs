using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;

namespace AwadTimetable.Solver;

/// <summary>
/// يبني سياق تقييم القيود من الجدول الفعلي (بدلاً من سياق فارغ لا يكتشف شيئًا).
/// العلاقات (Teacher/Room/Class/Subject) قد تكون null عند بناء الجدول في الذاكرة،
/// لذلك تُجمع هنا بأمان وتُستبعد القيم الفارغة.
/// </summary>
public static class TimetableContextFactory
{
    public static TimetableContext Build(Timetable timetable)
    {
        var lessons = timetable.Lessons ?? new List<Lesson>();

        var teachers = lessons
            .Select(l => l.Teacher)
            .OfType<Teacher>()
            .Distinct()
            .ToList();

        var rooms = lessons
            .Select(l => l.Room)
            .OfType<Room>()
            .Distinct()
            .ToList();

        var classes = lessons
            .Select(l => l.Class)
            .OfType<Class>()
            .Distinct()
            .ToList();

        var subjects = lessons
            .Select(l => l.Subject)
            .OfType<Subject>()
            .Distinct()
            .ToList();

        var availabilities = timetable.TeacherAvailabilities?.ToList()
            ?? new List<TeacherAvailability>();

        return new TimetableContext(
            timetable,
            lessons,
            teachers,
            rooms,
            classes,
            subjects,
            availabilities);
    }
}

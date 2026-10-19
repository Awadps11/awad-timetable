using System;
using System.Collections.Generic;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;

namespace AwadTimetable.Core.Constants;

/// <summary>
/// بنّاء توقيت الفترات من إعدادات بسيطة (نوع المدرسة، أيام الدراسة، عدد الحصص، زمن الحصة).
/// يضع الفسح تلقائياً حسب المرحلة:
/// - ابتدائي: فسحة واحدة بعد الحصة الثالثة (10 دقائق).
/// - متوسط: فسحة أولى بعد الثانية (10) وفسحة ثانية بعد الرابعة (10).
/// - ثانوي: فسحة واحدة دائماً بعد الثالثة (20 دقيقة).
/// </summary>
public static class TimingBuilder
{
    public const int DefaultLessonMinutes = 45;
    public const int DefaultLessonsPerDay = 7;
    public const int DefaultStartMinutes = 7 * 60; // 07:00

    /// <summary>فسح المرحلة: (بعد الحصة رقم، المدة بالدقائق)</summary>
    public static IReadOnlyList<(int AfterLesson, int Minutes)> BreakPlan(SchoolType type) => type switch
    {
        SchoolType.Primary => new[] { (3, 10) },
        SchoolType.Middle => new[] { (2, 10), (4, 10) },
        SchoolType.Secondary => new[] { (3, 20) },
        _ => new[] { (3, 10) },
    };

    /// <summary>بناء توقيت لكل مرحلة على حدة (توقيت محصص للمتوسط وتوقيت مخصص للثانوي)</summary>
    public static List<PeriodTime> BuildForStages(
        IReadOnlyList<(SchoolType Type, string Stage, int LessonsPerDay, int LessonMinutes)> stages,
        IReadOnlyList<int> workDays,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>>? periodsByDay = null,
        int startMinutes = DefaultStartMinutes)
    {
        var result = new List<PeriodTime>();

        foreach (var s in stages)
        {
            var defaultLessons = Math.Clamp(s.LessonsPerDay, 1, 12);
            var lesson = Math.Clamp(s.LessonMinutes, 20, 120);
            var breaks = BreakPlan(s.Type);
            var days = workDays is { Count: > 0 } ? workDays : new List<int> { 0, 1, 2, 3, 4, 5 };

            IReadOnlyDictionary<int, int>? perDay = null;
            periodsByDay?.TryGetValue(s.Stage, out perDay);

            foreach (var day in days)
            {
                var lessons = perDay is not null && perDay.TryGetValue(day, out var pd) && pd > 0
                    ? Math.Clamp(pd, 1, 12)
                    : defaultLessons;

                var t = startMinutes;
                for (var p = 1; p <= lessons; p++)
                {
                    result.Add(new PeriodTime
                    {
                        Period = (byte)p,
                        StartMinutes = t,
                        EndMinutes = t + lesson,
                        Day = day,
                        Stage = s.Stage,
                    });

                    t += lesson;
                    foreach (var (afterLesson, minutes) in breaks)
                    {
                        if (p == afterLesson) t += minutes;
                    }
                }
            }
        }

        return result;
    }

    public static List<PeriodTime> Build(
        SchoolType type,
        IReadOnlyList<int> workDays,
        int lessonsPerDay,
        int lessonMinutes = DefaultLessonMinutes,
        int startMinutes = DefaultStartMinutes)
    {
        var lessons = Math.Clamp(lessonsPerDay, 1, 12);
        var lesson = Math.Clamp(lessonMinutes, 20, 120);
        var breaks = BreakPlan(type);

        var result = new List<PeriodTime>();
        var days = workDays is { Count: > 0 } ? workDays : new List<int> { 0, 1, 2, 3, 4, 5 };

        foreach (var day in days)
        {
            var t = startMinutes;
            for (var p = 1; p <= lessons; p++)
            {
                result.Add(new PeriodTime
                {
                    Period = (byte)p,
                    StartMinutes = t,
                    EndMinutes = t + lesson,
                    Day = day,
                    Stage = null,
                });

                t += lesson;

                foreach (var (afterLesson, minutes) in breaks)
                {
                    if (p == afterLesson) t += minutes;
                }
            }
        }

        return result;
    }
}

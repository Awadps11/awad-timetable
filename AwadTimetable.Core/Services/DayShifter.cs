using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>
/// إزاحة حصص الجدول عند تغيير أيام الدراسة:
/// - اليوم المحتفظ به ينتقل إلى موضعه الجديد بنفس الترتيب.
/// - اليوم المحذوف تنتقل حصصه إلى أقرب يوم موجود (الأقرب في الترتيب الأصلي).
/// النتيجة: الجدول يتحرك تلقائياً للأمام أو للخلف دون فقد حصص.
/// </summary>
public static class DayShifter
{
    public static int Shift(Timetable timetable, IReadOnlyList<int> oldDays, IReadOnlyList<int> newDays)
    {
        if (timetable is null) throw new ArgumentNullException(nameof(timetable));
        if (newDays is null || newDays.Count == 0) return 0;

        var olds = oldDays?.Distinct().OrderBy(d => d).ToList() ?? new List<int>();
        var news = newDays.Distinct().OrderBy(d => d).ToList();

        if (olds.Count == 0) olds = news;

        // خريطة: كل يوم قديم → يوم جديد
        var map = new Dictionary<int, int>();
        for (var i = 0; i < olds.Count; i++)
        {
            map[olds[i]] = i < news.Count
                ? news[Math.Min(i, news.Count - 1)]
                : Nearest(news, olds[i]);
        }

        var moved = 0;
        foreach (var lesson in timetable.Lessons)
        {
            var current = (int)lesson.Day;
            if (!map.TryGetValue(current, out var target))
                target = Nearest(news, current);

            if (target == current) continue;
            lesson.Reschedule((DomainDayOfWeek)target, lesson.Period);
            moved++;
        }

        return moved;
    }

    private static int Nearest(IReadOnlyList<int> days, int day)
        => days.OrderBy(d => Math.Abs(d - day)).ThenBy(d => d).First();
}

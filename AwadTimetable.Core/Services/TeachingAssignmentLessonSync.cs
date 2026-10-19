using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>نتيجة مزامنة الحصص مع الإسنادات</summary>
public readonly record struct AssignmentSyncResult(int Created, int Removed);

/// <summary>
/// مزامنة الحصص مع طبقة الإسناد المركزية (معلم + مادة + فصل + عدد حصص أسبوعية).
/// المصدر هو <see cref="Timetable.Assignments"/>؛ تُنشأ الحصص الناقصة كغير مُسندة (Period = 0)
/// وتُحذف الزائدة لنفس العلاقة الثلاثية، فتظل عملية التوليد قابلة للتكرار دون تكرار الحصص.
/// </summary>
public static class TeachingAssignmentLessonSync
{
    public static AssignmentSyncResult Sync(
        Timetable timetable,
        Func<Guid, Teacher?>? teacherLookup = null,
        Func<Guid, Subject?>? subjectLookup = null,
        Func<Guid, Class?>? classLookup = null)
    {
        ArgumentNullException.ThrowIfNull(timetable);

        var created = 0;
        var removed = 0;

        var groups = (timetable.Assignments ?? new List<TeachingAssignment>())
            .GroupBy(a => (a.TeacherId, a.SubjectId, a.ClassId))
            .Select(g => new
            {
                g.Key.TeacherId,
                g.Key.SubjectId,
                g.Key.ClassId,
                Periods = g.Where(a => a.IsEnabled).Sum(a => Math.Max(0, a.WeeklyPeriods)),
                Enabled = g.Any(a => a.IsEnabled),
            })
            .ToList();

        foreach (var g in groups)
        {
            var existing = timetable.Lessons
                .Where(l => (Guid)l.TeacherId == g.TeacherId
                    && (Guid)l.SubjectId == g.SubjectId
                    && (Guid)l.ClassId == g.ClassId)
                .ToList();

            if (!g.Enabled)
            {
                // إسناد معطَّل: لا نحذف الحصص المجدولة، بل نزيل غير المُسندة فقط
                foreach (var lesson in existing.Where(l => l.Period.Value < 1).ToList())
                {
                    timetable.RemoveLesson(lesson);
                    existing.Remove(lesson);
                    removed++;
                }
                continue;
            }

            // احذف الزائد: غير المُسند أولاً ثم غير الثابت
            var overflow = existing.Count - g.Periods;
            if (overflow > 0)
            {
                foreach (var lesson in existing.Where(l => l.Period.Value < 1).Take(overflow).ToList())
                {
                    timetable.RemoveLesson(lesson);
                    existing.Remove(lesson);
                    removed++;
                }

                overflow = existing.Count - g.Periods;
                if (overflow > 0)
                {
                    foreach (var lesson in existing.Where(l => !l.IsFixed).Take(overflow).ToList())
                    {
                        timetable.RemoveLesson(lesson);
                        existing.Remove(lesson);
                        removed++;
                    }
                }
            }

            var teacher = teacherLookup?.Invoke(g.TeacherId);
            var subject = subjectLookup?.Invoke(g.SubjectId);
            var @class = classLookup?.Invoke(g.ClassId);

            for (var i = existing.Count; i < g.Periods; i++)
            {
                var lesson = new Lesson(
                    (TimetableId)timetable.Id,
                    (SubjectId)g.SubjectId,
                    (TeacherId)g.TeacherId,
                    (ClassId)g.ClassId,
                    DomainDayOfWeek.Saturday,
                    new PeriodNumber(0));

                lesson.AttachReferences(subject, teacher, @class);
                timetable.AddLesson(lesson);
                created++;
            }
        }

        return new AssignmentSyncResult(created, removed);
    }
}

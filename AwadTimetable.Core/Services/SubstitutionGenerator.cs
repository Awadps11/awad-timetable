using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;
using DayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;
using SystemDay = System.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>
/// صف نتيجة تعيين بديل لحصة واحدة
/// </summary>
public sealed record SubstitutionAssignmentRow(
    Guid LessonId,
    DayOfWeek Day,
    byte Period,
    Guid ClassId,
    Guid SubjectId,
    Guid AbsentTeacherId,
    Guid? SubstituteTeacherId,
    int? Tier,
    string Status);

/// <summary>
/// مولّد تعيين المعلمين البديلين (نظام الانتظار بثلاث فئات):
/// لكل معلم غائب تُولَّد الحصص البديلة بأولوية البديل الأول ثم الثاني ثم الثالث،
/// ويُرفض البديل المشغول في نفس الفترة أو المكلف بتغطية غائب آخر.
/// </summary>
public static class SubstitutionGenerator
{
    /// <summary>تحويل تاريخ ميلادي إلى رقم يوم الجدول: السبت=0 … الخميس=5، الجمعة=6 (لا حصص)</summary>
    public static int GetTimetableDayIndex(DateOnly date)
    {
        return date.DayOfWeek switch
        {
            SystemDay.Saturday => 0,
            SystemDay.Sunday => 1,
            SystemDay.Monday => 2,
            SystemDay.Tuesday => 3,
            SystemDay.Wednesday => 4,
            SystemDay.Thursday => 5,
            _ => 6 // الجمعة
        };
    }

    /// <summary>
    /// توليد تعيينات البديل ليوم محدد للمعلمين الغائبين المحددين
    /// </summary>
    public static IReadOnlyList<SubstitutionAssignmentRow> Generate(
        Timetable timetable,
        IReadOnlyList<Teacher> teachers,
        IReadOnlyCollection<Guid> absentTeacherIds,
        DateOnly date)
    {
        if (timetable == null) throw new ArgumentNullException(nameof(timetable));
        if (teachers == null) throw new ArgumentNullException(nameof(teachers));

        var dayIndex = GetTimetableDayIndex(date);
        var results = new List<SubstitutionAssignmentRow>();

        if (dayIndex > 5)
            return results; // الجمعة: لا حصص في الجدول

        var day = (DayOfWeek)dayIndex;
        var absentSet = absentTeacherIds?.ToHashSet() ?? new HashSet<Guid>();
        var teacherById = teachers.ToDictionary(t => t.Id);

        var allLessons = timetable.Lessons.Where(l => l.Day == day).ToList();

        // المعلمون الأحرار في كل فترة: لا يملكون حصة تدريس في تلك الفترة
        var busyTeaching = new HashSet<(Guid TeacherId, byte Period)>();
        foreach (var lesson in allLessons)
        {
            busyTeaching.Add((lesson.TeacherId.Value, lesson.Period.Value));
        }

        // المعلمون المشغولون بالتغطية (تم تكليفهم بالفعل هذه الفترة)
        var busyCovering = new HashSet<(Guid TeacherId, byte Period)>();

        // حصر الغائبين أولاً حتى لا يحل محلهم غائب آخر
        var absentLessons = allLessons
            .Where(l => absentSet.Contains(l.TeacherId.Value))
            .OrderBy(l => l.Period.Value)
            .ThenBy(l => l.ClassId.Value)
            .ToList();

        foreach (var lesson in absentLessons)
        {
            var absentTeacherId = lesson.TeacherId.Value;
            var period = lesson.Period.Value;

            Guid? substituteId = null;
            int? tier = null;

            if (teacherById.TryGetValue(absentTeacherId, out var absentTeacher))
            {
                var candidates = new (Guid Id, int Tier)[]
                {
                    (absentTeacher.Substitute1TeacherId ?? Guid.Empty, 1),
                    (absentTeacher.Substitute2TeacherId ?? Guid.Empty, 2),
                    (absentTeacher.Substitute3TeacherId ?? Guid.Empty, 3)
                };

                foreach (var (candidateId, candidateTier) in candidates)
                {
                    if (candidateId == Guid.Empty || candidateId == absentTeacherId)
                        continue;
                    if (absentSet.Contains(candidateId))
                        continue; // البديل المطلوب غائب أيضاً
                    if (!teacherById.TryGetValue(candidateId, out var candidate) || !candidate.IsActive)
                        continue;
                    if (busyTeaching.Contains((candidateId, period)))
                        continue; // لديه حصة تدريس في نفس الفترة
                    if (busyCovering.Contains((candidateId, period)))
                        continue; // مكلف بتغطية غائب آخر في نفس الفترة

                    substituteId = candidateId;
                    tier = candidateTier;
                    busyCovering.Add((candidateId, period));
                    break;
                }
            }

            results.Add(new SubstitutionAssignmentRow(
                lesson.Id,
                day,
                period,
                lesson.ClassId.Value,
                lesson.SubjectId.Value,
                absentTeacherId,
                substituteId,
                tier,
                substituteId.HasValue ? "معيّن" : "بدون بديل"));
        }

        return results;
    }
}

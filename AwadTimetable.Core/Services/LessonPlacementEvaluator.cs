using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>حالة موضع حصة محتمل: ممنوع / مقبول (بتبديل) / جيد (خلية فارغة).</summary>
public enum PlacementState
{
    Forbidden = 0,
    Acceptable = 1,
    Good = 2
}

/// <summary>
/// تقييم مواضع الحصص بقواعد نقية لا تعتمد على الواجهة.
/// تطابق القواعد التي يطبقها نقل الحصة بالسحب والإفلات، فيكون التلوين أثناء السحب
/// والتشخيص متسقين مع النتيجة الفعلية.
/// </summary>
public static class LessonPlacementEvaluator
{
    /// <summary>
    /// يقيّم موضعًا (يوم، فترة) للحصة داخل فصل محدد.
    /// </summary>
    /// <param name="timetable">الجدول الحالي (مصدر الحصص الأخرى).</param>
    /// <param name="lesson">الحصة المراد وضعها.</param>
    /// <param name="targetDay">اليوم المستهدف.</param>
    /// <param name="targetPeriod">الفترة المستهدفة.</param>
    /// <param name="targetClassId">الفصل المستهدف (في وضع المصفوفة هو صف الشبكة).</param>
    public static (PlacementState State, string Reason) Evaluate(
        Timetable timetable,
        Lesson lesson,
        DomainDayOfWeek targetDay,
        PeriodNumber targetPeriod,
        Guid targetClassId)
    {
        ArgumentNullException.ThrowIfNull(timetable);
        ArgumentNullException.ThrowIfNull(lesson);

        if (lesson.IsFixed)
            return (PlacementState.Forbidden, "الحصة مثبتة.");

        var sameClass = (Guid)lesson.ClassId == targetClassId;
        if (sameClass && lesson.Day == targetDay && lesson.Period.Value == targetPeriod.Value)
            return (PlacementState.Good, "الموضع الحالي.");

        // الإغلاق الملزم فقط (أولوية عالية) يمنع الموضع؛ الإغلاق المنخفض يُقبل لكنّه أقل جودة
        var availability = timetable.TeacherAvailabilities.FirstOrDefault(a =>
            (Guid)a.TeacherId == (Guid)lesson.TeacherId
            && a.Day == targetDay
            && a.Period.Value == targetPeriod.Value);

        if (availability is { IsHardBlock: true })
            return (PlacementState.Forbidden, "المعلم غير متاح في هذه الفترة (إغلاق ملزم).");

        if (availability is { IsSoftBlock: true })
            return (PlacementState.Acceptable, "المعلم أغلق هذه الفترة بأولوية منخفضة.");

        var sameSlot = timetable.Lessons.Where(l =>
            (Guid)l.Id != (Guid)lesson.Id && l.Day == targetDay && l.Period.Value == targetPeriod.Value);

        var occupant = sameSlot.FirstOrDefault(l =>
            (Guid)l.ClassId == targetClassId
            || (Guid)l.TeacherId == (Guid)lesson.TeacherId
            || (l.RoomId != null && lesson.RoomId != null && (Guid)l.RoomId == (Guid)lesson.RoomId));

        if (sameClass)
        {
            if (occupant == null)
                return (PlacementState.Good, "الخلية فارغة ولا تعارض فيها.");
            if (occupant.IsFixed)
                return (PlacementState.Forbidden, "الخلية تشغلها حصة مثبتة.");

            // التبديل مسموح فقط إن كانت الحصة المشغولة من الفصل نفسه (تُبدَّل مع حصتنا).
            // إن كانت مشغولة بمعلم أو قاعة من فصل آخر فالتبديل يخلق تعارضًا جديدًا.
            if ((Guid)occupant.ClassId != targetClassId)
                return (PlacementState.Forbidden, "المعلم أو القاعة مشغولة في هذا الوقت.");

            return (PlacementState.Acceptable, "تبديل مع الحصة الموجودة.");
        }

        // فصل مختلف: يُمنع نقل الحصة إلى فصل غير مُسند لهذا المعلم في هذه المادة
        if (!IsClassAssigned(timetable, lesson, targetClassId))
            return (PlacementState.Forbidden, "هذه المادة غير مُسندة لهذا المعلم في الفصل الهدف.");

        // فصل مختلف: لا يُسمح بالنقل إن وُجد تعارض في الوقت
        if (occupant != null)
            return (PlacementState.Forbidden, "المعلم أو القاعة مشغولة في هذا الوقت.");

        return (PlacementState.Good, "لا تعارض في هذا الوقت.");
    }

    /// <summary>
    /// هل يجوز نقل حصة (معلم+مادة) إلى الفصل الهدف؟ يُقيَّد ذلك بطبقة الإسناد المركزية:
    /// إن كانت لإسنادات الجدول مدخلات لـ(هذا المعلم، هذه المادة) فيلزم أن يكون الفصل الهدف ضمن شعبها.
    /// إن لم تُستخدم طبقة الإسناد لهذا الزوج فتُترك الحركة اليدوية حرّة.
    /// </summary>
    private static bool IsClassAssigned(Timetable timetable, Lesson lesson, Guid targetClassId)
    {
        if (timetable.Assignments.Count == 0)
            return true;

        var subjectId = (Guid)lesson.SubjectId;
        var teacherId = (Guid)lesson.TeacherId;

        var matching = timetable.Assignments
            .Where(a => a.IsEnabled && a.SubjectId == subjectId && a.TeacherId == teacherId)
            .ToList();

        if (matching.Count == 0)
            return true;

        return matching.Any(a => a.ClassId == targetClassId);
    }

    /// <summary>
    /// يحسب كل المواضع الممكنة (يوم × فترة من 1 إلى 12) لحصة، ويجمع أسباب الرفض.
    /// </summary>
    public static PlacementDiagnosis Diagnose(Timetable timetable, Lesson lesson, IEnumerable<DomainDayOfWeek>? days = null)
    {
        ArgumentNullException.ThrowIfNull(timetable);
        ArgumentNullException.ThrowIfNull(lesson);

        var result = new PlacementDiagnosis();
        var dayList = days?.ToList() ?? Enum.GetValues<DomainDayOfWeek>().ToList();
        var classId = (Guid)lesson.ClassId;
        var reasonCounts = new Dictionary<string, int>();

        foreach (var day in dayList)
        {
            for (var p = 1; p <= 12; p++)
            {
                var (state, reason) = Evaluate(timetable, lesson, day, PeriodNumber.Create((byte)p), classId);

                if (state == PlacementState.Good)
                {
                    result.GoodSlots.Add((day, p));
                    continue;
                }
                if (state == PlacementState.Acceptable) continue;

                var reasons = new List<string> { reason };
                var availability = timetable.TeacherAvailabilities.FirstOrDefault(a =>
                    (Guid)a.TeacherId == (Guid)lesson.TeacherId && a.Day == day && a.Period.Value == p);
                if (availability is { IsHardBlock: true })
                    reasons.Add("المعلم غير متاح في هذه الفترة (إغلاق ملزم)");
                else if (availability is { IsSoftBlock: true })
                    reasons.Add("المعلم أغلق هذه الفترة بأولوية منخفضة");

                foreach (var r in reasons)
                    reasonCounts[r] = reasonCounts.TryGetValue(r, out var c) ? c + 1 : 1;
            }
        }

        result.ReasonCounts = reasonCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new KeyValuePair<string, int>(kv.Key, kv.Value))
            .ToList();
        return result;
    }
}

/// <summary>نتيجة تشخيص حصة: المواضع المناسبة وأسباب الرفض مرتبة حسب التكرار.</summary>
public sealed class PlacementDiagnosis
{
    public List<(DomainDayOfWeek Day, int Period)> GoodSlots { get; } = new();
    public IReadOnlyList<KeyValuePair<string, int>> ReasonCounts { get; set; } = Array.Empty<KeyValuePair<string, int>>();
}

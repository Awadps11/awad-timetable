using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;

namespace AwadTimetable.Core.Services;

/// <summary>
/// بند في خطة الانتظار: معلم يُضاف لفئة انتظار في مرحلة، بعدد حصص أسبوعية.
/// (يمكن للمعلم أن يظهر في أكثر من فئة/مرحلة.)
/// </summary>
public sealed record WaitingPlanEntry(string Stage, int Tier, Guid TeacherId, int Periods);

/// <summary>
/// التحقق من خطة الانتظار قبل توليد الحصص:
/// - الفئة بين 1 و3.
/// - الحصص ≥ 1.
/// - مجموع حصص الفئة لا يتجاوز سعة شعبة الانتظار (أيام المرحلة × حصصها اليومية).
/// </summary>
public static class WaitingPlan
{
    public const int TierCount = 3;

    public static int Capacity(Timetable timetable, string stage)
    {
        var cfg = timetable.StageConfigs.FirstOrDefault(c => c.Stage == stage);
        if (cfg is null) return 0;

        var days = cfg.WorkDays.Count > 0 ? cfg.WorkDays.Count : 6;
        return days * Math.Clamp(cfg.LessonsPerDay, 1, 12);
    }

    public static IReadOnlyList<string> Validate(Timetable timetable, IReadOnlyList<WaitingPlanEntry> entries)
    {
        var errors = new List<string>();

        if (entries.Count == 0)
        {
            errors.Add("أضف بنداً واحداً على الأقل.");
            return errors;
        }

        foreach (var e in entries)
        {
            if (e.Tier < 1 || e.Tier > TierCount)
                errors.Add($"الفئة {e.Tier} غير صحيحة (1..{TierCount}).");

            if (e.Periods < 1)
                errors.Add($"عدد الحصص يجب أن يكون 1 على الأقل (الفئة {e.Tier}).");

            if (timetable.StageConfigs.All(c => c.Stage != e.Stage))
                errors.Add($"المرحلة «{e.Stage}» غير معرّفة في هذا الجدول.");
        }

        foreach (var group in entries.GroupBy(e => (e.Stage, e.Tier)))
        {
            var capacity = Capacity(timetable, group.Key.Stage);
            if (capacity <= 0) continue;

            var total = group.Sum(e => Math.Max(0, e.Periods));
            if (total > capacity)
                errors.Add($"فئة انتظار {group.Key.Tier} في {group.Key.Stage}: مجموع الحصص {total} يتجاوز السعة {capacity}.");
        }

        return errors;
    }

    /// <summary>أسماء شعب الانتظار: «انتظار {المرحلة} {الفئة}»</summary>
    public static string ClassName(string stage, int tier) => $"انتظار {stage} {tier}";

    /// <summary>اسم مادة الانتظار: «انتظار {الفئة}»</summary>
    public static string SubjectName(int tier) => $"انتظار {tier}";
}

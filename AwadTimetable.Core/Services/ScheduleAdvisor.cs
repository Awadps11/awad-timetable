using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

public enum AdvisorSeverity : byte
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>ملاحظة تشخيصية واحدة من مستشار الجدول</summary>
public sealed record AdvisorFinding(
    string Code,
    AdvisorSeverity Severity,
    string Title,
    string Message,
    IReadOnlyList<string> Entities);

/// <summary>
/// مستشار الجدول (Advisor): فحوصات تشخيصية مشابهة لتلك الموجودة في aSc قبل التوليد وبعده،
/// مثل الفصول المكتظة، والقاعات غير الموجودة، وعبء المعلم، والتعطل الكثير، ومادة تتجاوز أيام الأسبوع.
/// </summary>
public static class ScheduleAdvisor
{
    private const int MaxAcceptableGapsPerDay = 2;

    public static IReadOnlyList<AdvisorFinding> Analyze(TimetableContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var findings = new List<AdvisorFinding>();

        findings.AddRange(OverbookedClasses(context));
        findings.AddRange(RoomsMissingOrTooSmall(context));
        findings.AddRange(TeacherWorkload(context));
        findings.AddRange(TeacherGaps(context));
        findings.AddRange(SubjectMoreThanDays(context));

        // الأخطر أولاً
        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<AdvisorFinding> OverbookedClasses(TimetableContext context)
    {
        var timetable = context.Timetable;

        foreach (var group in context.Lessons.GroupBy(l => l.ClassId))
        {
            var lessons = group.ToList();
            var stage = timetable.GetStageForClass(group.Key);
            var days = ActiveDays(timetable, stage);
            if (days.Count == 0) continue;

            var cfg = timetable.StageConfigs.FirstOrDefault(c => c.Stage == stage);
            var perDay = cfg is { LessonsPerDay: > 0 } ? cfg.LessonsPerDay : 7;
            var capacity = days.Count * perDay;

            if (lessons.Count > capacity)
            {
                var name = lessons.FirstOrDefault(l => l.Class is not null)?.Class?.FullName ?? group.Key.ToString();
                yield return new AdvisorFinding(
                    "class-overbooked",
                    AdvisorSeverity.Error,
                    "فصل مكتظ",
                    $"الفصل «{name}» لديه {lessons.Count} حصة بينما الطاقة القصوى {capacity} خانة.",
                    new[] { name });
            }
        }
    }

    private static IEnumerable<AdvisorFinding> RoomsMissingOrTooSmall(TimetableContext context)
    {
        // قاعة مُشار إليها لكنها غير موجودة
        var missing = context.Lessons
            .Where(l => l.RoomId is not null && l.Room is null)
            .ToList();

        if (missing.Count > 0)
        {
            yield return new AdvisorFinding(
                "room-missing",
                AdvisorSeverity.Error,
                "قاعة غير موجودة",
                $"{missing.Count} حصة تشير إلى قاعة غير معرّفة.",
                missing.Select(l => l.GetDisplayName()).Distinct().Take(20).ToList());
        }

        // سعة القاعة أقل من عدد طلاب الفصل
        var tooSmall = context.Lessons
            .Where(l => l.Room is not null && l.Class is not null && l.Class.StudentCount > l.Room.Capacity)
            .ToList();

        if (tooSmall.Count > 0)
        {
            yield return new AdvisorFinding(
                "room-too-small",
                AdvisorSeverity.Warning,
                "سعة القاعة أقل من عدد الطلاب",
                $"{tooSmall.Count} حصة في قاعة سعتها أقل من عدد طلاب الفصل.",
                tooSmall.Select(l => $"{l.Room!.Name} ({l.Room.Capacity}) مقابل {l.Class!.FullName} ({l.Class.StudentCount})")
                    .Distinct().Take(20).ToList());
        }
    }

    private static IEnumerable<AdvisorFinding> TeacherWorkload(TimetableContext context)
    {
        foreach (var group in context.Lessons.GroupBy(l => l.TeacherId))
        {
            var lessons = group.ToList();
            var teacher = lessons.FirstOrDefault(l => l.Teacher is not null)?.Teacher;
            if (teacher is null) continue;

            var name = teacher.FullName;

            if (lessons.Count > teacher.MaxPeriodsPerWeek)
            {
                yield return new AdvisorFinding(
                    "teacher-week-overload",
                    AdvisorSeverity.Warning,
                    "عبء أسبوعي زائد للمعلم",
                    $"المعلم {name} لديه {lessons.Count} حصة بينما الحد الأسبوعي {teacher.MaxPeriodsPerWeek}.",
                    new[] { name });
            }

            var overDays = lessons
                .GroupBy(l => l.Day)
                .Where(g => g.Count() > teacher.MaxPeriodsPerDay)
                .ToList();

            if (overDays.Count > 0)
            {
                yield return new AdvisorFinding(
                    "teacher-day-overload",
                    AdvisorSeverity.Warning,
                    "عبء يومي زائد للمعلم",
                    $"المعلم {name} يتجاوز الحد اليومي ({teacher.MaxPeriodsPerDay}) في {overDays.Count} يوم.",
                    new[] { name });
            }
        }
    }

    private static IEnumerable<AdvisorFinding> TeacherGaps(TimetableContext context)
    {
        foreach (var dayGroup in context.Lessons.GroupBy(l => (l.TeacherId, l.Day)))
        {
            var periods = dayGroup.Select(l => (int)l.Period.Value).OrderBy(p => p).ToList();
            if (periods.Count < 3) continue;

            var span = periods[^1] - periods[0] + 1;
            var gaps = span - periods.Count;

            if (gaps > MaxAcceptableGapsPerDay)
            {
                var name = dayGroup.FirstOrDefault(l => l.Teacher is not null)?.Teacher?.FullName ?? dayGroup.Key.TeacherId.ToString();
                yield return new AdvisorFinding(
                    "teacher-gaps",
                    AdvisorSeverity.Info,
                    "تعطّل كثير للمعلم",
                    $"المعلم {name} لديه {gaps} فترة فراغ متفرقة في {dayGroup.Key.Day}.",
                    new[] { name });
            }
        }
    }

    private static IEnumerable<AdvisorFinding> SubjectMoreThanDays(TimetableContext context)
    {
        var timetable = context.Timetable;

        foreach (var group in context.Lessons.GroupBy(l => (l.ClassId, l.SubjectId)))
        {
            var lessons = group.ToList();
            var stage = timetable.GetStageForClass(group.Key.ClassId);
            var days = ActiveDays(timetable, stage).Count;
            if (days == 0) continue;

            if (lessons.Count > days)
            {
                var name = lessons.FirstOrDefault(l => l.Subject is not null)?.Subject?.Name ?? group.Key.SubjectId.ToString();
                var className = lessons.FirstOrDefault(l => l.Class is not null)?.Class?.FullName ?? group.Key.ClassId.ToString();
                yield return new AdvisorFinding(
                    "subject-more-than-days",
                    AdvisorSeverity.Info,
                    "مادة تتجاوز أيام الأسبوع",
                    $"المادة «{name}» للفصل «{className}» لها {lessons.Count} حصة في {days} يوم، لذا ستتكرر في يوم واحد على الأقل.",
                    new[] { $"{className}: {name}" });
            }
        }
    }

    private static List<DomainDayOfWeek> ActiveDays(Timetable timetable, string? stage)
    {
        var cfg = stage is null ? null : timetable.StageConfigs.FirstOrDefault(c => c.Stage == stage);
        var all = Enum.GetValues<DomainDayOfWeek>();

        if (cfg is { WorkDays.Count: > 0 })
            return all.Where(d => cfg.WorkDays.Contains((int)d)).ToList();

        return all.Where(d => d != DomainDayOfWeek.Friday).ToList();
    }
}

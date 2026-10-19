using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>نتيجة تقسيم حصة إلى عدة أجزاء موزّعة على أيام مختلفة.</summary>
public sealed record LessonSplitResult(
    IReadOnlyList<Lesson> Parts,
    Guid GroupId,
    Group? Group,
    RuleItem? MinDaysRule);

/// <summary>
/// تقسيم حصة (Split) إلى عدة حصص مترابطة تشترك في مجموعة واحدة وتُوزَّع على أيام مختلفة،
/// مع إمكانية إنشاء قاعدة «أدنى أيام بين الحصص» تلقائياً (على نمط FET).
/// القسمة تُقسّم المدة على عدد الأجزاء عندما تكون النتيجة مدة صحيحة (بين 30 و120 ومضاعف 5).
/// </summary>
public static class LessonSplitter
{
    public const int MinParts = 2;
    public const int MaxParts = 7;

    public static LessonSplitResult Split(
        Timetable timetable,
        Lesson source,
        int parts,
        int? minDaysBetween = null,
        bool splitDuration = true,
        string? groupName = null)
    {
        ArgumentNullException.ThrowIfNull(timetable);
        ArgumentNullException.ThrowIfNull(source);

        if (parts is < MinParts or > MaxParts)
            throw new ArgumentOutOfRangeException(nameof(parts),
                $"عدد الأجزاء يجب أن يكون بين {MinParts} و {MaxParts}.");

        if (minDaysBetween is int mdCheck && mdCheck is < 1 or > 7)
            throw new ArgumentOutOfRangeException(nameof(minDaysBetween),
                "أدنى عدد أيام بين الحصص يجب أن يكون بين 1 و 7.");

        if (!timetable.Lessons.Contains(source))
            throw new InvalidOperationException("الحصة المصدر ليست ضمن هذا الجدول.");

        var groupId = source.GroupId ?? Guid.NewGuid();
        var group = timetable.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null)
        {
            group = new Group(timetable.SchoolId, (Guid)source.ClassId, groupName ?? BuildGroupName(source));
            timetable.Groups.Add(group);
        }

        var minutes = (int)source.Duration;
        var perPart = minutes / parts;
        var usePerPart = splitDuration && perPart >= 30 && perPart % 5 == 0;
        var partDuration = usePerPart ? Duration.FromMinutes(perPart) : source.Duration;

        source.GroupId = group.Id;
        source.Group = group;
        if (usePerPart) source.SetDuration(partDuration);

        var parts2 = new List<Lesson> { source };

        var days = Enum.GetValues<DomainDayOfWeek>();
        var startIndex = Array.IndexOf(days, source.Day);

        for (var i = 1; i < parts; i++)
        {
            var day = days[(startIndex + i) % days.Length];
            var part = new Lesson(
                new TimetableId(timetable.Id),
                source.SubjectId,
                source.TeacherId,
                source.ClassId,
                day,
                source.Period,
                partDuration);

            if (source.RoomId is not null) part.AssignRoom(source.RoomId);
            part.SetType(source.Type);
            part.GroupId = group.Id;
            part.Group = group;
            part.AttachReferences(source.Subject, source.Teacher, source.Class, source.Room);
            timetable.AddLesson(part);
            parts2.Add(part);
        }

        RuleItem? rule = null;
        if (minDaysBetween is int md)
        {
            rule = new RuleItem
            {
                Kind = RuleKind.MinDaysBetweenActivities,
                Scope = RuleScope.Subject,
                TargetId = source.SubjectId,
                Title = $"أدنى {md} أيام بين حصص {source.Subject?.Name ?? "المادة"}",
                Parameters = $"{{\"minDaysBetween\": {md}}}",
                Priority = RulePriority.Hard,
                Source = "user"
            };
            timetable.Rules.Add(rule);
        }

        return new LessonSplitResult(parts2, group.Id, group, rule);
    }

    private static string BuildGroupName(Lesson source)
    {
        var subject = source.Subject?.Name ?? "مادة";
        var cls = source.Class?.Name ?? "فصل";
        return $"تقسيم {subject} — {cls}";
    }
}

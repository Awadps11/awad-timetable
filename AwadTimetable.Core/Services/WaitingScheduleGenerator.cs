using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>تكليف انتظار أساسي: (يوم، فترة، فصل) → معلم انتظار 1</summary>
public sealed record WaitingDuty(DomainDayOfWeek Day, int Period, Guid ClassId, Guid TeacherId);

/// <summary>احتياط انتظار 2/3: معلم حر في (يوم، فترة) يُستخدم عند غياب الأساسي</summary>
public sealed record WaitingBackup(DomainDayOfWeek Day, int Period, int Tier, Guid TeacherId);

/// <summary>جدول الانتظار المولّد</summary>
public sealed class WaitingSchedule
{
    /// <summary>تكليفات انتظار 1: واحد لكل (فصل، فترة)</summary>
    public IReadOnlyList<WaitingDuty> Duties { get; init; } = Array.Empty<WaitingDuty>();

    /// <summary>احتياط انتظار 2 و3 لكل (يوم، فترة) — لا تُحتسب تكليفات</summary>
    public IReadOnlyList<WaitingBackup> Backups { get; init; } = Array.Empty<WaitingBackup>();

    public IReadOnlyDictionary<Guid, int> BaseLoads { get; init; } = new Dictionary<Guid, int>();
    public IReadOnlyDictionary<Guid, int> WaitingLoads { get; init; } = new Dictionary<Guid, int>();
    public IReadOnlyDictionary<Guid, int> Tiers { get; init; } = new Dictionary<Guid, int>();

    /// <summary>خانات لم يُعثر لها على مشرف أساسي (نقص معلمين حرين)</summary>
    public IReadOnlyList<(DomainDayOfWeek Day, int Period, Guid ClassId)> Uncovered { get; init; }
        = Array.Empty<(DomainDayOfWeek, int, Guid)>();

    public int TotalDuties => Duties.Count;
    public int TotalBackups => Backups.Count;
}

/// <summary>
/// مولّد جدول الانتظار (المعلم البديل) بنموذج «بديل لكل حصة»:
/// - انتظار 1: معلم واحد لكل (فصل، فترة) — إلزامي، يُغطّي غياب معلم الحصة.
/// - انتظار 2 و3: قائمة احتياط لكل (يوم، فترة) تُستخدم عند غياب الأساسي (لا تُحتسب تكليفاً).
/// - العدالة: يُختار الأقل نصاباً تراكمياً (تدريس + انتظار) من الفئة الأنسب، ولا يُكلَّف معلم
///   أكثر من مرة في الفترة الواحدة.
/// - سبب وجود احتياط للفترة بدل تكليف 3 لكل فصل: عند 12 فصلاً و40 معلماً، المتاح في الفترة
///   الواحدة ≈ 28 معلماً، بينما 3 مشرفين لكل فصل تعني 36 معلماً وهو غير ممكن.
/// </summary>
public static class WaitingScheduleGenerator
{
    public static WaitingSchedule Generate(
        Timetable timetable,
        IReadOnlyList<Teacher> teachers,
        IReadOnlyList<Guid> classIds,
        IReadOnlyList<int> workDays,
        int periodsPerDay,
        int tierCount = 3,
        IReadOnlyCollection<Guid>? excludedTeacherIds = null,
        IReadOnlyCollection<Guid>? absentTeacherIds = null)
    {
        if (timetable is null) throw new ArgumentNullException(nameof(timetable));
        if (teachers is null) throw new ArgumentNullException(nameof(teachers));
        if (classIds is null) throw new ArgumentNullException(nameof(classIds));

        tierCount = Math.Clamp(tierCount, 2, 5);
        periodsPerDay = Math.Clamp(periodsPerDay, 1, 12);

        var excluded = excludedTeacherIds?.ToHashSet() ?? new HashSet<Guid>();
        var absent = absentTeacherIds?.ToHashSet() ?? new HashSet<Guid>();

        var active = teachers
            .Where(t => t.IsActive && !excluded.Contains(t.Id) && !absent.Contains(t.Id))
            .ToList();

        if (active.Count == 0 || workDays.Count == 0 || classIds.Count == 0)
            return new WaitingSchedule();

        // النصاب الأساسي: من الحصص المجدولة، وإلا من مجموع الإسنادات
        var baseLoad = active.ToDictionary(t => t.Id, _ => 0);
        if (timetable.Lessons.Count > 0)
        {
            foreach (var lesson in timetable.Lessons)
            {
                var id = lesson.TeacherId.Value;
                if (baseLoad.ContainsKey(id)) baseLoad[id]++;
            }
        }
        else
        {
            foreach (var a in timetable.Assignments ?? new List<TeachingAssignment>())
                if (baseLoad.ContainsKey(a.TeacherId)) baseLoad[a.TeacherId] += Math.Max(0, a.WeeklyPeriods);
        }

        // الفئات حسب النصاب: الأقل = انتظار 1
        var ranked = active.OrderBy(t => baseLoad[t.Id]).ThenBy(t => t.FullName, StringComparer.CurrentCulture).ToList();
        var tiers = new Dictionary<Guid, int>();
        for (var i = 0; i < ranked.Count; i++)
            tiers[ranked[i].Id] = Math.Min(tierCount, i * tierCount / ranked.Count + 1);

        // مشغولية التدريس
        var teaching = timetable.Lessons
            .Select(l => ((int)l.Day, l.Period.Value, l.TeacherId.Value))
            .ToHashSet();

        // الفصول التي لها حصة فعلاً في كل خانة (وإن لم توجد حصص، نستخدم كل الفصول)
        var hasLessons = timetable.Lessons.Count > 0;
        var lessonsBySlot = timetable.Lessons
            .GroupBy(l => ((int)l.Day, l.Period.Value))
            .ToDictionary(g => g.Key, g => g.Select(l => l.ClassId.Value).Distinct().ToList());

        var runningLoad = new Dictionary<Guid, int>(baseLoad);
        var waitingLoad = active.ToDictionary(t => t.Id, _ => 0);
        var duties = new List<WaitingDuty>();
        var backups = new List<WaitingBackup>();
        var uncovered = new List<(DomainDayOfWeek, int, Guid)>();

        foreach (var dayIndex in workDays.OrderBy(d => d))
        {
            var day = (DomainDayOfWeek)dayIndex;

            for (var period = 1; period <= periodsPerDay; period++)
            {
                var slotClasses = hasLessons
                    ? lessonsBySlot.TryGetValue((dayIndex, (byte)period), out var ids) ? ids : new List<Guid>()
                    : classIds;

                var usedThisPeriod = new HashSet<Guid>();

                // انتظار 1: معلم لكل فصل
                foreach (var classId in slotClasses)
                {
                    var candidate = active
                        .Where(t => !teaching.Contains((dayIndex, (byte)period, t.Id)))
                        .Where(t => !usedThisPeriod.Contains(t.Id))
                        .OrderBy(t => runningLoad[t.Id])
                        .ThenBy(t => t.FullName, StringComparer.CurrentCulture)
                        .FirstOrDefault();

                    if (candidate is null)
                    {
                        uncovered.Add((day, period, classId));
                        continue;
                    }

                    duties.Add(new WaitingDuty(day, period, classId, candidate.Id));
                    usedThisPeriod.Add(candidate.Id);
                    runningLoad[candidate.Id]++;
                    waitingLoad[candidate.Id]++;
                }

                // احتياط 2 و3: كل معلم حر في الفترة (سوى المكلَّف أساسياً) يصبح مرشحاً بفئته
                var backupCandidates = active
                    .Where(t => !teaching.Contains((dayIndex, (byte)period, t.Id)))
                    .Where(t => !usedThisPeriod.Contains(t.Id))
                    .OrderBy(t => tiers[t.Id])
                    .ThenBy(t => runningLoad[t.Id]);

                foreach (var t in backupCandidates)
                {
                    var tier = Math.Max(2, tiers[t.Id]); // الاحتياط يبدأ من الفئة 2
                    backups.Add(new WaitingBackup(day, period, Math.Min(tier, tierCount), t.Id));
                }
            }
        }

        return new WaitingSchedule
        {
            Duties = duties,
            Backups = backups,
            BaseLoads = baseLoad,
            WaitingLoads = waitingLoad,
            Tiers = tiers,
            Uncovered = uncovered,
        };
    }

    /// <summary>
    /// تغطية غياب حصة: انتظار 1 للفصل، فإن كان غائباً يُختار احتياط 2 ثم 3 في نفس الفترة.
    /// </summary>
    public static Guid? Resolve(
        WaitingSchedule schedule,
        DomainDayOfWeek day,
        int period,
        Guid classId,
        IReadOnlyCollection<Guid> absentTeacherIds)
    {
        var absent = absentTeacherIds?.ToHashSet() ?? new HashSet<Guid>();

        var primary = schedule.Duties
            .FirstOrDefault(d => d.Day == day && d.Period == period && d.ClassId == classId);

        if (primary is not null && !absent.Contains(primary.TeacherId))
            return primary.TeacherId;

        return schedule.Backups
            .Where(b => b.Day == day && b.Period == period)
            .Where(b => !absent.Contains(b.TeacherId))
            .OrderBy(b => b.Tier)
            .Select(b => (Guid?)b.TeacherId)
            .FirstOrDefault();
    }
}

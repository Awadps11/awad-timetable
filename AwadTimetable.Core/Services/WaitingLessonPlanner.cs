using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>خطة حصة انتظار: تُنشأ كحصة حقيقية لشعبة انتظار (صف في الجدول)</summary>
public sealed record WaitingLessonPlan(DomainDayOfWeek Day, int Period, string Stage, string WaitingClassName, Guid TeacherId);

/// <summary>
/// يخطط حصص الانتظار بطريقة aSc: شعب انتظار (صفوف) تُجدول لها حصص في الفترات التي
/// فيها دروس للمرحلة، ويُختار لكل حصة المعلم الأقل نصاباً الحر في تلك الفترة.
/// النتيجة: أنصبة المعلمين (تدريس + انتظار) تتقارب.
/// </summary>
public static class WaitingLessonPlanner
{
    public static IReadOnlyList<WaitingLessonPlan> Plan(
        Timetable timetable,
        IReadOnlyList<Teacher> teachers,
        IReadOnlyList<(string Stage, string WaitingClassName)> waitingClasses,
        IReadOnlyCollection<Guid>? excludedTeacherIds = null,
        IReadOnlyCollection<Guid>? absentTeacherIds = null)
    {
        if (timetable is null) throw new ArgumentNullException(nameof(timetable));
        if (teachers is null) throw new ArgumentNullException(nameof(teachers));
        if (waitingClasses is null) throw new ArgumentNullException(nameof(waitingClasses));

        var excluded = excludedTeacherIds?.ToHashSet() ?? new HashSet<Guid>();
        var absent = absentTeacherIds?.ToHashSet() ?? new HashSet<Guid>();

        var active = teachers
            .Where(t => t.IsActive && !excluded.Contains(t.Id) && !absent.Contains(t.Id))
            .ToList();

        if (active.Count == 0 || waitingClasses.Count == 0)
            return Array.Empty<WaitingLessonPlan>();

        // النصاب الحالي = عدد الحصص المجدولة لكل معلم
        var runningLoad = active.ToDictionary(t => t.Id, t => 0);
        foreach (var lesson in timetable.Lessons)
        {
            var id = lesson.TeacherId.Value;
            if (runningLoad.ContainsKey(id)) runningLoad[id]++;
        }

        // مشغولية التدريس
        var teaching = timetable.Lessons
            .Select(l => ((int)l.Day, l.Period.Value, l.TeacherId.Value))
            .ToHashSet();

        var plans = new List<WaitingLessonPlan>();

        foreach (var cfg in timetable.StageConfigs)
        {
            var stageWaiting = waitingClasses.Where(w => w.Stage == cfg.Stage).ToList();
            if (stageWaiting.Count == 0) continue;

            var days = cfg.WorkDays.Count > 0 ? cfg.WorkDays : new List<int> { 0, 1, 2, 3, 4, 5 };
            var periods = Math.Clamp(cfg.LessonsPerDay, 1, 12);

            foreach (var dayIndex in days.OrderBy(d => d))
            {
                var day = (DomainDayOfWeek)dayIndex;

                for (var period = 1; period <= periods; period++)
                {
                    // تُملأ كل الفترات لكل شعبة انتظار (لا تُترك فترة بلا مشرف)
                    var usedThisSlot = new HashSet<Guid>();

                    foreach (var waiting in stageWaiting)
                    {
                        var candidate = active
                            .Where(t => !teaching.Contains((dayIndex, (byte)period, t.Id)))
                            .Where(t => !usedThisSlot.Contains(t.Id))
                            .OrderBy(t => runningLoad[t.Id])
                            .ThenBy(t => t.FullName, StringComparer.CurrentCulture)
                            .FirstOrDefault();

                        if (candidate is null) continue;

                        plans.Add(new WaitingLessonPlan(day, period, cfg.Stage, waiting.WaitingClassName, candidate.Id));
                        usedThisSlot.Add(candidate.Id);
                        runningLoad[candidate.Id]++;
                    }
                }
            }
        }

        return plans;
    }
}

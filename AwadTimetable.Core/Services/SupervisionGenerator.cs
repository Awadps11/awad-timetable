using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;
using DayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>نوع الإشراف: استراحة، أو بين الحصص، أو فسحة</summary>
public enum SupervisionKind : byte
{
    Break = 0,
    Interval = 1,
}

/// <summary>تعيين إشراف واحد: قاعة في فترة معينة، بمعلم مشرف (قد يكون بلا مشرف)</summary>
public sealed record SupervisionAssignmentRow(
    Guid RoomId,
    string RoomName,
    DayOfWeek Day,
    byte Period,
    Guid? SupervisorTeacherId,
    string SupervisorName,
    SupervisionKind Kind,
    string Status);

/// <summary>
/// مولّد الإشراف (Dozor): يوزّع فترات الاستراحة على القاعات، ويكلّف كل قاعة بمعلم غير مشغول
/// (لا يُدرّس في الفترة، وليس غائباً، ولم يُكلَّف بإشراف آخر في الفترة نفسها).
/// </summary>
public static class SupervisionGenerator
{
    public static IReadOnlyList<SupervisionAssignmentRow> Generate(
        Timetable timetable,
        IReadOnlyList<Teacher> teachers,
        IReadOnlyList<Room> rooms,
        IReadOnlyCollection<Guid> absentTeacherIds,
        DateOnly date)
    {
        if (timetable == null) throw new ArgumentNullException(nameof(timetable));
        if (teachers == null) throw new ArgumentNullException(nameof(teachers));
        if (rooms == null) throw new ArgumentNullException(nameof(rooms));

        var results = new List<SupervisionAssignmentRow>();

        var dayIndex = SubstitutionGenerator.GetTimetableDayIndex(date);
        if (dayIndex > 5) return results; // الجمعة

        var day = (DayOfWeek)dayIndex;
        var absentSet = absentTeacherIds?.ToHashSet() ?? new HashSet<Guid>();

        var breakPeriods = timetable.PeriodTimes
            .Where(p => p.IsBreak && (p.Day == -1 || p.Day == dayIndex))
            .Select(p => p.Period)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        if (breakPeriods.Count == 0) return results;

        var activeRooms = rooms.Where(r => r.IsActive).OrderBy(r => r.Name).ToList();
        if (activeRooms.Count == 0) return results;

        var teacherById = teachers.ToDictionary(t => t.Id);

        // مشغول بالتدريس في هذه الفترة
        var busyTeaching = timetable.Lessons
            .Where(l => l.Day == day)
            .Select(l => (l.TeacherId.Value, l.Period.Value))
            .ToHashSet();

        var busySupervising = new HashSet<(Guid TeacherId, byte Period)>();

        foreach (var period in breakPeriods)
        {
            foreach (var room in activeRooms)
            {
                Guid? supervisorId = null;

                foreach (var teacher in teachers)
                {
                    if (!teacher.IsActive) continue;
                    if (absentSet.Contains(teacher.Id)) continue;
                    if (busyTeaching.Contains((teacher.Id, period))) continue;
                    if (busySupervising.Contains((teacher.Id, period))) continue;

                    supervisorId = teacher.Id;
                    busySupervising.Add((teacher.Id, period));
                    break;
                }

                var name = supervisorId.HasValue && teacherById.TryGetValue(supervisorId.Value, out var t)
                    ? t.FullName
                    : string.Empty;

                results.Add(new SupervisionAssignmentRow(
                    room.Id,
                    room.Name,
                    day,
                    period,
                    supervisorId,
                    name,
                    SupervisionKind.Break,
                    supervisorId.HasValue ? "معيّن" : "بدون مشرف"));
            }
        }

        return results;
    }
}

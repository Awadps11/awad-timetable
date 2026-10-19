using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Solver;

/// <summary>
/// تنفيذ مدقق القيود
/// </summary>
public class ConstraintValidator : IConstraintValidator
{
    public ValidationResult Validate(TimetableContext context, IEnumerable<IConstraint>? constraints = null)
    {
        constraints ??= ConstraintRegistry.CreateAll();

        var violations = new List<ConstraintViolation>();

        foreach (var constraint in constraints)
        {
            if (constraint.IsEnabled && constraint.IsApplicable(context))
            {
                var violation = constraint.Evaluate(context);
                if (violation != null)
                    violations.Add(violation);
            }
        }

        var hardPenalty = violations.Where(v => v.Type == ConstraintType.Hard).Sum(v => v.Penalty.Value);
        var softPenalty = violations.Where(v => v.Type != ConstraintType.Hard).Sum(v => v.Penalty.Value);

        var score = 100.0 - Math.Min(100.0, hardPenalty * 0.25) - 100.0 * (softPenalty / (softPenalty + 1000.0));
        score = Math.Clamp(score, 0.0, 100.0);

        return new ValidationResult
        {
            IsValid = violations.All(v => v.Type != ConstraintType.Hard),
            Violations = violations,
            QualityScore = new Score(score),
            TotalLessons = context.Lessons.Count,
            // التضاربات = انتهاكات صلبة فقط؛ الانتهاكات اللينة تُخصم من درجة الجودة لا من عدّاد التضاربات
            ConflictsCount = violations.Count(v => v.Type == ConstraintType.Hard)
        };
    }

    public async Task<ValidationResult> ValidateAsync(TimetableContext context, IEnumerable<IConstraint>? constraints = null, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(Validate(context, constraints));
    }

    public IReadOnlyList<ConstraintViolation> CheckHardConstraints(TimetableContext context)
    {
        var violations = new List<ConstraintViolation>();

        foreach (var constraint in ConstraintRegistry.CreateHard())
        {
            if (constraint.IsEnabled && constraint.IsApplicable(context))
            {
                var violation = constraint.Evaluate(context);
                if (violation != null)
                    violations.Add(violation);
            }
        }

        return violations;
    }

    public IReadOnlyList<ConstraintViolation> CheckSoftConstraints(TimetableContext context)
    {
        var violations = new List<ConstraintViolation>();

        foreach (var constraint in ConstraintRegistry.CreateSoft())
        {
            if (constraint.IsEnabled && constraint.IsApplicable(context))
            {
                var violation = constraint.Evaluate(context);
                if (violation != null)
                    violations.Add(violation);
            }
        }

        return violations;
    }
}

/// <summary>
/// تنفيذ كاشف التضاربات
/// </summary>
public class ConflictDetector : IConflictDetector
{
    public IReadOnlyList<Conflict> DetectConflicts(Timetable timetable)
    {
        var conflicts = new List<Conflict>();

        // تضارب المعلمين
        conflicts.AddRange(DetectTeacherConflicts(timetable));

        // تضارب الغرف
        conflicts.AddRange(DetectRoomConflicts(timetable));

        // تضارب السعة
        conflicts.AddRange(DetectCapacityConflicts(timetable));

        return conflicts;
    }

    public async Task<IReadOnlyList<Conflict>> DetectConflictsAsync(Timetable timetable, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(DetectConflicts(timetable));
    }

    public IReadOnlyList<Conflict> DetectTeacherConflicts(Timetable timetable)
    {
        var conflicts = new List<Conflict>();

        var teacherPeriods = new Dictionary<(TeacherId, DomainDayOfWeek, PeriodNumber), List<Lesson>>();

        foreach (var lesson in timetable.Lessons)
        {
            var key = (lesson.TeacherId, lesson.Day, lesson.Period);
            if (!teacherPeriods.ContainsKey(key))
                teacherPeriods[key] = new List<Lesson>();

            teacherPeriods[key].Add(lesson);
        }

        foreach (var kvp in teacherPeriods)
        {
            if (kvp.Value.Count > 1)
            {
                conflicts.Add(new Conflict
                {
                    Type = ConflictType.TeacherConflict,
                    Description = $"المعلم لديه {kvp.Value.Count} حصص متضاربة في نفس الوقت",
                    Lessons = kvp.Value,
                    Severity = ConflictSeverity.High,
                    AffectedEntities = kvp.Value.Select(l => l.Teacher?.FullName ?? "غير معروف").Distinct().ToList()
                });
            }
        }

        return conflicts;
    }

    public IReadOnlyList<Conflict> DetectRoomConflicts(Timetable timetable)
    {
        var conflicts = new List<Conflict>();

        var roomPeriods = new Dictionary<(RoomId, DomainDayOfWeek, PeriodNumber), List<Lesson>>();

        foreach (var lesson in timetable.Lessons)
        {
            if (lesson.RoomId is null || lesson.RoomId == RoomId.Empty) continue;

            var key = (lesson.RoomId, lesson.Day, lesson.Period);
            if (!roomPeriods.ContainsKey(key))
                roomPeriods[key] = new List<Lesson>();

            roomPeriods[key].Add(lesson);
        }

        foreach (var kvp in roomPeriods)
        {
            if (kvp.Value.Count > 1)
            {
                conflicts.Add(new Conflict
                {
                    Type = ConflictType.RoomConflict,
                    Description = $"الغرفة مستخدمة في {kvp.Value.Count} حصص في نفس الوقت",
                    Lessons = kvp.Value,
                    Severity = ConflictSeverity.High,
                    AffectedEntities = kvp.Value.Select(l => l.Room?.Name ?? "غير معروف").Distinct().ToList()
                });
            }
        }

        return conflicts;
    }

    public IReadOnlyList<Conflict> DetectCapacityConflicts(Timetable timetable)
    {
        var conflicts = new List<Conflict>();

        foreach (var lesson in timetable.Lessons)
        {
            if (lesson.RoomId is not null && lesson.RoomId != RoomId.Empty && lesson.Class != null && lesson.Room != null)
            {
                if (lesson.Class.StudentCount > lesson.Room.Capacity)
                {
                    conflicts.Add(new Conflict
                    {
                        Type = ConflictType.CapacityConflict,
                        Description = $"عدد الطلاب ({lesson.Class.StudentCount}) يتجاوز سعة الغرفة ({lesson.Room.Capacity})",
                        Lessons = new List<Lesson> { lesson },
                        Severity = ConflictSeverity.Medium,
                        AffectedEntities = new List<string> { lesson.Class.FullName, lesson.Room.Name }
                    });
                }
            }
        }

        return conflicts;
    }
}
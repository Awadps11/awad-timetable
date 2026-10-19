using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Common;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Constraints;

/// <summary>
/// واجهة القيد الأساسية
/// </summary>
public interface IConstraint
{
    ConstraintId Id { get; }
    string Name { get; }
    string Description { get; }
    ConstraintType Type { get; }
    ConstraintPriority Priority { get; }
    ConstraintWeight Weight { get; }
    bool IsEnabled { get; }
    bool IsHard { get; }
    bool IsApplicable(TimetableContext context);
    ConstraintViolation? Evaluate(TimetableContext context);
    void Enable();
    void Disable();
}

/// <summary>
/// سياق التقييم للقيود
/// </summary>
public sealed class TimetableContext
{
    public Timetable Timetable { get; }
    public IReadOnlyList<Lesson> Lessons { get; }
    public IReadOnlyList<Teacher> Teachers { get; }
    public IReadOnlyList<Room> Rooms { get; }
    public IReadOnlyList<Class> Classes { get; }
    public IReadOnlyList<Subject> Subjects { get; }
    public IReadOnlyList<TeacherAvailability> TeacherAvailabilities { get; }

    public TimetableContext()
        : this(new Timetable(), [], [], [], [], [], [])
    {
    }

    public TimetableContext(
        Timetable timetable,
        IReadOnlyList<Lesson> lessons,
        IReadOnlyList<Teacher> teachers,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<Class> classes,
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<TeacherAvailability> teacherAvailabilities)
    {
        Timetable = timetable;
        Lessons = lessons;
        Teachers = teachers;
        Rooms = rooms;
        Classes = classes;
        Subjects = subjects;
        TeacherAvailabilities = teacherAvailabilities;
    }
}

/// <summary>
/// انتهاك قيد
/// </summary>
public sealed record ConstraintViolation
{
    public ConstraintId ConstraintId { get; }
    public string ConstraintName { get; }
    public ConstraintType Type { get; }
    public ConstraintPriority Priority { get; }
    public string Message { get; }
    public IReadOnlyList<Lesson> AffectedLessons { get; }
    public IReadOnlyList<Entity> AffectedEntities { get; }
    public ConstraintWeight Penalty { get; }

    public ConstraintViolation(
        ConstraintId constraintId,
        string constraintName,
        ConstraintType type,
        ConstraintPriority priority,
        string message,
        IReadOnlyList<Lesson> affectedLessons,
        IReadOnlyList<Entity> affectedEntities,
        ConstraintWeight penalty)
    {
        ConstraintId = constraintId;
        ConstraintName = constraintName;
        Type = type;
        Priority = priority;
        Message = message;
        AffectedLessons = affectedLessons;
        AffectedEntities = affectedEntities;
        Penalty = penalty;
    }
}

/// <summary>
/// القيد الأساسي المجرد
/// </summary>
public abstract class Constraint : Entity<ConstraintId>, IConstraint, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public string Name { get; protected set; } = string.Empty;
    public string Description { get; protected set; } = string.Empty;
    public abstract ConstraintType Type { get; }
    public ConstraintPriority Priority { get; protected set; } = ConstraintPriority.Normal;
    public ConstraintWeight Weight { get; protected set; } = ConstraintWeight.Normal;
    public bool IsEnabled { get; private set; } = true;
    public abstract bool IsHard { get; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    protected Constraint()
    {
        Id = ConstraintId.New();
    }

    protected Constraint(string name, string description, ConstraintPriority priority, ConstraintWeight weight)
    {
        Id = ConstraintId.New();
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? string.Empty;
        Priority = priority;
        Weight = weight;
    }

    public abstract bool IsApplicable(TimetableContext context);
    public abstract ConstraintViolation? Evaluate(TimetableContext context);

    public void Enable()
    {
        IsEnabled = true;
        UpdatedAt = DateTime.UtcNow;
        OnPropertyChanged(nameof(IsEnabled));
    }

    public void Disable()
    {
        IsEnabled = false;
        UpdatedAt = DateTime.UtcNow;
        OnPropertyChanged(nameof(IsEnabled));
    }

    public void UpdatePriority(ConstraintPriority priority)
    {
        Priority = priority;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateWeight(ConstraintWeight weight)
    {
        Weight = weight;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDescription(string description)
    {
        Description = description ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// القيد الصلب الأساسي
/// </summary>
public abstract class HardConstraint : Constraint
{
    public override bool IsHard => true;
    public override bool IsApplicable(TimetableContext context) => true;

    protected HardConstraint(string name, string description, ConstraintPriority priority, ConstraintWeight weight)
        : base(name, description, priority, weight)
    {
    }
}

/// <summary>
/// القيد اللين الأساسي
/// </summary>
public abstract class SoftConstraint : Constraint
{
    public override bool IsHard => false;
    public override bool IsApplicable(TimetableContext context) => true;

    protected SoftConstraint(string name, string description, ConstraintPriority priority, ConstraintWeight weight)
        : base(name, description, priority, weight)
    {
    }
}

/// <summary>
/// قيد عدم تضارب المعلم
/// </summary>
public sealed class NoTeacherConflictConstraint : HardConstraint
{
    public override ConstraintType Type => ConstraintType.Hard;

    public NoTeacherConflictConstraint()
        : base("عدم تضارب المعلم", "لا يمكن للمعلم تدريس حصة واحدة في نفس الوقت", ConstraintPriority.Critical, ConstraintWeight.Critical)
    {
    }

    public override bool IsApplicable(TimetableContext context) => true;

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();
        var timetable = context.Timetable;
        var hasTimes = timetable.PeriodTimes.Count > 0;

        foreach (var dayGroup in context.Lessons.GroupBy(l => (l.TeacherId, l.Day)))
        {
            var lessons = dayGroup.ToList();
            if (lessons.Count < 2) continue;

            if (!hasTimes)
            {
                // بلا توقيتات: المقارنة برقم الفترة (كل المراحل بنفس الشبكة)
                foreach (var pg in lessons.GroupBy(l => l.Period.Value))
                {
                    if (pg.Count() > 1) violations.AddRange(pg);
                }
                continue;
            }

            // مع توقيتات (خاصة عند اختلاف توقيت المتوسط والثانوي): التضارب بتداخل الزمن الفعلي
            var slots = lessons.Select(l => (Lesson: l, Interval: IntervalOf(timetable, l))).ToList();

            for (var i = 0; i < slots.Count; i++)
            {
                for (var j = i + 1; j < slots.Count; j++)
                {
                    var a = slots[i];
                    var b = slots[j];
                    if (a.Interval is null || b.Interval is null)
                    {
                        // تعذّر معرفة الوقت لأحدهما: نتراجع للمقارنة برقم الفترة
                        if (a.Lesson.Period.Value == b.Lesson.Period.Value)
                        {
                            violations.Add(a.Lesson);
                            violations.Add(b.Lesson);
                        }
                        continue;
                    }

                    var (aStart, aEnd) = a.Interval.Value;
                    var (bStart, bEnd) = b.Interval.Value;
                    if (aStart < bEnd && bStart < aEnd)
                    {
                        violations.Add(a.Lesson);
                        violations.Add(b.Lesson);
                    }
                }
            }
        }

        if (violations.Count > 0)
        {
            return new ConstraintViolation(
                Id,
                Name,
                ConstraintType.Hard,
                Priority,
                $"المعلم {violations.First().Teacher?.FullName ?? "غير معروف"} لديه {violations.Count} حصص متضاربة في نفس الوقت",
                violations.Distinct().ToList(),
                violations.Cast<Entity>().Distinct().ToList(),
                Weight);
        }

        return null;
    }

    /// <summary>الفاصل الزمني لحصة بحسب مرحلة فصلها</summary>
    public static (int Start, int End)? IntervalOf(AwadTimetable.Core.Entities.Timetable timetable, Lesson lesson)
    {
        var stage = timetable.GetStageForClass(lesson.ClassId);
        return timetable.TryGetPeriodInterval(lesson.Period.Value, (int)lesson.Day, stage, out var start, out var end)
            ? (start, end)
            : null;
    }
}

/// <summary>
/// قيد عدم تضارب الغرفة
/// </summary>
public sealed class NoRoomConflictConstraint : HardConstraint
{
    public override ConstraintType Type => ConstraintType.Hard;

    public NoRoomConflictConstraint()
        : base("عدم تضارب الغرفة", "لا يمكن استخدام غرفة واحدة في نفس الوقت", ConstraintPriority.Critical, ConstraintWeight.Critical)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();

        var roomPeriods = new Dictionary<(RoomId, DomainDayOfWeek, PeriodNumber), List<Lesson>>();

        foreach (var lesson in context.Lessons)
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
                violations.AddRange(kvp.Value);
        }

        if (violations.Count > 0)
        {
            return new ConstraintViolation(
                Id,
                Name,
                ConstraintType.Hard,
                Priority,
                $"هناك {violations.Count} حصة متضاربة في نفس الغرفة والوقت",
                violations.Distinct().ToList(),
                violations.Cast<Entity>().Distinct().ToList(),
                Weight);
        }

        return null;
    }
}

/// <summary>
/// قيد توفر المعلم
/// </summary>
public sealed class TeacherAvailabilityConstraint : HardConstraint
{
    public override ConstraintType Type => ConstraintType.Hard;

    public TeacherAvailabilityConstraint()
        : base("توفر المعلم", "المعلم متاح فقط في الأوقات المحددة", ConstraintPriority.High, ConstraintWeight.High)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();

        // الإغلاق الملزم فقط (أولوية عالية)؛ الإغلاق المنخفض أولويته لينة ويُعالج في قيد آخر
        var hardBlocked = new HashSet<(TeacherId, DomainDayOfWeek, PeriodNumber)>();

        foreach (var avail in context.TeacherAvailabilities)
        {
            if (avail.IsHardBlock)
                hardBlocked.Add((avail.TeacherId, avail.Day, avail.Period));
        }

        foreach (var lesson in context.Lessons)
        {
            if (hardBlocked.Contains((lesson.TeacherId, lesson.Day, lesson.Period)))
                violations.Add(lesson);
        }

        if (violations.Count > 0)
        {
            return new ConstraintViolation(
                Id,
                Name,
                ConstraintType.Hard,
                Priority,
                $"يوجد {violations.Count} حصة في أوقات غير متاحة للمعلم",
                violations,
                violations.Cast<Entity>().ToList(),
                Weight);
        }

        return null;
    }
}

/// <summary>
/// إغلاق وقت المعلم بأولوية منخفضة (لين): يمكن تجاوزه عند الحاجة لكن يُعاقَب.
/// </summary>
public sealed class TeacherLowPriorityBlockConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public TeacherLowPriorityBlockConstraint()
        : base("تفريغ منخفض الأولوية", "تجنب جدولة المعلم في أوقات أغلقها بأولوية منخفضة (قابلة للتجاوز)", ConstraintPriority.Normal, ConstraintWeight.Normal)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var softBlocked = new HashSet<(TeacherId, DomainDayOfWeek, PeriodNumber)>();

        foreach (var avail in context.TeacherAvailabilities)
        {
            if (avail.IsSoftBlock)
                softBlocked.Add((avail.TeacherId, avail.Day, avail.Period));
        }

        if (softBlocked.Count == 0) return null;

        var violations = context.Lessons
            .Where(l => softBlocked.Contains((l.TeacherId, l.Day, l.Period)))
            .ToList();

        if (violations.Count == 0) return null;

        return ScheduleRules.Soft(
            Id, Name, Priority,
            $"يوجد {violations.Count} حصة في أوقات أغلقها المعلم بأولوية منخفضة",
            violations,
            Weight.Value);
    }
}

/// <summary>
/// قيد سعة الغرفة
/// </summary>
public sealed class RoomCapacityConstraint : HardConstraint
{
    public override ConstraintType Type => ConstraintType.Hard;

    public RoomCapacityConstraint()
        : base("سعة الغرفة", "عدد الطلاب لا يتجاوز سعة الغرفة", ConstraintPriority.High, ConstraintWeight.High)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();

foreach (var lesson in context.Lessons)
            {
                if (lesson.RoomId is not null && lesson.RoomId != RoomId.Empty)
                {
                    var room = context.Rooms.FirstOrDefault(r => r.Id == lesson.RoomId);
                if (room != null && lesson.Class?.StudentCount > room.Capacity)
                {
                    violations.Add(lesson);
                }
            }
        }

        if (violations.Count > 0)
        {
            return new ConstraintViolation(
                Id,
                Name,
                ConstraintType.Hard,
                Priority,
                $"يوجد {violations.Count} حصة يتجاوز فيها عدد الطلاب سعة الغرفة",
                violations,
                violations.Cast<Entity>().ToList(),
                Weight);
        }

        return null;
    }
}

/// <summary>
/// قيد الحد الأقصى للحصص اليومية
/// </summary>
public sealed class MaxPeriodsPerDayConstraint : HardConstraint
{
    public override ConstraintType Type => ConstraintType.Hard;

    public MaxPeriodsPerDayConstraint()
        : base("الحد الأقصى للحصص اليومية", "لا يتجاوز عدد الحصص اليومية الحد المسموح", ConstraintPriority.Normal, ConstraintWeight.Normal)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var timetable = context.Timetable;

        // لا يوجد حد يومي مُهيَّأ — لا يمكن اختراع حد ثم انتهاكه
        if (timetable.StageConfigs.Count == 0) return null;

        var violations = new List<Lesson>();

        foreach (var dayGroup in context.Lessons.GroupBy(l => (l.ClassId, l.Day)))
        {
            var cfg = timetable.StageConfigs
                .FirstOrDefault(c => c.Stage == timetable.GetStageForClass(dayGroup.Key.ClassId));

            if (cfg is not { LessonsPerDay: > 0 }) continue;
            if (dayGroup.Count() <= cfg.LessonsPerDay) continue;

            violations.AddRange(dayGroup);
        }

        if (violations.Count > 0)
        {
            return new ConstraintViolation(
                Id,
                Name,
                ConstraintType.Hard,
                Priority,
                $"يوجد {violations.Count} حصة تتجاوز الحد الأقصى اليومي المُهيَّأ",
                violations,
                violations.Cast<Entity>().ToList(),
                Weight);
        }

        return null;
    }
}

/// <summary>
/// لا حصص في فترات الاستراحة (صلب) — يشمل استراحة الغداء
/// </summary>
public sealed class NoLessonDuringBreakConstraint : HardConstraint
{
    public override ConstraintType Type => ConstraintType.Hard;

    public NoLessonDuringBreakConstraint()
        : base("لا حصص في الاستراحة", "لا تُجدَّل حصص في فترات الاستراحة (مثل استراحة الغداء)", ConstraintPriority.Critical, ConstraintWeight.High)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var timetable = context.Timetable;

        var bad = context.Lessons
            .Where(l => timetable.IsBreakPeriod(l.Period.Value, (int)l.Day, timetable.GetStageForClass(l.ClassId)))
            .ToList();

        if (bad.Count == 0) return null;

        return new ConstraintViolation(
            Id,
            Name,
            ConstraintType.Hard,
            Priority,
            $"يوجد {bad.Count} حصة مُجدولة في فترة استراحة",
            bad,
            bad.Cast<Entity>().ToList(),
            Weight);
    }
}

/// <summary>
/// الحد الأدنى لعدد الحصص اليومية للفصل (لين): إن قلّت حصص اليوم عن حد المرحلة يُعاقَب
/// </summary>
public sealed class MinPeriodsPerDayConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public MinPeriodsPerDayConstraint()
        : base("الحد الأدنى للحصص اليومية", "ألا يقل عدد حصص الفصل في اليوم عن الحد الأدنى لمرحلته", ConstraintPriority.Normal, ConstraintWeight.Normal)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var timetable = context.Timetable;
        if (timetable.StageConfigs.Count == 0) return null;

        var violations = new List<Lesson>();

        foreach (var dayGroup in context.Lessons.GroupBy(l => (l.ClassId, l.Day)))
        {
            var cfg = timetable.StageConfigs
                .FirstOrDefault(c => c.Stage == timetable.GetStageForClass(dayGroup.Key.ClassId));

            if (cfg is not { MinLessonsPerDay: > 0 }) continue;
            if (dayGroup.Count() >= cfg.MinLessonsPerDay) continue;

            violations.AddRange(dayGroup);
        }

        var affected = violations.Distinct().ToList();

        if (affected.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {affected.Count} حصة في أيام دون الحد الأدنى المطلوب للفصل",
                affected,
                Weight.Value);
        }

        return null;
    }
}

/// <summary>
/// الحصص المزدوجة عبر الاستراحة (لين): مادة بحصتين متتاليتين في يوم واحد يفصلهما استراحة تُعاقَب
/// </summary>
public sealed class DoublePeriodAcrossBreakConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public DoublePeriodAcrossBreakConstraint()
        : base("حصة مزدوجة عبر الاستراحة", "تجنب وضع حصتين لمادة واحدة حول استراحة في اليوم نفسه", ConstraintPriority.Normal, ConstraintWeight.Normal)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var timetable = context.Timetable;
        var violations = new List<Lesson>();

        foreach (var group in context.Lessons.GroupBy(l => (l.ClassId, l.SubjectId, l.Day)))
        {
            var ordered = group.OrderBy(l => l.Period.Value).ToList();
            if (ordered.Count < 2) continue;

            for (var i = 0; i < ordered.Count - 1; i++)
            {
                var a = ordered[i];
                var b = ordered[i + 1];

                var separatedByBreak = false;
                for (var p = a.Period.Value + 1; p < b.Period.Value; p++)
                {
                    if (timetable.IsBreakPeriod(p, (int)a.Day, timetable.GetStageForClass(a.ClassId)))
                    {
                        separatedByBreak = true;
                        break;
                    }
                }

                if (separatedByBreak)
                {
                    violations.Add(a);
                    violations.Add(b);
                }
            }
        }

        var affected = violations.Distinct().ToList();

        if (affected.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {affected.Count} حصة لمادة موزّعة حول استراحة في اليوم نفسه",
                affected,
                Weight.Value);
        }

        return null;
    }
}

/// <summary>
/// زمن الانتقال بين المباني (لين): حصتان متتاليتان لمعلم في مبنيين مختلفين بلا فاصل تُعاقَبان
/// </summary>
public sealed class TeacherBuildingChangeConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public TeacherBuildingChangeConstraint()
        : base("زمن الانتقال بين المباني", "تجنب انتقال المعلم بين مبنيين في فترتين متتاليتين بلا فاصل", ConstraintPriority.Normal, ConstraintWeight.Normal)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();

        foreach (var dayGroup in context.Lessons.GroupBy(l => (l.TeacherId, l.Day)))
        {
            var byPeriod = dayGroup
                .Where(l => l.Room is not null && !string.IsNullOrWhiteSpace(l.Room.Building))
                .GroupBy(l => l.Period.Value)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var entry in byPeriod)
            {
                if (!byPeriod.TryGetValue((byte)(entry.Key + 1), out var next)) continue;

                var current = entry.Value;
                if (string.Equals(current.Room!.Building, next.Room!.Building, StringComparison.OrdinalIgnoreCase))
                    continue;

                violations.Add(current);
                violations.Add(next);
            }
        }

        var affected = violations.Distinct().ToList();

        if (affected.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {affected.Count} حصة تتطلب انتقال المعلم بين مبنيين في فترة متتالية",
                affected,
                Weight.Value);
        }

        return null;
    }
}

/// <summary>
/// قواعد المستخدم الصلبة (RuleItem بأولوية Hard): حجب أوقات، منع بعد فترة، وحد يومي للمادة.
/// </summary>
public sealed class UserRulesConstraint : HardConstraint
{
    public override ConstraintType Type => ConstraintType.Hard;

    public UserRulesConstraint()
        : base("قواعد المستخدم", "احترام القواعد المخصصة الصلبة التي أضافها المستخدم أو الذكاء الاصطناعي", ConstraintPriority.Critical, ConstraintWeight.High)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var program = RuleCompiler.Compile(context.Timetable);
        if (program.IsEmpty) return null;

        var violations = new List<Lesson>();

        foreach (var lesson in context.Lessons)
        {
            foreach (var block in program.SlotBlocks)
            {
                if (block.IsHard && RuleCompiler.Blocks(block, lesson, lesson.Day, lesson.Period.Value))
                    violations.Add(lesson);
            }

            foreach (var limit in program.NoAfterPeriods)
            {
                if (limit.IsHard && lesson.Period.Value > limit.AfterPeriod && RuleCompiler.Applies(limit, lesson))
                    violations.Add(lesson);
            }
        }

        foreach (var rule in program.MaxPerDays)
        {
            if (!rule.IsHard || rule.Scope != RuleScope.Subject) continue;

            foreach (var group in context.Lessons
                         .Where(l => (Guid)l.SubjectId == rule.TargetId)
                         .GroupBy(l => (l.ClassId, l.Day)))
            {
                if (group.Count() > rule.MaxPerDay)
                    violations.AddRange(group);
            }
        }

        // حد التتابع: أقصى عدد حصص متتالية بلا فراغ
        foreach (var rule in program.MaxConsecutives)
        {
            if (!rule.IsHard) continue;

            foreach (var group in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => (Day: l.Day, Target: TargetKey(l, rule.Scope))))
            {
                var periods = group.Select(l => l.Period.Value).Distinct().OrderBy(x => x).ToList();
                var run = 0;
                var prev = int.MinValue;
                var runMax = 0;

                foreach (var p in periods)
                {
                    run = p == prev + 1 ? run + 1 : 1;
                    prev = p;
                    if (run > runMax) runMax = run;
                }

                if (runMax > rule.Max)
                    violations.AddRange(group);
            }
        }

        // حد الفجوات اليومية
        foreach (var rule in program.MaxGapsPerDays)
        {
            if (!rule.IsHard) continue;

            foreach (var group in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => (Day: l.Day, Target: TargetKey(l, rule.Scope))))
            {
                var periods = group.Select(l => l.Period.Value).Distinct().ToList();
                if (periods.Count < 2) continue;

                var gaps = (periods.Max() - periods.Min() + 1) - periods.Count;
                if (gaps > rule.Max)
                    violations.AddRange(group);
            }
        }

        // العبء اليومي: الحد الأقصى لكل يوم، والحد الأدنى على أيام العمل
        foreach (var rule in program.DailyLoads)
        {
            if (!rule.IsHard) continue;

            foreach (var group in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => (Day: l.Day, Target: TargetKey(l, rule.Scope))))
            {
                var count = group.Select(l => l.Period.Value).Distinct().Count();
                if (rule.Max is int mx && count > mx)
                    violations.AddRange(group);
            }

            if (rule.Min is int mn && mn > 0)
            {
                foreach (var day in ActiveDaysFor(rule, context))
                {
                    var dayLessons = context.Lessons
                        .Where(l => l.Day == day && RuleCompiler.Applies(rule, l))
                        .ToList();

                    if (dayLessons.Count < mn)
                        violations.AddRange(dayLessons);
                }
            }
        }

        // أقصى أيام عمل أسبوعياً
        foreach (var rule in program.MaxDaysPerWeeks)
        {
            if (!rule.IsHard) continue;

            foreach (var group in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => TargetKey(l, rule.Scope)))
            {
                if (group.Select(l => l.Day).Distinct().Count() > rule.Max)
                    violations.AddRange(group);
            }
        }

        // أدنى أيام عمل أسبوعياً
        foreach (var rule in program.MinDaysPerWeeks)
        {
            if (!rule.IsHard) continue;

            foreach (var group in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => TargetKey(l, rule.Scope)))
            {
                if (group.Select(l => l.Day).Distinct().Count() < rule.Min)
                    violations.AddRange(group);
            }
        }

        // حد تغيّر القاعة (صلب)
        foreach (var rule in program.RoomChangeLimits)
        {
            if (!rule.IsHard) continue;
            if (rule.PerDay is null && rule.PerWeek is null) continue;

            foreach (var group in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => TargetKey(l, rule.Scope)))
            {
                var weekChanges = 0;

                foreach (var dayGroup in group.GroupBy(l => l.Day))
                {
                    var ordered = dayGroup
                        .Where(l => l.RoomId is not null && (Guid)l.RoomId.Value != Guid.Empty)
                        .OrderBy(l => l.Period.Value)
                        .ToList();

                    var changes = 0;
                    for (var i = 1; i < ordered.Count; i++)
                        if (ordered[i].RoomId != ordered[i - 1].RoomId) changes++;

                    weekChanges += changes;

                    if (rule.PerDay is int pd && changes > pd)
                        violations.AddRange(dayGroup);
                }

                if (rule.PerWeek is int pw && weekChanges > pw)
                    violations.AddRange(group);
            }
        }

        // أقصى فجوات أسبوعياً: يُعاقَب ليّنًا فقط في المحرك (لا يُبلَّغ كخطر صلب)

        // أقصى امتداد لليوم
        foreach (var rule in program.MaxSpanPerDays)
        {
            if (!rule.IsHard) continue;

            foreach (var group in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => (Day: l.Day, Target: TargetKey(l, rule.Scope))))
            {
                var periods = group.Select(l => l.Period.Value).Distinct().ToList();
                if (periods.Count >= 2 && periods.Max() - periods.Min() + 1 > rule.Max)
                    violations.AddRange(group);
            }
        }

        // بدايات مبكرة: أيام تبدأ من الحصة الثانية
        foreach (var rule in program.BeginsEarlys)
        {
            if (!rule.IsHard) continue;

            foreach (var group in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => TargetKey(l, rule.Scope)))
            {
                var earlyDays = group
                    .GroupBy(l => l.Day)
                    .Count(d => d.Min(l => (int)l.Period.Value) == 2);

                if (earlyDays > rule.Max)
                    violations.AddRange(group);
            }
        }

        // علاقة زوج مواد
        foreach (var rule in program.SubjectPairs)
        {
            if (!rule.IsHard) continue;

            var aLessons = context.Lessons.Where(l => (Guid)l.SubjectId == rule.SubjectA).ToList();
            var bLessons = context.Lessons.Where(l => (Guid)l.SubjectId == rule.SubjectB).ToList();

            foreach (var classGroup in aLessons.GroupBy(l => l.ClassId))
            {
                var classId = classGroup.Key;
                var classB = bLessons.Where(l => l.ClassId == classId).ToList();
                if (classB.Count == 0) continue;

                var aByDay = classGroup.GroupBy(l => l.Day).ToDictionary(g => g.Key, g => g.ToList());
                var bByDay = classB.GroupBy(l => l.Day).ToDictionary(g => g.Key, g => g.ToList());

                foreach (var day in aByDay.Keys.Where(bByDay.ContainsKey))
                {
                    if (rule.NoSameDay)
                    {
                        violations.AddRange(aByDay[day]);
                        violations.AddRange(bByDay[day]);
                        continue;
                    }

                    if (rule.NoConsecutive)
                    {
                        var aPeriods = aByDay[day].Select(l => (int)l.Period.Value).ToHashSet();
                        foreach (var b in bByDay[day])
                        {
                            var p = (int)b.Period.Value;
                            if (aPeriods.Contains(p + 1) || aPeriods.Contains(p - 1))
                            {
                                violations.Add(b);
                                violations.AddRange(aByDay[day].Where(l =>
                                    Math.Abs((int)l.Period.Value - p) == 1));
                            }
                        }
                    }
                }
            }
        }

        // مادة بحد أقصى مرتين يومياً (مع خيار التتابع)
        foreach (var rule in program.SubjectTwicePerDays)
        {
            if (!rule.IsHard) continue;

            foreach (var group in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => (Class: l.ClassId, Day: l.Day)))
            {
                var ordered = group.OrderBy(l => l.Period.Value).ToList();
                if (ordered.Count > 2)
                {
                    violations.AddRange(ordered);
                    continue;
                }

                if (rule.MustBeConsecutive && ordered.Count == 2 &&
                    ordered[1].Period.Value != ordered[0].Period.Value + 1)
                {
                    violations.AddRange(ordered);
                }
            }
        }

        // كتلة تعليم: بلا فجوات داخلية في اليوم
        foreach (var rule in program.BlockLessons)
        {
            if (!rule.IsHard) continue;

            foreach (var classGroup in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => (Class: l.ClassId, Day: l.Day)))
            {
                var periods = classGroup.Select(l => l.Period.Value).Distinct().ToList();
                if (periods.Count < 2) continue;

                if (periods.Max() - periods.Min() + 1 != periods.Count)
                    violations.AddRange(classGroup);
            }
        }

        // عدم التداخل: لا حصتان من النطاق في نفس (يوم/حصة) للفصل نفسه
        foreach (var rule in program.NotOverlappings)
        {
            if (!rule.IsHard) continue;

            var scoped = context.Lessons.Where(l => RuleCompiler.Applies(rule, l)).ToList();
            foreach (var slot in scoped.GroupBy(l => (Class: l.ClassId, Day: l.Day, Period: l.Period)))
            {
                if (slot.Count() > 1)
                    violations.AddRange(slot);
            }
        }

        // أدنى أيام بين التكرارات
        foreach (var rule in program.MinDaysBetweens)
        {
            if (!rule.IsHard) continue;

            foreach (var classGroup in context.Lessons
                         .Where(l => RuleCompiler.Applies(rule, l))
                         .GroupBy(l => l.ClassId))
            {
                var days = classGroup.Select(l => (int)l.Day).Distinct().OrderBy(d => d).ToList();

                for (var i = 0; i < days.Count; i++)
                {
                    for (var j = i + 1; j < days.Count; j++)
                    {
                        if (days[j] - days[i] < rule.MinDays)
                        {
                            var d1 = (DomainDayOfWeek)days[i];
                            var d2 = (DomainDayOfWeek)days[j];
                            violations.AddRange(classGroup.Where(l => l.Day == d1 || l.Day == d2));
                        }
                    }
                }
            }
        }

        var affected = violations.Distinct().ToList();
        if (affected.Count == 0) return null;

        return new ConstraintViolation(
            Id, Name, ConstraintType.Hard, Priority,
            $"يوجد {affected.Count} حصة تخالف قواعد صلبة أضافها المستخدم",
            affected,
            affected.Cast<Entity>().ToList(),
            Weight);
    }

    /// <summary>معرّف الهدف (معلم/فصل/مادة/قاعة) لحصة وفق النطاق.</summary>
    private static Guid TargetKey(Lesson lesson, RuleScope scope) => scope switch
    {
        RuleScope.Teacher => (Guid)lesson.TeacherId,
        RuleScope.Class => (Guid)lesson.ClassId,
        RuleScope.Subject => (Guid)lesson.SubjectId,
        RuleScope.Room => lesson.RoomId is null ? Guid.Empty : (Guid)lesson.RoomId,
        _ => Guid.Empty,
    };

    /// <summary>أيام العمل الفعلية لقاعدة عبء يومي (فصل مباشرة، أو اتحاد فصول المعلم/المادة).</summary>
    private static List<DomainDayOfWeek> ActiveDaysFor(DailyLoadRule rule, TimetableContext context)
    {
        var timetable = context.Timetable;

        if (rule.Scope == RuleScope.Class)
            return ScheduleRules.ActiveDays(timetable, rule.TargetId);

        var days = new HashSet<DomainDayOfWeek>();
        var classIds = context.Lessons
            .Where(l => RuleCompiler.Applies(rule, l))
            .Select(l => (Guid)l.ClassId)
            .Distinct();

        foreach (var classId in classIds)
            foreach (var day in ScheduleRules.ActiveDays(timetable, classId))
                days.Add(day);

        return days.ToList();
    }
}

/// <summary>
/// قواعد مشتركة بين القيود اللينة: أيام العمل الفعلية، الهدف اليومي، وبناء الانتهاك
/// </summary>
internal static class ScheduleRules
{
    /// <summary>أيام العمل لنفس المنطق المستخدم في المحلّل (أيام مرحلة الفصل، وإلا كل الأيام عدا الجمعة)</summary>
    public static List<DomainDayOfWeek> ActiveDays(Timetable timetable, Guid classId)
    {
        var cfg = timetable.StageConfigs
            .FirstOrDefault(c => c.Stage == timetable.GetStageForClass(classId));

        var all = Enum.GetValues<DomainDayOfWeek>();

        if (cfg is { WorkDays.Count: > 0 })
            return all.Where(d => cfg.WorkDays.Contains((int)d)).ToList();

        return all.Where(d => d != DomainDayOfWeek.Friday).ToList();
    }

    /// <summary>العدد المقبول للحصص في اليوم الواحد (نفس هدف المحلّل: ⌈الإجمالي / الأيام⟩)</summary>
    public static int TargetPerDay(int lessonsCount, int activeDays)
        => activeDays <= 0 ? lessonsCount : (int)Math.Ceiling(lessonsCount / (double)activeDays);

    /// <summary>الحصص التي تتجاوز الهدف اليومي في اليوم الذي تجاوزت فيه الكمية المسموحة</summary>
    public static List<Lesson> CollectDaysOverTarget(IEnumerable<Lesson> lessons, int activeDays)
    {
        var list = lessons.ToList();
        if (list.Count == 0 || activeDays <= 0) return new List<Lesson>();

        var target = TargetPerDay(list.Count, activeDays);

        return list
            .GroupBy(l => l.Day)
            .Where(g => g.Count() > target)
            .SelectMany(g => g)
            .ToList();
    }

    /// <summary>بناء انتهاك لين: العقوبة تكبر مع عدد الحصص المتأثرة (بحد أقصى 1000)</summary>
    public static ConstraintViolation Soft(
        ConstraintId id,
        string name,
        ConstraintPriority priority,
        string message,
        IReadOnlyList<Lesson> affectedLessons,
        double baseWeight)
    {
        var count = Math.Max(1, affectedLessons.Count);
        var penalty = ConstraintWeight.Create(Math.Min(1000.0, Math.Max(1.0, baseWeight) * count));

        return new ConstraintViolation(
            id, name, ConstraintType.Soft, priority, message,
            affectedLessons.ToList(),
            affectedLessons.Cast<Entity>().ToList(),
            penalty);
    }
}

/// <summary>
/// قيد توفر المعلم (لين)
/// </summary>
public sealed class TeacherPreferenceConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public TeacherPreferenceConstraint()
        : base("تفضيل المعلم", "مراعاة تفضيلات المعلم للأيام والفترات", ConstraintPriority.Normal, ConstraintWeight.Normal)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        // "التفضيل" الوحيد القابل للقياس: الفترات التي أعلن المعلم فيها صراحةً عن توفره
        var declared = context.TeacherAvailabilities
            .Where(a => a.IsAvailable)
            .GroupBy(a => a.TeacherId)
            .ToDictionary(g => g.Key, g => g.Select(a => (a.Day, a.Period.Value)).ToHashSet());

        // معلم لم يُعلن عن أي فترة → لا يوجد تفضيل يمكن مخالفته
        if (declared.Count == 0) return null;

        var violations = new List<Lesson>();

        foreach (var lesson in context.Lessons)
        {
            if (!declared.TryGetValue(lesson.TeacherId, out var slots)) continue;
            if (!slots.Contains((lesson.Day, lesson.Period.Value)))
                violations.Add(lesson);
        }

        if (violations.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {violations.Count} حصة في فترات لم يُعلن فيها المعلم توفره",
                violations,
                ConstraintWeight.Low.Value);
        }

        return null;
    }
}

/// <summary>
/// قيد الفترات المتتالية (لين)
/// </summary>
public sealed class ConsecutivePeriodsConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public int MaxConsecutivePeriods { get; private set; } = 2;

    public ConsecutivePeriodsConstraint() : this(2) { }

    public ConsecutivePeriodsConstraint(int maxConsecutive = 2)
        : base("الفترات المتتالية", $"تجنب أكثر من {maxConsecutive} فترات متتالية للمعلم أو الفصل", ConstraintPriority.Normal, ConstraintWeight.Normal)
    {
        MaxConsecutivePeriods = Math.Max(1, maxConsecutive);
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();

        // فحص الفترات المتتالية للمعلمين
        foreach (var group in context.Lessons.GroupBy(l => (l.TeacherId, l.Day)))
            violations.AddRange(CollectOverlongRuns(group.ToList()));

        // ونفس الفحص للفصول
        foreach (var group in context.Lessons.GroupBy(l => (l.ClassId, l.Day)))
            violations.AddRange(CollectOverlongRuns(group.ToList()));

        var affected = violations.Distinct().ToList();

        if (affected.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {affected.Count} حصة ضمن سلسلة فترات متتالية تتجاوز {MaxConsecutivePeriods}",
                affected,
                Weight.Value);
        }

        return null;
    }

    /// <summary>الحصص الداخلة في سلاسل فترات متتالية أطول من المسموح في يوم واحد</summary>
    private List<Lesson> CollectOverlongRuns(List<Lesson> dayLessons)
    {
        var result = new List<Lesson>();
        var sorted = dayLessons.OrderBy(l => l.Period.Value).ToList();
        var runStart = 0;

        for (var i = 1; i <= sorted.Count; i++)
        {
            if (i < sorted.Count && sorted[i].Period.Value == sorted[i - 1].Period.Value + 1)
                continue;

            var runLength = i - runStart;
            if (runLength > MaxConsecutivePeriods)
                result.AddRange(sorted.Skip(runStart).Take(runLength));

            runStart = i;
        }

        return result;
    }
}

/// <summary>
/// قيد توزيع الحصص (لين)
/// </summary>
public sealed class DistributionConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public DistributionConstraint()
        : base("توزيع الحصص", "توزيع الحصص بالتساوي على أيام الأسبوع", ConstraintPriority.Low, ConstraintWeight.Low)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();

        foreach (var group in context.Lessons.GroupBy(l => l.ClassId))
        {
            Guid classId = group.Key;
            var activeDays = ScheduleRules.ActiveDays(context.Timetable, classId).Count;
            violations.AddRange(ScheduleRules.CollectDaysOverTarget(group, activeDays));
        }

        if (violations.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {violations.Count} حصة تتركّز في أيام مزدحمة خارج التوزيع المتوازن",
                violations,
                Weight.Value);
        }

        return null;
    }
}

/// <summary>
/// قيد الحد الأدنى للأيام في الأسبوع (لين)
/// </summary>
public sealed class MinDaysPerWeekConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public int MinDays { get; private set; } = 3;

    public MinDaysPerWeekConstraint() : this(3) { }

    public MinDaysPerWeekConstraint(int minDays = 3)
        : base("الحد الأدنى للأيام الأسبوعية", "توزيع الحصص على أيام متعددة", ConstraintPriority.Low, ConstraintWeight.Low)
    {
        MinDays = Math.Max(1, minDays);
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();
        var shortClasses = 0;

        foreach (var group in context.Lessons.GroupBy(l => l.ClassId))
        {
            if (group.Select(l => l.Day).Distinct().Count() >= MinDays) continue;

            shortClasses++;
            violations.AddRange(group);
        }

        if (violations.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {shortClasses} فصل تقل حصصه الأسبوعية عن {MinDays} أيام",
                violations,
                Weight.Value);
        }

        return null;
    }
}

/// <summary>
/// قيد تفضيل الغرفة (لين)
/// </summary>
public sealed class RoomPreferenceConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public RoomPreferenceConstraint()
        : base("تفضيل الغرفة", "مطابقة الغرفة لمتطلبات المادة (معمل/قاعة خاصة)", ConstraintPriority.Low, ConstraintWeight.Low)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        // بلا بيانات عن الغرف أو المواد لا يمكن الحكم على المطابقة
        if (context.Rooms.Count == 0 || context.Subjects.Count == 0) return null;

        var rooms = context.Rooms.ToDictionary(r => r.Id, r => r);
        var subjects = context.Subjects.ToDictionary(s => s.Id, s => s);

        var violations = new List<Lesson>();

        foreach (var lesson in context.Lessons)
        {
            if (lesson.RoomId is null || lesson.RoomId == RoomId.Empty) continue;

            Guid roomId = lesson.RoomId.Value;
            if (!rooms.TryGetValue(roomId, out var room)) continue;

            Guid subjectId = lesson.SubjectId;
            if (!subjects.TryGetValue(subjectId, out var subject)) continue;

            var roomIsSpecial = room.IsLab || room.IsLibrary || room.IsMusicRoom
                || room.IsArtRoom || room.IsGym || room.HasComputers;

            if ((subject.RequiresLab && !room.IsLab) || (subject.RequiresSpecialRoom && !roomIsSpecial))
                violations.Add(lesson);
        }

        if (violations.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {violations.Count} حصة في غرفة لا تلبي متطلبات المادة (معمل/قاعة خاصة)",
                violations,
                Weight.Value);
        }

        return null;
    }
}

/// <summary>
/// قيد توزيع المواد (لين)
/// </summary>
public sealed class SubjectDistributionConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public SubjectDistributionConstraint()
        : base("توزيع المواد", "توزيع مواد الفصل بالتساوي على الأسبوع", ConstraintPriority.Normal, ConstraintWeight.Normal)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();

        foreach (var group in context.Lessons.GroupBy(l => (l.ClassId, l.SubjectId)))
        {
            Guid classId = group.Key.ClassId;
            var activeDays = ScheduleRules.ActiveDays(context.Timetable, classId).Count;
            violations.AddRange(ScheduleRules.CollectDaysOverTarget(group, activeDays));
        }

        if (violations.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {violations.Count} حصة مادة متكررة في يوم واحد خارج توزيعها على الأسبوع",
                violations,
                Weight.Value);
        }

        return null;
    }
}

/// <summary>
/// قيد توازن عبء المعلم (لين)
/// </summary>
public sealed class TeacherWorkloadBalanceConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public TeacherWorkloadBalanceConstraint()
        : base("توازن عبء المعلم", "عدم تجاوز حدود الحصص اليومية والأسبوعية المحددة للمعلم", ConstraintPriority.Low, ConstraintWeight.Low)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        // الحدود مخزنة على كيان المعلم — بلا قائمة معلمين لا يمكن قياسها
        if (context.Teachers.Count == 0) return null;

        var teachers = context.Teachers.ToDictionary(t => t.Id, t => t);
        var violations = new List<Lesson>();

        foreach (var group in context.Lessons.GroupBy(l => l.TeacherId))
        {
            Guid teacherId = group.Key;
            if (!teachers.TryGetValue(teacherId, out var teacher)) continue;

            var lessons = group.ToList();

            foreach (var dayGroup in lessons.GroupBy(l => l.Day))
            {
                if (dayGroup.Count() > teacher.MaxPeriodsPerDay)
                    violations.AddRange(dayGroup);
            }

            if (lessons.Count > teacher.MaxPeriodsPerWeek)
                violations.AddRange(lessons);
        }

        var affected = violations.Distinct().ToList();

        if (affected.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {affected.Count} حصة تتجاوز حدود الحصص اليومية أو الأسبوعية المحددة للمعلم",
                affected,
                Weight.Value);
        }

        return null;
    }
}

/// <summary>
/// قيد عدم تكرار المادة في اليوم نفسه للفصل الواحد (لين).
/// يُعاقَب كل يوم تظهر فيه المادة أكثر من مرة لنفس الفصل.
/// </summary>
public sealed class SubjectOncePerDayConstraint : SoftConstraint
{
    public override ConstraintType Type => ConstraintType.Soft;

    public SubjectOncePerDayConstraint()
        : base("مادة واحدة يومياً", "تجنب تكرار المادة نفسها أكثر من مرة في اليوم نفسه للفصل الواحد", ConstraintPriority.Normal, ConstraintWeight.Normal)
    {
    }

    public override ConstraintViolation? Evaluate(TimetableContext context)
    {
        var violations = new List<Lesson>();

        foreach (var group in context.Lessons.GroupBy(l => (l.ClassId, l.SubjectId, l.Day)))
        {
            if (group.Count() > 1)
                violations.AddRange(group);
        }

        var affected = violations.Distinct().ToList();

        if (affected.Count > 0)
        {
            return ScheduleRules.Soft(
                Id, Name, Priority,
                $"يوجد {affected.Count} حصة لمادة تتكرر في اليوم نفسه للفصل نفسه",
                affected,
                Weight.Value);
        }

        return null;
    }
}

/// <summary>
/// سجل القيود
/// </summary>
public static class ConstraintRegistry
{
    private static readonly Dictionary<string, Func<IConstraint>> _constraints = new();

    static ConstraintRegistry()
    {
        Register<NoTeacherConflictConstraint>("no_teacher_conflict");
        Register<NoRoomConflictConstraint>("no_room_conflict");
        Register<TeacherAvailabilityConstraint>("teacher_availability");
        Register<TeacherLowPriorityBlockConstraint>("teacher_low_priority_block");
        Register<UserRulesConstraint>("user_rules");
        Register<RoomCapacityConstraint>("room_capacity");
        Register<MaxPeriodsPerDayConstraint>("max_periods_per_day");
        Register<TeacherPreferenceConstraint>("teacher_preference");
        Register<ConsecutivePeriodsConstraint>("consecutive_periods");
        Register<DistributionConstraint>("distribution");
        Register<MinDaysPerWeekConstraint>("min_days_per_week");
        Register<RoomPreferenceConstraint>("room_preference");
        Register<SubjectDistributionConstraint>("subject_distribution");
        Register<TeacherWorkloadBalanceConstraint>("teacher_workload_balance");
        Register<SubjectOncePerDayConstraint>("subject_once_per_day");
        Register<NoLessonDuringBreakConstraint>("no_lesson_during_break");
        Register<MinPeriodsPerDayConstraint>("min_periods_per_day");
        Register<DoublePeriodAcrossBreakConstraint>("double_period_across_break");
        Register<TeacherBuildingChangeConstraint>("teacher_building_change");
    }

    public static void Register<T>(string key) where T : IConstraint, new()
    {
        _constraints[key] = () => new T();
    }

    public static IConstraint Create(string key)
    {
        if (_constraints.TryGetValue(key, out var factory))
            return factory();
        throw new ArgumentException($"Unknown constraint: {key}");
    }

    public static IReadOnlyList<string> GetAllKeys() => _constraints.Keys.ToList();

    public static IEnumerable<IConstraint> CreateAll()
    {
        foreach (var factory in _constraints.Values)
            yield return factory();
    }

    public static IEnumerable<IConstraint> CreateHard()
    {
        foreach (var factory in _constraints.Values)
        {
            var constraint = factory();
            if (constraint.IsHard)
                yield return constraint;
        }
    }

    public static IEnumerable<IConstraint> CreateSoft()
    {
        foreach (var factory in _constraints.Values)
        {
            var constraint = factory();
            if (!constraint.IsHard)
                yield return constraint;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>معاملات القاعدة كما تُحفظ في RuleItem.Parameters (JSON)</summary>
public sealed class RuleParameters
{
    public List<int>? Days { get; set; }
    public List<int>? Periods { get; set; }
    public int? AfterPeriod { get; set; }
    public int? MaxPerDay { get; set; }

    /// <summary>حد الحصص المتتالية (MaxConsecutive): أقصى عدد حصص متتابعة بلا فراغ.</summary>
    public int? MaxConsecutive { get; set; }

    /// <summary>حد الفجوات اليومية (MaxGapsPerDay): أقصى عدد فراغات بين الحصص في اليوم.</summary>
    public int? MaxGapsPerDay { get; set; }

    /// <summary>الحد الأدنى للحصص في اليوم (DailyLoad).</summary>
    public int? MinPerDay { get; set; }

    /// <summary>أقصى أيام عمل أسبوعياً (MaxDaysPerWeek).</summary>
    public int? MaxDaysPerWeek { get; set; }

    /// <summary>أقصى فجوات أسبوعياً (MaxGapsPerWeek).</summary>
    public int? MaxGapsPerWeek { get; set; }

    /// <summary>أقصى امتداد لليوم (MaxSpanPerDay).</summary>
    public int? MaxSpanPerDay { get; set; }

    /// <summary>أقصى عدد أيام تبدأ من الحصة الثانية (BeginsEarly).</summary>
    public int? MaxBeginningsSecondHour { get; set; }

    /// <summary>معرّف المادة الثانية في علاقة زوج المواد (SubjectPair) أو القاعة المفضّلة.</summary>
    public Guid? SecondTargetId { get; set; }

    /// <summary>منع نفس اليوم (SubjectPair).</summary>
    public bool? NoSameDay { get; set; }

    /// <summary>منع التتابع (SubjectPair / SubjectTwicePerDay: true = يجب التتابع).</summary>
    public bool? NoConsecutive { get; set; }

    /// <summary>يجب أن تكون الحصتان متتاليتين (SubjectTwicePerDay).</summary>
    public bool? MustBeConsecutive { get; set; }

    /// <summary>أدنى عدد أيام بين تكرارات (MinDaysBetweenActivities).</summary>
    public int? MinDaysBetween { get; set; }

    /// <summary>حد تغيّر القاعة في اليوم (RoomChangeLimit).</summary>
    public int? MaxRoomChangesPerDay { get; set; }

    /// <summary>حد تغيّر القاعة في الأسبوع (RoomChangeLimit).</summary>
    public int? MaxRoomChangesPerWeek { get; set; }

    /// <summary>أدنى أيام عمل أسبوعياً (MinDaysPerWeek).</summary>
    public int? MinDaysPerWeek { get; set; }
}

/// <summary>حجب أوقات عن نطاق (معلم/فصل) — صلب أو لين</summary>
public sealed record SlotBlockRule(RuleScope Scope, Guid TargetId, IReadOnlyList<int> Days, IReadOnlyList<int> Periods, bool IsHard, string Title);

/// <summary>منع الحصص بعد فترة معينة لنطاق ما</summary>
public sealed record NoAfterPeriodRule(RuleScope Scope, Guid TargetId, int AfterPeriod, bool IsHard, string Title);

/// <summary>حد أقصى لعدد حصص مادة في اليوم الواحد</summary>
public sealed record MaxPerDayRule(RuleScope Scope, Guid TargetId, int MaxPerDay, bool IsHard, string Title);

/// <summary>أقصى عدد حصص متتابعة (بلا فراغ) لنطاق ما في اليوم</summary>
public sealed record MaxConsecutiveRule(RuleScope Scope, Guid TargetId, int Max, bool IsHard, string Title);

/// <summary>أقصى عدد فجوات (فراغات بين أول وآخر حصة) لنطاق ما في اليوم</summary>
public sealed record MaxGapsPerDayRule(RuleScope Scope, Guid TargetId, int Max, bool IsHard, string Title);

/// <summary>عبء يومي: حد أدنى/أقصى لعدد الحصص في اليوم لنطاق ما</summary>
public sealed record DailyLoadRule(RuleScope Scope, Guid TargetId, int? Min, int? Max, bool IsHard, string Title);

/// <summary>أقصى أيام عمل أسبوعياً لنطاق ما</summary>
public sealed record MaxDaysPerWeekRule(RuleScope Scope, Guid TargetId, int Max, bool IsHard, string Title);

/// <summary>أدنى أيام عمل أسبوعياً لنطاق ما</summary>
public sealed record MinDaysPerWeekRule(RuleScope Scope, Guid TargetId, int Min, bool IsHard, string Title);

/// <summary>أقصى فجوات أسبوعياً لنطاق ما</summary>
public sealed record MaxGapsPerWeekRule(RuleScope Scope, Guid TargetId, int Max, bool IsHard, string Title);

/// <summary>أقصى امتداد اليوم لنطاق ما</summary>
public sealed record MaxSpanPerDayRule(RuleScope Scope, Guid TargetId, int Max, bool IsHard, string Title);

/// <summary>بدايات مبكرة: أقصى عدد أيام تبدأ من الحصة الثانية</summary>
public sealed record BeginsEarlyRule(RuleScope Scope, Guid TargetId, int Max, bool IsHard, string Title);

/// <summary>علاقة زوج مواد: منع نفس اليوم و/أو التتابع</summary>
public sealed record SubjectPairRule(Guid SubjectA, Guid SubjectB, bool NoSameDay, bool NoConsecutive, bool IsHard, string Title);

/// <summary>مادة بحد أقصى مرتين يومياً، مع خيار إلزام التتابع</summary>
public sealed record SubjectTwicePerDayRule(Guid TargetId, bool MustBeConsecutive, bool IsHard, string Title);

/// <summary>كتلة تعليم: حصص النطاق في اليوم متصلة بلا فجوات داخلية</summary>
public sealed record BlockLessonsRule(RuleScope Scope, Guid TargetId, bool IsHard, string Title);

/// <summary>نفس البداية (نفس اليوم والحصة) لمجموعة حصص النطاق</summary>
public sealed record SameStartingTimeRule(RuleScope Scope, Guid TargetId, bool IsHard, string Title);

/// <summary>عدم التداخل: لا حصتان من النطاق في نفس (يوم/حصة)</summary>
public sealed record NotOverlappingRule(RuleScope Scope, Guid TargetId, bool IsHard, string Title);

/// <summary>أدنى عدد أيام بين تكرارات النطاق</summary>
public sealed record MinDaysBetweenRule(RuleScope Scope, Guid TargetId, int MinDays, bool IsHard, string Title);

/// <summary>قاعة مفضّلة/إلزامية لنطاق (مادة/فصل)</summary>
public sealed record PreferredRoomRule(RuleScope Scope, Guid TargetId, Guid RoomId, bool IsHard, string Title);

/// <summary>حد تغيّر القاعة للمعلم/الفصل يومياً أو أسبوعياً</summary>
public sealed record RoomChangeLimitRule(RuleScope Scope, Guid TargetId, int? PerDay, int? PerWeek, bool IsHard, string Title);

/// <summary>برنامج القواعد المُصرَّف من RuleItem — جاهز للتطبيق في المحرك والمدقق</summary>
public sealed class RuleProgram
{
    public IReadOnlyList<SlotBlockRule> SlotBlocks { get; init; } = Array.Empty<SlotBlockRule>();
    public IReadOnlyList<NoAfterPeriodRule> NoAfterPeriods { get; init; } = Array.Empty<NoAfterPeriodRule>();
    public IReadOnlyList<MaxPerDayRule> MaxPerDays { get; init; } = Array.Empty<MaxPerDayRule>();
    public IReadOnlyList<MaxConsecutiveRule> MaxConsecutives { get; init; } = Array.Empty<MaxConsecutiveRule>();
    public IReadOnlyList<MaxGapsPerDayRule> MaxGapsPerDays { get; init; } = Array.Empty<MaxGapsPerDayRule>();
    public IReadOnlyList<DailyLoadRule> DailyLoads { get; init; } = Array.Empty<DailyLoadRule>();
    public IReadOnlyList<MaxDaysPerWeekRule> MaxDaysPerWeeks { get; init; } = Array.Empty<MaxDaysPerWeekRule>();
    public IReadOnlyList<MaxGapsPerWeekRule> MaxGapsPerWeeks { get; init; } = Array.Empty<MaxGapsPerWeekRule>();
    public IReadOnlyList<MaxSpanPerDayRule> MaxSpanPerDays { get; init; } = Array.Empty<MaxSpanPerDayRule>();
    public IReadOnlyList<BeginsEarlyRule> BeginsEarlys { get; init; } = Array.Empty<BeginsEarlyRule>();
    public IReadOnlyList<SubjectPairRule> SubjectPairs { get; init; } = Array.Empty<SubjectPairRule>();
    public IReadOnlyList<SubjectTwicePerDayRule> SubjectTwicePerDays { get; init; } = Array.Empty<SubjectTwicePerDayRule>();
    public IReadOnlyList<BlockLessonsRule> BlockLessons { get; init; } = Array.Empty<BlockLessonsRule>();
    public IReadOnlyList<SameStartingTimeRule> SameStartingTimes { get; init; } = Array.Empty<SameStartingTimeRule>();
    public IReadOnlyList<NotOverlappingRule> NotOverlappings { get; init; } = Array.Empty<NotOverlappingRule>();
    public IReadOnlyList<MinDaysBetweenRule> MinDaysBetweens { get; init; } = Array.Empty<MinDaysBetweenRule>();
    public IReadOnlyList<PreferredRoomRule> PreferredRooms { get; init; } = Array.Empty<PreferredRoomRule>();
    public IReadOnlyList<RoomChangeLimitRule> RoomChangeLimits { get; init; } = Array.Empty<RoomChangeLimitRule>();
    public IReadOnlyList<MinDaysPerWeekRule> MinDaysPerWeeks { get; init; } = Array.Empty<MinDaysPerWeekRule>();

    public bool IsEmpty =>
        SlotBlocks.Count == 0 && NoAfterPeriods.Count == 0 && MaxPerDays.Count == 0
        && MaxConsecutives.Count == 0 && MaxGapsPerDays.Count == 0 && DailyLoads.Count == 0
        && MaxDaysPerWeeks.Count == 0 && MaxGapsPerWeeks.Count == 0 && MaxSpanPerDays.Count == 0
        && BeginsEarlys.Count == 0 && SubjectPairs.Count == 0 && SubjectTwicePerDays.Count == 0
        && BlockLessons.Count == 0 && SameStartingTimes.Count == 0 && NotOverlappings.Count == 0
        && MinDaysBetweens.Count == 0 && PreferredRooms.Count == 0 && RoomChangeLimits.Count == 0
        && MinDaysPerWeeks.Count == 0;
}

/// <summary>
/// يُصرّف قواعد المستخدم/الذكاء الاصطناعي (RuleItem) إلى قيود قابلة للتطبيق:
/// - Availability (نطاق معلم): حجب أوقات (Days × Periods).
/// - EndTime (أي نطاق): منع الحصص بعد فترة معينة.
/// - SubjectDistribution (نطاق مادة): حد أقصى لعدد الحصص يومياً.
/// Priority = Hard → قيد صلب، وإلا لين. Priority = Ignore → يُتجاهل.
/// </summary>
public static class RuleCompiler
{
    public static RuleProgram Compile(Timetable? timetable)
    {
        if (timetable?.Rules is null || timetable.Rules.Count == 0)
            return new RuleProgram();

        var blocks = new List<SlotBlockRule>();
        var afterPeriods = new List<NoAfterPeriodRule>();
        var maxPerDay = new List<MaxPerDayRule>();
        var maxConsecutive = new List<MaxConsecutiveRule>();
        var maxGapsPerDay = new List<MaxGapsPerDayRule>();
        var dailyLoads = new List<DailyLoadRule>();
        var maxDaysPerWeek = new List<MaxDaysPerWeekRule>();
        var maxGapsPerWeek = new List<MaxGapsPerWeekRule>();
        var maxSpanPerDay = new List<MaxSpanPerDayRule>();
        var beginsEarly = new List<BeginsEarlyRule>();
        var subjectPairs = new List<SubjectPairRule>();
        var subjectTwicePerDay = new List<SubjectTwicePerDayRule>();
        var blockLessons = new List<BlockLessonsRule>();
        var sameStartingTimes = new List<SameStartingTimeRule>();
        var notOverlappings = new List<NotOverlappingRule>();
        var minDaysBetweens = new List<MinDaysBetweenRule>();
        var preferredRooms = new List<PreferredRoomRule>();
        var roomChangeLimits = new List<RoomChangeLimitRule>();
        var minDaysPerWeek = new List<MinDaysPerWeekRule>();

        foreach (var rule in timetable.Rules)
        {
            if (!rule.IsEnabled) continue;
            if (rule.Priority == RulePriority.Ignore) continue;
            if (rule.TargetId is null || rule.TargetId == Guid.Empty) continue;

            var p = ParseParameters(rule.Parameters);
            if (p is null) continue;

            var isHard = rule.Priority == RulePriority.Hard;
            var title = string.IsNullOrWhiteSpace(rule.Title) ? rule.Kind.ToString() : rule.Title;

            switch (rule.Kind)
            {
                case RuleKind.Availability:
                case RuleKind.TeacherLink:
                    if (p.Days is { Count: > 0 } || p.Periods is { Count: > 0 })
                    {
                        blocks.Add(new SlotBlockRule(
                            rule.Scope,
                            rule.TargetId.Value,
                            p.Days ?? new List<int>(),
                            p.Periods ?? new List<int>(),
                            isHard,
                            title));
                    }
                    break;

                case RuleKind.EndTime:
                    if (p.AfterPeriod is int ap && ap is >= 1 and <= 12)
                        afterPeriods.Add(new NoAfterPeriodRule(rule.Scope, rule.TargetId.Value, ap, isHard, title));
                    break;

                case RuleKind.SubjectDistribution:
                    if (p.MaxPerDay is int mp && mp is >= 1 and <= 12)
                        maxPerDay.Add(new MaxPerDayRule(rule.Scope, rule.TargetId.Value, mp, isHard, title));
                    break;

                case RuleKind.MaxConsecutive:
                    if (p.MaxConsecutive is int mc && mc is >= 1 and <= 12)
                        maxConsecutive.Add(new MaxConsecutiveRule(rule.Scope, rule.TargetId.Value, mc, isHard, title));
                    break;

                case RuleKind.MaxGapsPerDay:
                    if (p.MaxGapsPerDay is int mg && mg is >= 0 and <= 12)
                        maxGapsPerDay.Add(new MaxGapsPerDayRule(rule.Scope, rule.TargetId.Value, mg, isHard, title));
                    break;

                case RuleKind.DailyLoad:
                    if (p.MinPerDay is >= 1 and <= 12 || p.MaxPerDay is >= 1 and <= 12)
                    {
                        var min = p.MinPerDay is >= 1 and <= 12 ? p.MinPerDay : null;
                        var max = p.MaxPerDay is >= 1 and <= 12 ? p.MaxPerDay : null;
                        dailyLoads.Add(new DailyLoadRule(rule.Scope, rule.TargetId.Value, min, max, isHard, title));
                    }
                    break;

                case RuleKind.MaxDaysPerWeek:
                    if (p.MaxDaysPerWeek is int mdw && mdw is >= 1 and <= 7)
                        maxDaysPerWeek.Add(new MaxDaysPerWeekRule(rule.Scope, rule.TargetId.Value, mdw, isHard, title));
                    break;

                case RuleKind.MaxGapsPerWeek:
                    if (p.MaxGapsPerWeek is int mgw && mgw is >= 0 and <= 60)
                        maxGapsPerWeek.Add(new MaxGapsPerWeekRule(rule.Scope, rule.TargetId.Value, mgw, isHard, title));
                    break;

                case RuleKind.MaxSpanPerDay:
                    if (p.MaxSpanPerDay is int msp && msp is >= 1 and <= 12)
                        maxSpanPerDay.Add(new MaxSpanPerDayRule(rule.Scope, rule.TargetId.Value, msp, isHard, title));
                    break;

                case RuleKind.BeginsEarly:
                    if (p.MaxBeginningsSecondHour is int be && be is >= 0 and <= 7)
                        beginsEarly.Add(new BeginsEarlyRule(rule.Scope, rule.TargetId.Value, be, isHard, title));
                    break;

                case RuleKind.SubjectPair:
                    if (p.SecondTargetId is Guid sb && sb != Guid.Empty)
                        subjectPairs.Add(new SubjectPairRule(
                            rule.TargetId.Value, sb,
                            p.NoSameDay ?? true,
                            p.NoConsecutive ?? true,
                            isHard, title));
                    break;

                case RuleKind.SubjectTwicePerDay:
                    subjectTwicePerDay.Add(new SubjectTwicePerDayRule(
                        rule.TargetId.Value, p.MustBeConsecutive ?? false, isHard, title));
                    break;

                case RuleKind.BlockLessons:
                    blockLessons.Add(new BlockLessonsRule(rule.Scope, rule.TargetId.Value, isHard, title));
                    break;

                case RuleKind.SameStartingTime:
                    sameStartingTimes.Add(new SameStartingTimeRule(rule.Scope, rule.TargetId.Value, isHard, title));
                    break;

                case RuleKind.NotOverlapping:
                    notOverlappings.Add(new NotOverlappingRule(rule.Scope, rule.TargetId.Value, isHard, title));
                    break;

                case RuleKind.MinDaysBetweenActivities:
                    if (p.MinDaysBetween is int mdb && mdb is >= 1 and <= 7)
                        minDaysBetweens.Add(new MinDaysBetweenRule(rule.Scope, rule.TargetId.Value, mdb, isHard, title));
                    break;

                case RuleKind.PreferredRoom:
                    if (p.SecondTargetId is Guid roomId && roomId != Guid.Empty)
                        preferredRooms.Add(new PreferredRoomRule(rule.Scope, rule.TargetId.Value, roomId, isHard, title));
                    break;

                case RuleKind.RoomChangeLimit:
                    if (p.MaxRoomChangesPerDay is >= 0 and <= 12 || p.MaxRoomChangesPerWeek is >= 0 and <= 60)
                    {
                        var pd = p.MaxRoomChangesPerDay is >= 0 and <= 12 ? p.MaxRoomChangesPerDay : null;
                        var pw = p.MaxRoomChangesPerWeek is >= 0 and <= 60 ? p.MaxRoomChangesPerWeek : null;
                        roomChangeLimits.Add(new RoomChangeLimitRule(rule.Scope, rule.TargetId.Value, pd, pw, isHard, title));
                    }
                    break;

                case RuleKind.MinDaysPerWeek:
                    if (p.MinDaysPerWeek is int md && md is >= 1 and <= 7)
                        minDaysPerWeek.Add(new MinDaysPerWeekRule(rule.Scope, rule.TargetId.Value, md, isHard, title));
                    break;
            }
        }

        return new RuleProgram
        {
            SlotBlocks = blocks,
            NoAfterPeriods = afterPeriods,
            MaxPerDays = maxPerDay,
            MaxConsecutives = maxConsecutive,
            MaxGapsPerDays = maxGapsPerDay,
            DailyLoads = dailyLoads,
            MaxDaysPerWeeks = maxDaysPerWeek,
            MaxGapsPerWeeks = maxGapsPerWeek,
            MaxSpanPerDays = maxSpanPerDay,
            BeginsEarlys = beginsEarly,
            SubjectPairs = subjectPairs,
            SubjectTwicePerDays = subjectTwicePerDay,
            BlockLessons = blockLessons,
            SameStartingTimes = sameStartingTimes,
            NotOverlappings = notOverlappings,
            MinDaysBetweens = minDaysBetweens,
            PreferredRooms = preferredRooms,
            RoomChangeLimits = roomChangeLimits,
            MinDaysPerWeeks = minDaysPerWeek,
        };
    }

    public static RuleParameters? ParseParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<RuleParameters>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>هل تنطبق القاعدة على هذه الحصة؟ (النطاق + المعرّف)</summary>
    public static bool Applies(SlotBlockRule rule, Lesson lesson)
        => rule.Scope switch
        {
            RuleScope.Teacher => (Guid)lesson.TeacherId == rule.TargetId,
            RuleScope.Class => (Guid)lesson.ClassId == rule.TargetId,
            RuleScope.Subject => (Guid)lesson.SubjectId == rule.TargetId,
            RuleScope.Room => lesson.RoomId is not null && (Guid)lesson.RoomId == rule.TargetId,
            _ => false,
        };

    public static bool Applies(NoAfterPeriodRule rule, Lesson lesson)
        => rule.Scope switch
        {
            RuleScope.School => true,
            RuleScope.Teacher => (Guid)lesson.TeacherId == rule.TargetId,
            RuleScope.Class => (Guid)lesson.ClassId == rule.TargetId,
            RuleScope.Subject => (Guid)lesson.SubjectId == rule.TargetId,
            RuleScope.Room => lesson.RoomId is not null && (Guid)lesson.RoomId == rule.TargetId,
            _ => false,
        };

    public static bool Applies(MaxPerDayRule rule, Lesson lesson)
        => rule.Scope switch
        {
            RuleScope.Subject => (Guid)lesson.SubjectId == rule.TargetId,
            RuleScope.Class => (Guid)lesson.ClassId == rule.TargetId,
            RuleScope.Teacher => (Guid)lesson.TeacherId == rule.TargetId,
            _ => false,
        };

    /// <summary>هل ينطبق النطاق + المعرّف على الحصة؟</summary>
    public static bool AppliesScope(RuleScope scope, Guid targetId, Lesson lesson)
        => scope switch
        {
            RuleScope.School => true,
            RuleScope.Teacher => (Guid)lesson.TeacherId == targetId,
            RuleScope.Class => (Guid)lesson.ClassId == targetId,
            RuleScope.Subject => (Guid)lesson.SubjectId == targetId,
            RuleScope.Room => lesson.RoomId is not null && (Guid)lesson.RoomId == targetId,
            _ => false,
        };

    public static bool Applies(MaxConsecutiveRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(MaxGapsPerDayRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(DailyLoadRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(MaxDaysPerWeekRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(MinDaysPerWeekRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(MaxGapsPerWeekRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(MaxSpanPerDayRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(BeginsEarlyRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(BlockLessonsRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(SameStartingTimeRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(NotOverlappingRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(MinDaysBetweenRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(RoomChangeLimitRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    public static bool Applies(PreferredRoomRule rule, Lesson lesson) => AppliesScope(rule.Scope, rule.TargetId, lesson);

    /// <summary>هل الحصة من إحدى مادتي الزوج؟</summary>
    public static bool Applies(SubjectPairRule rule, Lesson lesson)
        => (Guid)lesson.SubjectId == rule.SubjectA || (Guid)lesson.SubjectId == rule.SubjectB;

    /// <summary>هل الحصة من مادة قاعدة «مرتين يومياً»؟</summary>
    public static bool Applies(SubjectTwicePerDayRule rule, Lesson lesson)
        => (Guid)lesson.SubjectId == rule.TargetId;

    /// <summary>هل يُحجب هذا الموضع (يوم/فترة) عن الحصة وفق قاعدة الحجب؟</summary>
    public static bool Blocks(SlotBlockRule rule, Lesson lesson, DomainDayOfWeek day, int period)
    {
        if (!Applies(rule, lesson)) return false;
        var dayOk = rule.Days.Count == 0 || rule.Days.Contains((int)day);
        var periodOk = rule.Periods.Count == 0 || rule.Periods.Contains(period);
        return dayOk && periodOk;
    }
}

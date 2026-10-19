using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;
using DayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Constants;

/// <summary>
/// قوالب توقيت جاهزة لنماذج المدارس:
/// - مشترك (متوسط +ثانوي): لكل مرحلة توقيتها ومكان استراحتها
/// - متوسط فقط / ثانوي فقط: توقيت موحّد
/// </summary>
public static class SchoolTimingPresets
{
    public const string MiddleStage = "متوسط";
    public const string SecondaryStage = "ثانوي";

    /// <summary>مشترك: متوسط (7×45 استراحة بعد الحصة الثانية كل الأيام) + ثانوي (الأحد/الاثنين 7×45 استراحة بعد الثالثة، الثلاثاء-الخميس 6×50 استراحة بعد الثالثة)</summary>
    public static List<PeriodTime> SharedMiddleSecondary()
    {
        var list = new List<PeriodTime>();

        // متوسط: يوم دراسي موحّد (السبت-الخميس) — استراحة 20 دقيقة بعد الحصة الثانية
        list.AddRange(MiddlePattern(day: -1));

        // ثانوي الأحد (1) والاثنين (2): 7×45 استراحة بعد الثالثة
        foreach (var day in new[] { (int)DayOfWeek.Sunday, (int)DayOfWeek.Monday })
            list.AddRange(SecondaryPatternFull(day));

        // ثانوي الثلاثاء-الخميس (3-5): 6×50 استراحة بعد الثالثة
        foreach (var day in new[] { (int)DayOfWeek.Tuesday, (int)DayOfWeek.Wednesday, (int)DayOfWeek.Thursday })
            list.AddRange(SecondaryPatternShort(day));

        return list;
    }

    /// <summary>متوسط فقط: 7×45 كل الأيام، استراحة بعد الحصة الثانية</summary>
    public static List<PeriodTime> MiddleOnly() => MiddlePattern(day: -1).ToList();

    /// <summary>ثانوي فقط: 7×45 كل الأيام، استراحة بعد الحصة الثالثة</summary>
    public static List<PeriodTime> SecondaryOnly() => SecondaryPatternFull(day: -1).ToList();

    /// <summary>افتراضي عام: 8 فترات متساوية من 07:30 (بدون تمييز مرحلة/يوم)</summary>
    public static List<PeriodTime> DefaultGeneral()
    {
        var list = new List<PeriodTime>();
        var start = 7 * 60 + 30;
        for (byte p = 1; p <= 8; p++)
        {
            list.Add(new PeriodTime { Period = p, StartMinutes = start, EndMinutes = start + 45, Day = -1, Stage = null });
            start += 50;
        }
        return list;
    }

    /// <summary>السبت-الخميس (قيم enum DayOfWeek)</summary>
    public static readonly IReadOnlyList<int> DefaultWorkDays = new[] { 0, 1, 2, 3, 4, 5 };

    /// <summary>
    /// إعدادات أيام العمل/الحصص اليومية المقابلة لنمط التوقيت المختار:
    /// مشترك: متوسط+ثانوي 7 حصص/يوم (سبت-خميس) — الثلاثاء-الخميس يقتصر فعلياً على 6 عبر توقيت الفترات.
    /// متوسط/ثانوي فقط: مرحلة واحدة. افتراضي عام: بدون قيود (سلوك الجدول القديم).
    /// </summary>
    public static List<StageScheduleConfig> DefaultStageConfigs(string mode)
    {
        return mode switch
        {
            "shared" => new List<StageScheduleConfig>
            {
                new StageScheduleConfig { Stage = MiddleStage, WorkDays = DefaultWorkDays.ToList(), LessonsPerDay = 7 },
                new StageScheduleConfig { Stage = SecondaryStage, WorkDays = DefaultWorkDays.ToList(), LessonsPerDay = 7 },
            },
            "middle" => new List<StageScheduleConfig>
            {
                new StageScheduleConfig { Stage = MiddleStage, WorkDays = DefaultWorkDays.ToList(), LessonsPerDay = 7 },
            },
            "secondary" => new List<StageScheduleConfig>
            {
                new StageScheduleConfig { Stage = SecondaryStage, WorkDays = DefaultWorkDays.ToList(), LessonsPerDay = 7 },
            },
            _ => new List<StageScheduleConfig>(),
        };
    }

    // ===== internal builders =====

    /// <summary>متوسط: 7 فترات 45 دقيقة — فسحة أولى بعد الثانية وفسحة ثانية بعد الرابعة (10 دقائق لكل فسحة)</summary>
    private static IEnumerable<PeriodTime> MiddlePattern(int day)
    {
        const int lesson = 45;
        const int breakMin = 10;
        var t = 7 * 60;
        for (byte p = 1; p <= 7; p++)
        {
            yield return new PeriodTime { Period = p, StartMinutes = t, EndMinutes = t + lesson, Day = day, Stage = MiddleStage };
            t += lesson;
            // فسحة أولى بعد الحصة الثانية، وفسحة ثانية بعد الحصة الرابعة (10 دقائق لكل فسحة)
            if (p == 2 || p == 4) t += breakMin;
        }
    }

    /// <summary>ثانوي يوم كامل: 7×45 — استراحة بعد الثالثة — ينتهي 12:35</summary>
    private static IEnumerable<PeriodTime> SecondaryPatternFull(int day)
    {
        const int lesson = 45;
        const int breakMin = 20;
        var t = 7 * 60;
        for (byte p = 1; p <= 7; p++)
        {
            yield return new PeriodTime { Period = p, StartMinutes = t, EndMinutes = t + lesson, Day = day, Stage = SecondaryStage };
            t += lesson;
            if (p == 3) t += breakMin; // استراحة بعد الحصة الثالثة
        }
    }

    /// <summary>ثانوي اليوم القصير (الثلاثاء-الخميس): 6×50 — استراحة بعد الثالثة — ينتهي 12:20</summary>
    private static IEnumerable<PeriodTime> SecondaryPatternShort(int day)
    {
        const int lesson = 50;
        const int breakMin = 20;
        var t = 7 * 60;
        for (byte p = 1; p <= 6; p++)
        {
            yield return new PeriodTime { Period = p, StartMinutes = t, EndMinutes = t + lesson, Day = day, Stage = SecondaryStage };
            t += lesson;
            if (p == 3) t += breakMin;
        }
    }
}

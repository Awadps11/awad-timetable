using System;

namespace AwadTimetable.Core.Entities;

/// <summary>تصنيف القاعدة/البند.</summary>
public enum RuleKind
{
    /// <summary>وقت انتهاء</summary>
    EndTime = 0,
    /// <summary>ربط معلمين</summary>
    TeacherLink = 1,
    /// <summary>توفر معلم</summary>
    Availability = 2,
    /// <summary>صعوبة مادة / توزيع</summary>
    SubjectDistribution = 3,
    /// <summary>حد الحصص المتتالية: منع أكثر من N حصة متتابعة بلا فراغ</summary>
    MaxConsecutive = 4,
    /// <summary>فجوات يومية: حد أقصى لعدد الفجوات (الفراغات بين الحصص) في اليوم</summary>
    MaxGapsPerDay = 5,
    /// <summary>عبء يومي: حد أدنى/أقصى لعدد الحصص في اليوم</summary>
    DailyLoad = 6,
    /// <summary>أقصى أيام عمل في الأسبوع (معلم/فصل/مادة)</summary>
    MaxDaysPerWeek = 7,
    /// <summary>أقصى فجوات في الأسبوع</summary>
    MaxGapsPerWeek = 8,
    /// <summary>أقصى امتداد اليوم (من أول حصة إلى آخرها)</summary>
    MaxSpanPerDay = 9,
    /// <summary>بدايات مبكرة: أقصى عدد أيام يبدأ فيها اليوم من الحصة الثانية</summary>
    BeginsEarly = 10,
    /// <summary>علاقة زوج مواد: منع نفس اليوم و/أو التتابع</summary>
    SubjectPair = 11,
    /// <summary>مادة مرتين يومياً: بحد أقصى مرتين مع خيار التتابع</summary>
    SubjectTwicePerDay = 12,
    /// <summary>كتلة تعليم: حصص النطاق في أي يوم متصلة بلا فجوات داخلية</summary>
    BlockLessons = 13,
    /// <summary>نفس البداية (نفس اليوم والحصة) لمجموعة حصص</summary>
    SameStartingTime = 14,
    /// <summary>عدم التداخل: حصص النطاق لا تتقاطع في (يوم/حصة)</summary>
    NotOverlapping = 15,
    /// <summary>أدنى عدد أيام بين تكرارات نفس المادة/النطاق</summary>
    MinDaysBetweenActivities = 16,
    /// <summary>قاعة مفضّلة/إلزامية لنطاق (مادة/فصل)</summary>
    PreferredRoom = 17,
    /// <summary>حد تغيّر القاعة للمعلم/الفصل يومياً أو أسبوعياً</summary>
    RoomChangeLimit = 18,
    /// <summary>أدنى أيام عمل في الأسبوع (معلم/فصل/مادة)</summary>
    MinDaysPerWeek = 19,
    /// <summary>قاعدة عامة</summary>
    General = 99
}

/// <summary>الكيان/العلاقة التي تنطبق عليها القاعدة.</summary>
public enum RuleScope
{
    School = 0,
    Teacher = 1,
    Class = 2,
    Subject = 3,
    Room = 4
}

/// <summary>أولوية القاعدة عند توليد الجدول.</summary>
public enum RulePriority
{
    /// <summary>صلبة: لا يجوز خرقها إطلاقًا</summary>
    Hard = 0,
    /// <summary>عالية: تُحاول بأولوية</summary>
    High = 1,
    /// <summary>متوسطة: افتراضية للقواعد الجديدة والقادمة من المساعد</summary>
    Medium = 2,
    /// <summary>منخفضة: تُطبَّق إن أمكن دون إفساد غيرها</summary>
    Low = 3,
    /// <summary>تجاهل: يُبرزها للمستخدم لكن لا تُطبَّق عند التوليد</summary>
    Ignore = 4
}

/// <summary>
/// بند قاعدة/سجل قابل للتحكم: يصف قاعدة (وقت انتهاء، ربط معلمين، …) أو أي سجل أنشأه المساعد الذكي.
/// يُحفظ مع ملف الجدول (غير مخزّن في مخطط قاعدة البيانات) في عمود RulesJson.
/// </summary>
public class RuleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>نوع القاعدة/البند.</summary>
    public RuleKind Kind { get; set; } = RuleKind.General;

    /// <summary>الكيان الذي تنطبق عليه القاعدة.</summary>
    public RuleScope Scope { get; set; } = RuleScope.School;

    /// <summary>معرّف الهدف (معلم/فصل/مادة/قاعة) — فارغ للقواعد العامة.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>عنوان/وصف مختصر للبند.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>تفاصيل/معاملات حرة (نص أو JSON).</summary>
    public string? Parameters { get; set; }

    /// <summary>الأولوية عند التوليد (افتراضي: متوسطة).</summary>
    public RulePriority Priority { get; set; } = RulePriority.Medium;

    /// <summary>هل البند مُفعَّل؟</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>المصدر: user (المستخدم) أو ai (المساعد الذكي).</summary>
    public string Source { get; set; } = "user";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>ملاحظات حرة.</summary>
    public string? Notes { get; set; }
}

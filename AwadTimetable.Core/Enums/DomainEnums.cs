namespace AwadTimetable.Core.Enums;

/// <summary>
/// أيام الأسبوع
/// </summary>
public enum DayOfWeek : byte
{
    /// <summary>السبت</summary>
    Saturday = 0,

    /// <summary>الأحد</summary>
    Sunday = 1,

    /// <summary>الاثنين</summary>
    Monday = 2,

    /// <summary>الثلاثاء</summary>
    Tuesday = 3,

    /// <summary>الأربعاء</summary>
    Wednesday = 4,

    /// <summary>الخميس</summary>
    Thursday = 5,

    /// <summary>الجمعة</summary>
    Friday = 6,
}

/// <summary>
/// نوع الحصة
/// </summary>
public enum LessonType : byte
{
    /// <summary>عادية</summary>
    Regular = 0,

    /// <summary>مختبر</summary>
    Lab = 1,

    /// <summary>نشاط</summary>
    Activity = 2,

    /// <summary>امتحان</summary>
    Exam = 3,

    /// <summary>استراحة</summary>
    Break = 4,
}

/// <summary>
/// نوع القيد
/// </summary>
public enum ConstraintType : byte
{
    /// <summary>قيد صلب (لا يمكن كسره)</summary>
    Hard = 0,

    /// <summary>قيد لين (مع عقوبة)</summary>
    Soft = 1,
}

/// <summary>
/// أولوية القيد
/// </summary>
public enum ConstraintPriority : byte
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3,
}

/// <summary>
/// حالة الجدول
/// </summary>
public enum TimetableStatus : byte
{
    Draft = 0,
    Generated = 1,
    Published = 2,
    Archived = 3,
}

/// <summary>
/// أولوية إغلاق وقت المعلم (تفريغ/ندب):
/// Low = يمكن التجاوز عند الضرورة (لين)، High = ملزم ولا يُتجاوز (صلب).
/// </summary>
public enum AvailabilityPriority : byte
{
    Low = 0,
    High = 1,
}

/// <summary>نوع المدرسة: يحدد قالب الفسح وزمن الحصص الافتراضي</summary>
public enum SchoolType : byte
{
    Primary = 0,
    Middle = 1,
    Secondary = 2,
}

/// <summary>
/// محرك توليد الجدول الذي يختاره المستخدم عند إنشاء الجدول
/// </summary>
public enum GeneratorEngine : byte
{
    /// <summary>CP-SAT (OR-Tools): محرك افتراضي بقيود صلبة ولينة</summary>
    CpSat = 0,

    /// <summary>FET عبر سطر الأوامر: محرك خارجي يعمل بالكامل</summary>
    Fet = 1,

    /// <summary>aSc Timetables: يُصدَّر الجدول إلى aSc ليُولَّد داخل واجهته ثم يُستورد الناتج</summary>
    Asc = 2,
}

/// <summary>
/// نوع اليوم
/// </summary>
public enum DayType : byte
{
    Regular = 0,
    Holiday = 1,
    Exam = 2,
    ShortDay = 3,
}

/// <summary>
/// نوع التصدير
/// </summary>
public enum ExportFormat : byte
{
    Excel = 0,
    CSV = 1,
    PDF = 2,
    XML = 3,
    JSON = 4,
    iCal = 5,
    HTML = 6,
    MadrasatiCSV = 7,
    MadrasatiExcel = 8,
    NoorXML = 9,
    NoorCSV = 10,

    /// <summary>aSc Timetables XML — قابل للفتح مباشرة في برنامج aSc Timetables</summary>
    AscXml = 11,
}

/// <summary>
/// نوع الاستيراد
/// </summary>
public enum ImportFormat : byte
{
    Excel = 0,
    CSV = 1,
    JSON = 2,
    Image = 3,

    /// <summary>aSc Timetables XML</summary>
    AscXml = 4,
}

/// <summary>
/// نوع القيد الصلب
/// </summary>
public enum HardConstraintType : byte
{
    NoTeacherConflict = 0,
    NoRoomConflict = 1,
    TeacherAvailability = 2,
    RoomCapacity = 3,
    MaxPeriodsPerDay = 4,
    ConsecutivePeriodsLimit = 5,
    ForbiddenSlots = 6,
    RequiredSlots = 7,
}

/// <summary>
/// نوع القيد اللين
/// </summary>
public enum SoftConstraintType : byte
{
    TeacherPreference = 0,
    ConsecutivePeriods = 1,
    Distribution = 2,
    MinDaysPerWeek = 3,
    RoomPreference = 4,
    SubjectDistribution = 5,
    TeacherWorkloadBalance = 6,
}
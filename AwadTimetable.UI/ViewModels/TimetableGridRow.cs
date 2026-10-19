using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;
using AwadTimetable.Core.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// وضع عرض الجدول الرئيسي (بأسلوب aSc: مصفوفة المدرسة أو جدول كيان واحد)
/// </summary>
public enum TimetableViewMode
{
    SchoolMatrix,
    ByClass,
    ByTeacher,
    ByRoom,
    BySubject,
    ByStudent,
    ByGroup
}

/// <summary>
/// خيار كيان في محدد العرض (فصل / معلم / قاعة / مادة)
/// </summary>
public sealed record EntityOption(Guid Id, string Name);

/// <summary>
/// سياق عرض بطاقة الحصة: أي كيان نعرض جدوله الآن (يحدد السطر الثاني والثالث في البطاقة)
/// </summary>
public enum ChipDisplayContext
{
    School,
    Class,
    Teacher,
    Room,
    Subject,
    Student,
    Group
}

/// <summary>
/// صف في شبكة الجدول للعرض
/// </summary>
public class TimetableGridRow : INotifyPropertyChanged
{
    public PeriodNumber Period { get; set; } = PeriodNumber.Create(1);
    public string PeriodName { get; set; } = "الحصة الأولى";
    public string PeriodLabel => $"ح{Period.Value}";

    /// <summary>معرف الفصل الذي ينتمي إليه هذا الصف (مصفوفة المدرسة)</summary>
    public ClassId ClassId { get; set; } = ClassId.Empty;

    /// <summary>اسم الفصل (يظهر فقط في أول صف لكل فصل)</summary>
    public string ClassName { get; set; } = "";

    /// <summary>true = أول صف لفصل جديد (خط فاصل أسمح)</summary>
    public bool IsClassGroupStart { get; set; }

    /// <summary>حصص خلية (اليوم × الفترة) — تُعرض جميعها في قالب عمود اليوم</summary>
    public System.Collections.ObjectModel.ObservableCollection<ScheduledLessonViewModel> SaturdayLessons { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ScheduledLessonViewModel> SundayLessons { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ScheduledLessonViewModel> MondayLessons { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ScheduledLessonViewModel> TuesdayLessons { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ScheduledLessonViewModel> WednesdayLessons { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ScheduledLessonViewModel> ThursdayLessons { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ScheduledLessonViewModel> FridayLessons { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// نموذج عرض الحصة المجدولة
/// </summary>
public class ScheduledLessonViewModel : INotifyPropertyChanged
{
    public Lesson Lesson { get; set; } = null!;

    // الكيانات المحلولة من ذاكرة الإدارة (لأن العلاقات قد تكون null في الجدول المولّد حديثاً)
    public Subject? ResolvedSubject { get; set; }
    public Teacher? ResolvedTeacher { get; set; }
    public Class? ResolvedClass { get; set; }
    public Room? ResolvedRoom { get; set; }

    // أمر فتح خصائص الحصة (يُضبط من MainWindowViewModel)
    public ICommand? EditCommand { get; set; }

    /// <summary>true = تلوين البطاقة بلون المعلم بدلاً من لون المادة</summary>
    public bool UseTeacherColor { get; set; }

    /// <summary>
    /// سياق عرض البطاقة (يحدد السطرين الثاني والثالث):
    /// في جدول الفصل: مادة/معلم/قاعة، في جدول المعلم: مادة/فصل/قاعة، ...
    /// </summary>
    public ChipDisplayContext ChipContext { get; set; } = ChipDisplayContext.School;

    /// <summary>السطر الأول في البطاقة: اسم المادة دائماً</summary>
    public string ChipLine1 => SubjectShortName;

    /// <summary>السطر الثاني: المعلم (أو الفصل عند عرض جدول معلم/طالب/مجموعة)</summary>
    public string ChipLine2 => ChipContext switch
    {
        ChipDisplayContext.Teacher => ClassShortName,
        ChipDisplayContext.Student => ClassShortName,
        ChipDisplayContext.Group => ClassShortName,
        _ => TeacherShortName
    };

    /// <summary>السطر الثالث: القاعة (أو الفصل في جدول القاعة/المادة أو المجموعة في جدول الطالب)</summary>
    public string ChipLine3 => ChipContext switch
    {
        ChipDisplayContext.Teacher => RoomShortName,
        ChipDisplayContext.Room => ClassShortName,
        ChipDisplayContext.Subject => ClassShortName,
        ChipDisplayContext.Student => GroupShortName,
        _ => RoomShortName
    };

    /// <summary>نص توقيت الحصة (من إعدادات توقيت الفترات) إن وُجد</summary>
    public string PeriodTimeText { get; set; } = "";

    public SubjectId SubjectId => Lesson.SubjectId;
    public TeacherId TeacherId => Lesson.TeacherId;
    public ClassId ClassId => Lesson.ClassId;
    public RoomId? RoomId => Lesson.RoomId;
    public DomainDayOfWeek Day => Lesson.Day;
    public PeriodNumber Period => Lesson.Period;
    public Duration Duration => Lesson.Duration;
    public LessonType Type => Lesson.Type;
    public bool IsFixed => Lesson.IsFixed;

    public string SubjectName => ResolvedSubject?.Name ?? Lesson.Subject?.Name ?? "";
    public string SubjectShortName => ResolvedSubject?.ShortName ?? Lesson.Subject?.ShortName ?? "";
    public string SubjectColorHex => ResolvedSubject?.ColorHex ?? Lesson.Subject?.ColorHex ?? "#2563EB";

    public string TeacherName => ResolvedTeacher?.FullName ?? Lesson.Teacher?.FullName ?? "";
    public string TeacherShortName =>
        (ResolvedTeacher ?? Lesson.Teacher) is { } t ? t.DisplayShortName : "";

    public string ClassName => ResolvedClass?.FullName ?? Lesson.Class?.FullName ?? "";
    public string ClassShortName => ResolvedClass?.ShortName ?? Lesson.Class?.ShortName ?? "";

    public string RoomName => ResolvedRoom?.Name ?? Lesson.Room?.Name ?? "";
    public string RoomShortName => ResolvedRoom?.ShortName ?? Lesson.Room?.ShortName ?? "";

    public string GroupName => Lesson.Group?.Name ?? "";
    public string GroupShortName => Lesson.Group?.Name ?? "";

    public string TypeText => Lesson.Type.ToString();
    public string DurationText => $"{Lesson.Duration.Value} د";
    public string DayText => Lesson.Day.ToString();
    public string PeriodText => PeriodNames.GetFullName(Lesson.Period.Value);

    public string TooltipText =>
        $"{SubjectName} - {TeacherName} - {ClassName}" +
        (string.IsNullOrEmpty(RoomName) ? "" : $" - {RoomName}") +
        (string.IsNullOrEmpty(PeriodTimeText) ? "" : $"\n{PeriodText}: {PeriodTimeText}");

    public System.Windows.Media.Brush SubjectColorBrush
    {
        get
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(SubjectColorHex);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.SteelBlue);
            }
        }
    }

    // ===== ألوان المعلمين (لون ثابت لكل معلم للتفرقة أثناء المراجعة) =====

    private static readonly string[] TeacherPalette =
    {
        "#DC2626", "#EA580C", "#D97706", "#65A30D",
        "#16A34A", "#0D9488", "#0891B2", "#2563EB",
        "#4F46E5", "#9333EA", "#C026D3", "#DB2777"
    };

    public string TeacherColorHex
    {
        get
        {
            try
            {
                var custom = (ResolvedTeacher ?? Lesson.Teacher)?.CustomColorHex;
                if (!string.IsNullOrWhiteSpace(custom)) return custom!;

                var bytes = TeacherId.Value.ToByteArray();
                var h = 0;
                foreach (var b in bytes) h = (h * 31) + b;
                if (h == int.MinValue) h = 0;
                return TeacherPalette[(h & 0x7FFFFFFF) % TeacherPalette.Length];
            }
            catch
            {
                return "#64748B";
            }
        }
    }

    public System.Windows.Media.Brush TeacherColorBrush
    {
        get
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(TeacherColorHex);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.SlateGray);
            }
        }
    }

    /// <summary>لون خلفية البطاقة حسب وضع التلوين المحدد</summary>
    public System.Windows.Media.Brush CardBackgroundBrush => UseTeacherColor ? TeacherColorBrush : SubjectColorBrush;

    /// <summary>
    /// لون النص داخل البطاقة: أسود على الألوان الفاتحة وأبيض على الألوان الداكنة
    /// (لون محسوب حسب الإضاءة حتى يبقى النص مقروءاً — كما في aSc)
    /// </summary>
    public System.Windows.Media.Brush CardForegroundBrush
    {
        get
        {
            try
            {
                var bg = (System.Windows.Media.SolidColorBrush)CardBackgroundBrush;
                var c = bg.Color;
                var luminance = ((0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B)) / 255.0;
                return luminance > 0.55
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1C))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            }
            catch
            {
                return System.Windows.Media.Brushes.White;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// نموذج عرض بأسلوب aSc: الصفوف = الكيانات (الفصول)، الأعمدة = الأيام × الحصص،
/// ورأس أفقي فيه اسم كل يوم ممتدّ فوق أعمدته.
/// يُبنى هذا النموذج في MainWindowViewModel ويُعرض داخل شبكة Grid في الواجهة.
/// </summary>
public sealed class AscGridView
{
    /// <summary>عنوان عمود الصفوف (مثال: "الفصل"، "المعلم").</summary>
    public string RowHeaderLabel { get; set; } = "الفصل";

    /// <summary>صفوف الكيانات.</summary>
    public List<AscRow> Rows { get; } = new();

    /// <summary>مجموعات الأيام (كل مجموعة فيها أعمدتها الفرعية = الحصص).</summary>
    public List<AscDayGroup> DayGroups { get; } = new();
}

/// <summary>مجموعة يوم واحد: اسم اليوم + أعمدته الفرعية (الحصص).</summary>
public sealed class AscDayGroup
{
    public DomainDayOfWeek Day { get; init; }
    public string DayName { get; init; } = "";
    public List<AscPeriodColumn> Periods { get; } = new();
}

/// <summary>عمود فرعي داخل يوم (حصة واحدة).</summary>
public sealed class AscPeriodColumn
{
    public int Period { get; init; }
    public string Label { get; init; } = "";
    public string TooltipText { get; init; } = "";
}

/// <summary>صف كيان واحد (فصل/معلم/قاعة/مادة) مع خلاياه.</summary>
public sealed class AscRow
{
    public Guid Key { get; init; }
    public string Name { get; init; } = "";

    /// <summary>الخلايا: المفتاح = (رقم اليوم، رقم الحصة)، القيمة = حصص الخلية.</summary>
    public Dictionary<(int Day, int Period), List<ScheduledLessonViewModel>> Cells { get; } = new();
}

/// <summary>مرجع خلية لعرض aSc (يُخزّن في Tag لعنصر الواجهة للسحب/الإفلات والتحديد).</summary>
public sealed class AscCellRef
{
    public DomainDayOfWeek Day { get; init; }
    public int Period { get; init; }
    public Guid RowKey { get; init; }
    public bool IsMatrix { get; init; }
    public List<ScheduledLessonViewModel> Lessons { get; init; } = new();
}

/// <summary>حصة تحتاج انتباهًا (متعارضة/بلا قاعة/غير معبأة) تُعرض في اللوحة السفلية لتُسحب إلى الجدول.</summary>
public sealed class ProblemLessonItem
{
    public ScheduledLessonViewModel Lesson { get; init; } = null!;
    public string Title { get; init; } = "";
    public string Reason { get; init; } = "";
}

/// <summary>
/// نموذج عرض الحصة للتعديل
/// </summary>
public partial class LessonEditViewModel : ObservableObject
{
    public LessonId Id { get; set; } = LessonId.Empty;
    
    [ObservableProperty]
    private SubjectId _subjectId = SubjectId.Empty;
    
    [ObservableProperty]
    private TeacherId _teacherId = TeacherId.Empty;
    
    [ObservableProperty]
    private ClassId _classId = ClassId.Empty;
    
    [ObservableProperty]
    private RoomId? _roomId;
    
    [ObservableProperty]
    private DomainDayOfWeek _day;
    
    [ObservableProperty]
    private PeriodNumber _period = PeriodNumber.Create(1);
    
    [ObservableProperty]
    private Duration _duration = Duration.FromMinutes(45);
    
    [ObservableProperty]
    private LessonType _type = LessonType.Regular;
    
    [ObservableProperty]
    private bool _isFixed;
    
    [ObservableProperty]
    private string? _notes;

    public IReadOnlyList<Subject> Subjects { get; set; } = Array.Empty<Subject>();
    public IReadOnlyList<Teacher> Teachers { get; set; } = Array.Empty<Teacher>();
    public IReadOnlyList<Class> Classes { get; set; } = Array.Empty<Class>();
    public IReadOnlyList<Room> Rooms { get; set; } = Array.Empty<Room>();
}

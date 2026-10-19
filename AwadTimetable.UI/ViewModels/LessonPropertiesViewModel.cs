namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// نموذج عرض خصائص الحصة المحددة (لوحة "الخصائص")
/// </summary>
public class LessonPropertiesViewModel
{
    public string SubjectName { get; init; } = "غير محدد";
    public string TeacherName { get; init; } = "غير محدد";
    public string ClassName { get; init; } = "غير محدد";
    public string RoomName { get; init; } = "بدون غرفة";
    public string DayName { get; init; } = "";
    public string PeriodName { get; init; } = "";
    public string DurationText { get; init; } = "";
    public string TypeText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string Notes { get; init; } = "—";
}

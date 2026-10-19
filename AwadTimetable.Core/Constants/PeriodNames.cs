namespace AwadTimetable.Core.Constants;

/// <summary>
/// أسماء الفترات الدراسية بالعربية (الأولى، الثانية، ...)
/// </summary>
public static class PeriodNames
{
    private static readonly string[] Names =
    {
        "",                 // 0
        "الأولى",           // 1
        "الثانية",          // 2
        "الثالثة",          // 3
        "الرابعة",          // 4
        "الخامسة",          // 5
        "السادسة",          // 6
        "السابعة",          // 7
        "الثامنة",          // 8
        "التاسعة",          // 9
        "العاشرة",          // 10
        "الحادية عشرة",    // 11
        "الثانية عشرة",    // 12
        "الثالثة عشرة",   // 13
        "الرابعة عشرة",   // 14
        "الخامسة عشرة",   // 15
    };

    /// <summary>الاسم الكامل: "الحصة الأولى"</summary>
    public static string GetFullName(int period)
        => period >= 1 && period < Names.Length
            ? $"الحصة {Names[period]}"
            : $"الحصة {period}";

    /// <summary>الاسم المختصر: "الأولى"</summary>
    public static string GetName(int period)
        => period >= 1 && period < Names.Length ? Names[period] : period.ToString();
}

using AwadTimetable.Core.Services;

namespace AwadTimetable.Tests;

/// <summary>اختبارات استيراد الإسنادات من CSV.</summary>
public class TeachingAssignmentImporterTests
{
    [Fact]
    public void FromCsv_ParsesRows_AndSkipsHeader()
    {
        var csv = string.Join('\n',
            "المعلم,المادة,الفصل,عدد الحصص",
            "أحمد علي,رياضيات,أول متوسط 1,5",
            "سارة محمود;علوم;أول متوسط 2;3",
            ",,,",
            "خالد سعيد,عربي,أول متوسط 1");

        var rows = TeachingAssignmentImporter.FromCsv(csv);

        Assert.Equal(3, rows.Count);

        Assert.Equal("أحمد علي", rows[0].TeacherName);
        Assert.Equal("رياضيات", rows[0].SubjectName);
        Assert.Equal("أول متوسط 1", rows[0].ClassName);
        Assert.Equal(5, rows[0].WeeklyPeriods);

        Assert.Equal(3, rows[1].WeeklyPeriods);

        // بدون عمود عدد الحصص → الافتراضي 1
        Assert.Equal(1, rows[2].WeeklyPeriods);
    }

    [Fact]
    public void FromRows_SkipsIncompleteRows()
    {
        var rows = TeachingAssignmentImporter.FromRows(new[]
        {
            new[] { "معلم", "مادة" },              // ناقص
            new[] { "معلم", "مادة", "فصل", "abc" }, // عدد غير رقمي → 1
            new[] { "معلم2", "مادة2", "فصل2", "4" },
        });

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].WeeklyPeriods);
        Assert.Equal(4, rows[1].WeeklyPeriods);
    }
}

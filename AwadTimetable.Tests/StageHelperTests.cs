using AwadTimetable.Core.Entities;

namespace AwadTimetable.Tests;

/// <summary>اختبارات استنتاج الصف والشعبة من اسم الفصل العربي (تُستخدم عند الاستيراد).</summary>
public class StageHelperTests
{
    [Theory]
    [InlineData("أول متوسط 1", 7)]
    [InlineData("ثاني متوسط 2", 8)]
    [InlineData("ثالث متوسط1", 9)]
    [InlineData("أول ثانوي 2", 10)]
    [InlineData("ثاني ثانوي 1", 11)]
    [InlineData("ثالث ثانوي 2", 12)]
    [InlineData("أول ابتدائي", 1)]
    [InlineData("خامس ابتدائي", 5)]
    [InlineData("سادس ابتدائي", 6)]
    public void ParseGrade_InfersGradeFromArabicName(string name, int expected)
        => Assert.Equal(expected, StageHelper.ParseGrade(name));

    [Theory]
    [InlineData("أول متوسط أ", 'أ')]
    [InlineData("أول متوسط ب", 'ب')]
    [InlineData("ثاني ثانوي ج", 'ج')]
    [InlineData("أول متوسط 1", 'أ')]
    [InlineData("أول ثانوي1", 'أ')]
    public void ParseSection_InfersSectionFromName(string name, char expected)
        => Assert.Equal(expected, StageHelper.ParseSection(name));

    [Theory]
    [InlineData("أول متوسط 1", "متوسط")]
    [InlineData("ثالث ثانوي 2", "ثانوي")]
    [InlineData("سادس ابتدائي", "ابتدائي")]
    public void GetStage_InfersStageFromName(string name, string expected)
        => Assert.Equal(expected, StageHelper.GetStage(0, name));
}

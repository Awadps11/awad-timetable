using AwadTimetable.Core.Services;

namespace AwadTimetable.Tests;

/// <summary>
/// تحقق من قارئ aSc على ملف تصدير حقيقي أنتجه aSc 2027 (windows-1256، معرّفات سداسية).
/// الملف في Fixtures يُنسَخ لمجلد التشغيل، فالاختبار قائم بذاته.
/// </summary>
public class AscXmlReaderTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "asc-export-2027.xml");

    [Fact]
    public void FixtureFile_Exists()
    {
        Assert.True(File.Exists(FixturePath), $"لم يُعثر على ملف aSc للاختبار: {FixturePath}");
    }

    [Fact]
    public void Parse_ReadsAllDefinitions_FromRealAscFile()
    {
        var parsed = AscXmlReader.ParseFile(FixturePath);

        Assert.Equal(7, parsed.PeriodsCount);
        Assert.Equal(31, parsed.Subjects.Count);
        Assert.Equal(38, parsed.Teachers.Count);
        Assert.Equal(17, parsed.Classes.Count);
        // ملف aSc الحقيقي هذا فيه بلوك <classrooms> فارغ (بلا قاعات)
        Assert.Empty(parsed.Rooms);
        Assert.Equal(10, parsed.Cards.Count);
    }

    [Fact]
    public void Parse_DecodesArabicNames_NotMojibake()
    {
        var parsed = AscXmlReader.ParseFile(FixturePath);

        // صيغة aSc windows-1256: الاسم يجب أن يُفكّ إلى عربية، لا رموز بديلة
        Assert.Contains(parsed.Subjects.Values, s => s.Name.Any(c => c >= '\u0600' && c <= '\u06FF'));
        Assert.Contains(parsed.Teachers.Values, t => t.First.Any(c => c >= '\u0600' && c <= '\u06FF'));
    }

    [Fact]
    public void Parse_EveryCardResolvesToSubjectClassAndTeacher()
    {
        var parsed = AscXmlReader.ParseFile(FixturePath);

        foreach (var card in parsed.Cards)
        {
            Assert.True(parsed.Lessons.TryGetValue(card.LessonId, out var lesson), $"بطاقة بلا درس: {card.LessonId}");
            Assert.True(parsed.Subjects.ContainsKey(lesson!.SubjectId), "درس بلا مادة معروفة");

            var classIds = AscXmlReader.SplitIds(lesson.ClassIds);
            var teacherIds = AscXmlReader.SplitIds(lesson.TeacherIds);
            Assert.NotEmpty(classIds);
            Assert.NotEmpty(teacherIds);
            Assert.All(classIds, id => Assert.True(parsed.Classes.ContainsKey(id)));
            Assert.All(teacherIds, id => Assert.True(parsed.Teachers.ContainsKey(id)));
        }
    }

    [Fact]
    public void Parse_EveryCardHasResolvableDay()
    {
        var parsed = AscXmlReader.ParseFile(FixturePath);

        foreach (var card in parsed.Cards)
        {
            var days = AscXmlReader.ParseDayBits(card.Days).ToList();
            Assert.NotEmpty(days);
            // aSc لا يستخدم الجمعة
            Assert.DoesNotContain(AwadTimetable.Core.Enums.DayOfWeek.Friday, days);
        }
    }

    [Fact]
    public void Parse_PeriodIsZeroBased()
    {
        var parsed = AscXmlReader.ParseFile(FixturePath);

        // aSc يكتب الحصة الأولى period="0"
        Assert.Contains(parsed.Cards, c => c.Period0 == 0);
        Assert.All(parsed.Cards, c => Assert.InRange(c.Period0, 0, parsed.PeriodsCount - 1));
    }

    [Fact]
    public void ParseDayBits_MapsSundayToSecondBit()
    {
        var sunday = AscXmlReader.ParseDayBits("10000").Single();
        var thursday = AscXmlReader.ParseDayBits("00001").Single();

        Assert.Equal(AwadTimetable.Core.Enums.DayOfWeek.Sunday, sunday);
        Assert.Equal(AwadTimetable.Core.Enums.DayOfWeek.Thursday, thursday);
    }

    [Fact]
    public void Parse_MultiMask_UnionsDays()
    {
        var days = AscXmlReader.ParseDayBits("10000,00100").ToList();

        Assert.Equal(2, days.Count);
        Assert.Contains(AwadTimetable.Core.Enums.DayOfWeek.Sunday, days);
        Assert.Contains(AwadTimetable.Core.Enums.DayOfWeek.Tuesday, days);
    }
}

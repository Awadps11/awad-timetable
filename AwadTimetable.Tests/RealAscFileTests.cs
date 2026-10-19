using AwadTimetable.Core.Services;

namespace AwadTimetable.Tests;

/// <summary>
/// تحقق من المحلّل على ملف aSc الكامل الحقيقي (168KB، إصدار 2024.24.1، 17 فصلاً و38 معلماً و100+ حصة).
/// </summary>
public class RealAscFileTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "asc-full-2024.xml");

    [Fact]
    public void Fixture_Exists()
    {
        Assert.True(File.Exists(FixturePath), FixturePath);
    }

    [Fact]
    public void VendorParser_DelegatesAsc_AndParsesRealFile()
    {
        var xml = AscXmlReader.ReadXmlText(FixturePath);
        var rows = VendorXmlImporter.Parse(xml, VendorXmlImporter.Untis);

        Assert.True(rows.Count >= 100, $"عدد الحصص المستخرجة: {rows.Count}");
        Assert.True(rows.Select(r => r.ClassName).Distinct().Count() >= 10);
        Assert.True(rows.Select(r => r.TeacherName).Distinct().Count() >= 20);
        Assert.All(rows, r => Assert.InRange(r.Period, 1, 12));
    }

    [Fact]
    public void AscReader_ReadsFullFile()
    {
        var parsed = AscXmlReader.ParseFile(FixturePath);

        Assert.True(parsed.Classes.Count >= 17, $"الفصول: {parsed.Classes.Count}");
        Assert.True(parsed.Teachers.Count >= 36, $"المعلمون: {parsed.Teachers.Count}");
        Assert.True(parsed.Subjects.Count >= 30, $"المواد: {parsed.Subjects.Count}");
        Assert.True(parsed.Cards.Count >= 100, $"البطاقات: {parsed.Cards.Count}");
    }

    [Fact]
    public void AscReader_NoOrphanCards()
    {
        var parsed = AscXmlReader.ParseFile(FixturePath);

        foreach (var card in parsed.Cards)
            Assert.True(parsed.Lessons.ContainsKey(card.LessonId), card.LessonId);
    }
}

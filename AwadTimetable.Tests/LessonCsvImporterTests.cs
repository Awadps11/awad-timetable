using AwadTimetable.Core.Services;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class LessonCsvImporterTests
{
    [Fact]
    public void ParseCsv_EnglishHeader()
    {
        var csv = "Class,Teacher,Subject,Day,Period,Room\n7A,أحمد,رياضيات,Monday,3,101\n";

        var rows = LessonCsvImporter.ParseCsv(csv);

        Assert.Single(rows);
        Assert.Equal("7A", rows[0].ClassName);
        Assert.Equal("أحمد", rows[0].TeacherName);
        Assert.Equal(DomainDayOfWeek.Monday, rows[0].Day);
        Assert.Equal(3, rows[0].Period);
        Assert.Equal("101", rows[0].RoomName);
    }

    [Fact]
    public void ParseCsv_GermanHeader_Semicolon()
    {
        var csv = "Klasse;Lehrer;Fach;Tag;Stunde\n9b;Müller;Mathe;Dienstag;2\n";

        var rows = LessonCsvImporter.ParseCsv(csv);

        Assert.Single(rows);
        Assert.Equal("9b", rows[0].ClassName);
        Assert.Equal(DomainDayOfWeek.Tuesday, rows[0].Day);
        Assert.Equal(2, rows[0].Period);
    }

    [Fact]
    public void ParseCsv_CzechHeader()
    {
        var csv = "třída;učitel;předmět;den;hodina\n1A;Novák;Fyzika;středa;4\n";

        var rows = LessonCsvImporter.ParseCsv(csv);

        Assert.Single(rows);
        Assert.Equal(DomainDayOfWeek.Wednesday, rows[0].Day);
        Assert.Equal(4, rows[0].Period);
    }

    [Fact]
    public void ParseCsv_ArabicHeader()
    {
        var csv = "الفصل,المعلم,المادة,اليوم,الفترة\nأول متوسط 1,سالم,علوم,الأحد,5\n";

        var rows = LessonCsvImporter.ParseCsv(csv);

        Assert.Single(rows);
        Assert.Equal(DomainDayOfWeek.Sunday, rows[0].Day);
        Assert.Equal(5, rows[0].Period);
    }

    [Fact]
    public void ParseCsv_WithoutHeader_UsesDefaultOrder()
    {
        var csv = "7A,أحمد,رياضيات,السبت,1\n";

        var rows = LessonCsvImporter.ParseCsv(csv);

        Assert.Single(rows);
        Assert.Equal(DomainDayOfWeek.Saturday, rows[0].Day);
    }

    [Fact]
    public void ParseCsv_SkipsInvalidRows()
    {
        var csv = "Class,Teacher,Subject,Day,Period\n7A,أحمد,رياضيات,Noday,3\n7A,,رياضيات,Monday,3\n";

        Assert.Empty(LessonCsvImporter.ParseCsv(csv));
    }

    [Fact]
    public void TryParseDay_NumericZeroMeansSaturday()
    {
        Assert.True(LessonCsvImporter.TryParseDay("0", out var d));
        Assert.Equal(DomainDayOfWeek.Saturday, d);
    }

    [Fact]
    public void TryParsePeriod_ZeroBecomesOne()
    {
        Assert.True(LessonCsvImporter.TryParsePeriod("0", out var p));
        Assert.Equal(1, p);
    }
}

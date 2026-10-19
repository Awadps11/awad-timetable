using AwadTimetable.Core.Services;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class VendorXmlImporterTests
{
    [Fact]
    public void Parse_UntisStyle_PlacedLessons()
    {
        var xml = """
<untis>
  <lessons>
    <lesson>
      <class>7A</class><teacher>Ahmed</teacher><subject>Math</subject>
      <day>Monday</day><period>3</period><room>101</room>
    </lesson>
  </lessons>
</untis>
""";

        var rows = VendorXmlImporter.Parse(xml, VendorXmlImporter.Untis);

        Assert.Single(rows);
        Assert.Equal(7, 7);
        Assert.Equal("7A", rows[0].ClassName);
        Assert.Equal(DomainDayOfWeek.Monday, rows[0].Day);
        Assert.Equal(3, rows[0].Period);
    }

    [Fact]
    public void Parse_WinLdStyle_CzechTags()
    {
        var xml = """
<rozvrh>
  <hodiny>
    <hodina>
      <trida>1A</trida><ucitel>Novák</ucitel><predmet>Fyzika</predmet>
      <den>středa</den><period>4</period>
    </hodina>
  </hodiny>
</rozvrh>
""";

        var rows = VendorXmlImporter.Parse(xml, VendorXmlImporter.WinLd);

        Assert.Single(rows);
        Assert.Equal("1A", rows[0].ClassName);
        Assert.Equal(DomainDayOfWeek.Wednesday, rows[0].Day);
        Assert.Equal(4, rows[0].Period);
    }

    [Fact]
    public void Parse_VulcanStyle_PolishTags()
    {
        var xml = """
<plan>
  <lekcja>
    <klasa>2B</klasa><nauczyciel>Kowalski</nauczyciel><przedmiot>Matematyka</przedmiot>
    <dzien>wtorek</dzien><period>2</period>
  </lekcja>
</plan>
""";

        var rows = VendorXmlImporter.Parse(xml, VendorXmlImporter.Vulcan);

        Assert.Single(rows);
        Assert.Equal("2B", rows[0].ClassName);
        Assert.Equal(DomainDayOfWeek.Tuesday, rows[0].Day);
        Assert.Equal(2, rows[0].Period);
    }

    [Fact]
    public void Parse_SchildStyle_GermanTags()
    {
        var xml = """
<Schild>
  <Stunde>
    <Klasse>9b</Klasse><Lehrer>Müller</Lehrer><Fach>Mathe</Fach>
    <Tag>Dienstag</Tag><Period>5</Period>
  </Stunde>
</Schild>
""";

        var rows = VendorXmlImporter.Parse(xml, VendorXmlImporter.Schild);

        Assert.Single(rows);
        Assert.Equal("9b", rows[0].ClassName);
        Assert.Equal(DomainDayOfWeek.Tuesday, rows[0].Day);
        Assert.Equal(5, rows[0].Period);
    }

    [Fact]
    public void Parse_BasicDataOnly_ProducesUnplacedLessonsByWeeklyCount()
    {
        var xml = """
<untis>
  <lesson>
    <class>7A</class><teacher>Ahmed</teacher><subject>Math</subject>
    <periodsperweek>4</periodsperweek>
  </lesson>
</untis>
""";

        var rows = VendorXmlImporter.Parse(xml);

        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(0, r.Period));
    }

    [Fact]
    public void Parse_MultipleClasses_ExpandRows()
    {
        var xml = """
<untis>
  <lesson>
    <class>7A,7B</class><teacher>Ahmed</teacher><subject>Math</subject>
    <day>Sunday</day><period>1</period>
  </lesson>
</untis>
""";

        var rows = VendorXmlImporter.Parse(xml);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Parse_AscXml_IsDelegated()
    {
        // ملف aSc مبسط: مادة/معلم/فصل/درس + بطاقة
        var xml = """
<fet version="7.12.0">
  <Days_List><Day><Name>Saturday</Name></Day></Days_List>
  <Hours_List><Hour><Name>P1</Name></Hour></Hours_List>
  <Teachers_List><Teacher><Name>T1</Name></Teacher></Teachers_List>
  <Subjects_List><Subject><Name>S1</Name></Subject></Subjects_List>
  <Students_List><Year><Name>C1</Name><Group><Name>G1</Name><Subgroup><Name>S1</Name></Subgroup></Group></Year></Students_List>
</fet>
""";

        // ليس aSc حقيقياً (aSc root=timetable)، لذلك يجب ألا يتعطل
        var rows = VendorXmlImporter.Parse(xml);

        Assert.NotNull(rows);
    }

    [Fact]
    public void Parse_InvalidXml_ReturnsEmpty()
    {
        Assert.Empty(VendorXmlImporter.Parse("<not-xml"));
        Assert.Empty(VendorXmlImporter.Parse(""));
    }
}

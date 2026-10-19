using System.IO.Compression;
using System.Text;
using AwadTimetable.Core.Services;

namespace AwadTimetable.Tests;

public class SubjectListImporterTests
{
    [Fact]
    public void ParseCsv_WithHeader_ReadsNameShortAndPeriods()
    {
        var csv = "المادة,الاختصار,الحصص\nرياضيات,ريا,5\nعلوم,عل,3\n";

        var rows = SubjectListImporter.ParseCsv(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal("رياضيات", rows[0].Name);
        Assert.Equal("ريا", rows[0].ShortName);
        Assert.Equal(5, rows[0].PeriodsPerWeek);
        Assert.Equal(3, rows[1].PeriodsPerWeek);
    }

    [Fact]
    public void ParseCsv_WithoutHeader_StillWorks()
    {
        var rows = SubjectListImporter.ParseCsv("عربي,عر,4\nإنجليزي");

        Assert.Equal(2, rows.Count);
        Assert.Equal("عربي", rows[0].Name);
        Assert.Equal(4, rows[0].PeriodsPerWeek);
        Assert.Equal("إنجليزي", rows[1].Name);
        Assert.Null(rows[1].ShortName);
    }

    [Fact]
    public void ParseCsv_SemicolonDelimiter_IsDetected()
    {
        var rows = SubjectListImporter.ParseCsv("المادة;الاختصار\nفيزياء;في\nكيمياء;كي");

        Assert.Equal(2, rows.Count);
        Assert.Equal("فيزياء", rows[0].Name);
        Assert.Equal("في", rows[0].ShortName);
    }

    [Fact]
    public void ParseCsv_QuotedValues_AreHandled()
    {
        var rows = SubjectListImporter.ParseCsv("\"لغة عربية\",\"ع\",\"6\"");

        Assert.Single(rows);
        Assert.Equal("لغة عربية", rows[0].Name);
        Assert.Equal("ع", rows[0].ShortName);
        Assert.Equal(6, rows[0].PeriodsPerWeek);
    }

    [Fact]
    public void ParseCsv_DuplicateNames_AreDeduped()
    {
        var rows = SubjectListImporter.ParseCsv("رياضيات\nرياضيات\nعلوم");

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ParseCsv_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(SubjectListImporter.ParseCsv(""));
        Assert.Empty(SubjectListImporter.ParseCsv("   \n  "));
    }

    [Fact]
    public void ParseXlsx_ReadsInlineStrings()
    {
        using var stream = BuildXlsx(new[]
        {
            new[] { "المادة", "الاختصار", "الحصص" },
            new[] { "رياضيات", "ريا", "5" },
            new[] { "علوم", "عل", "2" },
        });

        var rows = SubjectListImporter.ParseXlsx(stream);

        Assert.Equal(2, rows.Count);
        Assert.Equal("رياضيات", rows[0].Name);
        Assert.Equal("ريا", rows[0].ShortName);
        Assert.Equal(5, rows[0].PeriodsPerWeek);
        Assert.Equal("علوم", rows[1].Name);
    }

    [Fact]
    public void ParseXlsx_ThrowsOnNonExcel()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip"));

        Assert.ThrowsAny<Exception>(() => SubjectListImporter.ParseXlsx(stream));
    }

    /// <summary>يبني ملف xlsx بسيط (inlineStr) في الذاكرة</summary>
    private static MemoryStream BuildXlsx(IReadOnlyList<string[]> rows)
    {
        static string Col(int i)
        {
            var s = string.Empty;
            i++;
            while (i > 0) { var r = (i - 1) % 26; s = (char)('A' + r) + s; i = (i - 1) / 26; }
            return s;
        }

        var sheet = new StringBuilder();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var text = System.Security.SecurityElement.Escape(rows[r][c]) ?? "";
                sheet.Append($"<c r=\"{Col(c)}{r + 1}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{text}</t></is></c>");
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                var entry = zip.CreateEntry(name);
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(content);
            }

            Add("[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>");
            Add("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheets>" +
                "<sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/>" +
                "</sheets></workbook>");
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }

        ms.Position = 0;
        return ms;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace AwadTimetable.Core.Services;

/// <summary>صف مادة مُستورَد: الاسم، الاختصار، عدد الحصص الأسبوعي</summary>
public sealed record SubjectImportRow(string Name, string? ShortName, int? PeriodsPerWeek);

/// <summary>
/// استيراد قائمة مواد من CSV أو Excel (xlsx) إلى صفوف موحّدة.
/// يكتشف الترويسة تلقائياً، ويدعم الفاصلة والفاصلة المنقوطة والمُحدِّد الجدولي، والعربية.
/// </summary>
public static class SubjectListImporter
{
    private static readonly string[] HeaderHints =
    {
        "اسم", "المادة", "المواد", "مادة", "name", "subject", "short", "الاختصار", "المختصر"
    };

    public static IReadOnlyList<SubjectImportRow> ParseCsv(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<SubjectImportRow>();

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (lines.Count == 0) return Array.Empty<SubjectImportRow>();

        var delimiter = DetectDelimiter(lines[0]);
        var rows = lines.Select(l => SplitLine(l, delimiter)).ToList();
        return FromRows(rows);
    }

    public static IReadOnlyList<SubjectImportRow> ParseXlsx(Stream stream)
        => FromRows(ReadXlsxRows(stream));

    public static IReadOnlyList<SubjectImportRow> FromRows(IEnumerable<string[]> rows)
    {
        var list = rows
            .Select(r => r.Select(c => (c ?? string.Empty).Trim()).ToArray())
            .ToList();

        // إزالة الصفوف الفارغة
        list = list.Where(r => r.Any(c => c.Length > 0)).ToList();
        if (list.Count == 0) return Array.Empty<SubjectImportRow>();

        // تجاهل الترويسة إن وُجدت
        if (LooksLikeHeader(list[0]))
            list.RemoveAt(0);

        var result = new List<SubjectImportRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in list)
        {
            var name = row.ElementAtOrDefault(0)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!seen.Add(name)) continue;

            var shortName = row.ElementAtOrDefault(1)?.Trim();
            if (string.Equals(shortName, name, StringComparison.OrdinalIgnoreCase)) shortName = null;

            int? periods = null;
            var periodCell = row.ElementAtOrDefault(2);
            if (!string.IsNullOrWhiteSpace(periodCell))
            {
                var digits = new string(periodCell.Where(char.IsAsciiDigit).ToArray());
                if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0)
                    periods = p;
            }

            result.Add(new SubjectImportRow(name!, string.IsNullOrWhiteSpace(shortName) ? null : shortName, periods));
        }

        return result;
    }

    private static bool LooksLikeHeader(string[] row)
        => row.Any(cell => HeaderHints.Any(h => cell.Contains(h, StringComparison.OrdinalIgnoreCase)));

    private static char DetectDelimiter(string header)
    {
        var comma = header.Count(c => c == ',');
        var semi = header.Count(c => c == ';');
        var tab = header.Count(c => c == '\t');

        if (tab >= comma && tab >= semi && tab > 0) return '\t';
        if (semi > comma) return ';';
        return ',';
    }

    /// <summary>تقسيم بسيط يدعم الاقتباس المزدوج</summary>
    public static string[] SplitLine(string line, char delimiter)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                parts.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        parts.Add(sb.ToString());
        return parts.ToArray();
    }

    /// <summary>قراءة أول ورقة عمل من xlsx (يدعم inlineStr و sharedStrings والأرقام)</summary>
    public static List<string[]> ReadXlsxRows(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var shared = new List<string>();
        var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (ssEntry != null)
        {
            using var ssStream = ssEntry.Open();
            var ssDoc = XDocument.Load(ssStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var si in ssDoc.Root!.Elements(ns + "si"))
                shared.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
        }

        var sheetEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("الملف لا يحتوي على ورقة عمل (ليس ملف Excel صالح)");

        using var sheetStream = sheetEntry.Open();
        var doc = XDocument.Load(sheetStream);
        XNamespace nm = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        static int ColumnIndex(string cellRef)
        {
            var col = 0;
            foreach (var ch in cellRef)
            {
                if (char.IsLetter(ch)) col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
                else break;
            }
            return Math.Max(0, col - 1);
        }

        var rows = new List<string[]>();

        foreach (var rowEl in doc.Root!.Element(nm + "sheetData")!.Elements(nm + "row"))
        {
            var cells = new List<(int Col, string Val)>();

            foreach (var c in rowEl.Elements(nm + "c"))
            {
                var cellRef = (string?)c.Attribute("r") ?? "A1";
                var col = ColumnIndex(cellRef);
                var type = (string?)c.Attribute("t") ?? "";
                string value;

                if (type == "inlineStr")
                {
                    value = string.Concat((c.Element(nm + "is")?.Descendants(nm + "t") ?? Enumerable.Empty<XElement>())
                        .Select(t => t?.Value ?? ""));
                }
                else if (type == "s")
                {
                    var vText = c.Element(nm + "v")?.Value;
                    value = int.TryParse(vText, out var idx) && idx >= 0 && idx < shared.Count ? shared[idx] : "";
                }
                else
                {
                    value = c.Element(nm + "v")?.Value ?? "";
                }

                cells.Add((col, value));
            }

            if (cells.Count == 0) continue;

            var maxCol = cells.Max(x => x.Col);
            var arr = new string[maxCol + 1];
            for (var i = 0; i < arr.Length; i++) arr[i] = string.Empty;
            foreach (var (col, val) in cells)
                if (col < arr.Length) arr[col] = val;

            rows.Add(arr);
        }

        return rows;
    }
}

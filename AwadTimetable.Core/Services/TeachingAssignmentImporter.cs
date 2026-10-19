using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AwadTimetable.Core.Services;

/// <summary>صف إسناد مستورد: (معلم، مادة، فصل، عدد الحصص الأسبوعية).</summary>
public sealed record TeachingAssignmentImportRow(string TeacherName, string SubjectName, string ClassName, int WeeklyPeriods);

/// <summary>
/// استيراد الإسنادات مجمّعةً من CSV أو Excel.
/// الأعمدة: المعلم، المادة، الفصل، عدد الحصص الأسبوعية (العمود الرابع اختياري ويُفترض 1).
/// يتجاهل صف العنوان والصفوف الفارغة أو الناقصة.
/// </summary>
public static class TeachingAssignmentImporter
{
    private static readonly char[] Delimiters = { ',', ';', '\t' };

    public static IReadOnlyList<TeachingAssignmentImportRow> FromCsv(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<TeachingAssignmentImportRow>();

        var rows = new List<string[]>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            rows.Add(SplitLine(raw));
        }

        return FromRows(rows);
    }

    public static IReadOnlyList<TeachingAssignmentImportRow> FromXlsx(Stream stream)
        => FromRows(SubjectListImporter.ReadXlsxRows(stream));

    public static IReadOnlyList<TeachingAssignmentImportRow> FromRows(IEnumerable<string[]> rows)
    {
        var result = new List<TeachingAssignmentImportRow>();

        foreach (var cells in rows)
        {
            if (cells is null) continue;

            var cols = cells
                .Select(c => (c ?? string.Empty).Trim())
                .Where(c => c.Length > 0)
                .ToArray();

            if (cols.Length < 3) continue;

            // تجاهل صف العنوان (المعلم/Teacher)
            if (IsHeader(cols[0])) continue;

            var teacher = cols[0];
            var subject = cols[1];
            var @class = cols[2];
            if (string.IsNullOrWhiteSpace(teacher) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(@class))
                continue;

            var periods = 1;
            if (cols.Length >= 4 && int.TryParse(cols[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0)
                periods = p;

            result.Add(new TeachingAssignmentImportRow(teacher, subject, @class, periods));
        }

        return result;
    }

    private static bool IsHeader(string firstCell)
        => firstCell.Equals("المعلم", StringComparison.OrdinalIgnoreCase)
            || firstCell.Equals("اسم المعلم", StringComparison.OrdinalIgnoreCase)
            || firstCell.Equals("teacher", StringComparison.OrdinalIgnoreCase)
            || firstCell.Equals("teacher name", StringComparison.OrdinalIgnoreCase);

    private static string[] SplitLine(string line)
    {
        foreach (var delimiter in Delimiters)
        {
            var parts = SubjectListImporter.SplitLine(line, delimiter);
            if (parts.Length >= 2) return parts;
        }

        return new[] { line };
    }
}

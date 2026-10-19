using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AwadTimetable.Core.Services;

/// <summary>صف مكوّن (معلم/مادة/فصل/قاعة) للتصدير والاستيراد</summary>
public sealed record ComponentRow(string Name, string? ShortName, int? Number);

/// <summary>
/// تصدير واستيراد مكوّن واحد (معلمين، مواد، فصول، قاعات) بصيغة CSV.
/// الأعمدة: الاسم، الاختصار، رقم (عدد الحصص للمادة / عدد الطلاب للفصل / السعة للقاعة).
/// يقرأ ملفات Excel أيضًا عبر SubjectListImporter.ReadXlsxRows.
/// </summary>
public static class ComponentTransfer
{
    public const string Header = "الاسم,الاختصار,الرقم";

    public static string ToCsv(IEnumerable<ComponentRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        foreach (var r in rows)
        {
            sb.Append(Escape(r.Name)).Append(',')
              .Append(Escape(r.ShortName ?? string.Empty)).Append(',')
              .Append(r.Number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
              .AppendLine();
        }
        return sb.ToString();
    }

    public static IReadOnlyList<ComponentRow> FromCsv(string text)
        => Parse(SubjectListImporter.ParseCsv(text).Select(r =>
            new ComponentRow(r.Name, r.ShortName, r.PeriodsPerWeek)));

    public static IReadOnlyList<ComponentRow> FromXlsx(System.IO.Stream stream)
        => Parse(SubjectListImporter.ParseXlsx(stream).Select(r =>
            new ComponentRow(r.Name, r.ShortName, r.PeriodsPerWeek)));

    private static IReadOnlyList<ComponentRow> Parse(IEnumerable<ComponentRow> rows)
        => rows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();

    private static string Escape(string value)
        => value.Contains(',') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}

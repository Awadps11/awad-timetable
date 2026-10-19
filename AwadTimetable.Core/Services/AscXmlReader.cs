using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

public sealed record AscSubjectDef(string Name, string? Short);
public sealed record AscTeacherDef(string First, string Last, string Full);
public sealed record AscClassDef(string Name, string? Grade);
public sealed record AscLessonDef(string SubjectId, string ClassIds, string TeacherIds, string? RoomIds, string? DaysDefId);

/// <summary>بطاقة حصة من aSc مع الأيام محلولة (من البطاقة أو من daysdef)</summary>
public sealed record AscCard(string LessonId, int Period0, string Days);

/// <summary>ناتج قراءة ملف aSc XML: التعريفات والبطاقات جاهزة للاستهلاك</summary>
public sealed class AscParsedFile
{
    public int PeriodsCount { get; init; }
    public IReadOnlyDictionary<string, AscSubjectDef> Subjects { get; init; } = new Dictionary<string, AscSubjectDef>();
    public IReadOnlyDictionary<string, AscTeacherDef> Teachers { get; init; } = new Dictionary<string, AscTeacherDef>();
    public IReadOnlyDictionary<string, AscClassDef> Classes { get; init; } = new Dictionary<string, AscClassDef>();
    public IReadOnlyDictionary<string, string> Rooms { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, AscLessonDef> Lessons { get; init; } = new Dictionary<string, AscLessonDef>();
    public IReadOnlyList<AscCard> Cards { get; init; } = new List<AscCard>();
}

/// <summary>
/// قارئ صيغة aSc Timetables XML (‎2027): تفصل القراءة والتحليل عن كتابة قاعدة البيانات
/// لتصبح قابلة للاختبار على ملفات aSc الحقيقية.
/// يدعم ترميز windows-1256 (العربية) و UTF-8، وأقنعة الأيام "10000" (الموضع 0 = الأحد).
/// </summary>
public static class AscXmlReader
{
    static AscXmlReader()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* مسجل مسبقاً */ }
    }

    public static AscParsedFile ParseFile(string filePath) => Parse(ReadXmlText(filePath));

    public static AscParsedFile Parse(string text)
    {
        var doc = XDocument.Parse(text, LoadOptions.None);
        var root = doc.Root ?? throw new InvalidDataException("الملف لا يحتوي على عنصر جذر");

        var periodCount = root.Element("periods")?.Elements("period").Count() ?? 0;

        var subjects = new Dictionary<string, AscSubjectDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in root.Element("subjects")?.Elements("subject") ?? Enumerable.Empty<XElement>())
        {
            var id = Attr(el, "id"); if (string.IsNullOrEmpty(id)) continue;
            subjects[id] = new AscSubjectDef(Attr(el, "name")?.Trim() ?? "", NullIfEmpty(Attr(el, "short")));
        }

        var teachers = new Dictionary<string, AscTeacherDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in root.Element("teachers")?.Elements("teacher") ?? Enumerable.Empty<XElement>())
        {
            var id = Attr(el, "id"); if (string.IsNullOrEmpty(id)) continue;
            var full = Attr(el, "name")?.Trim() ?? "";
            var first = Attr(el, "firstname")?.Trim() ?? "";
            var last = Attr(el, "lastname")?.Trim() ?? "";
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(last))
            {
                var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2)
                {
                    if (string.IsNullOrEmpty(first)) first = parts[0];
                    if (string.IsNullOrEmpty(last)) last = string.Join(' ', parts.Skip(1));
                }
                else if (parts.Length == 1)
                {
                    if (string.IsNullOrEmpty(first)) first = parts[0];
                    if (string.IsNullOrEmpty(last)) last = parts[0];
                }
            }
            if (string.IsNullOrEmpty(first)) first = full;
            if (string.IsNullOrEmpty(last)) last = first;
            teachers[id] = new AscTeacherDef(first, last, full.Length > 0 ? full : $"{first} {last}");
        }

        var classes = new Dictionary<string, AscClassDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in root.Element("classes")?.Elements("class") ?? Enumerable.Empty<XElement>())
        {
            var id = Attr(el, "id"); if (string.IsNullOrEmpty(id)) continue;
            classes[id] = new AscClassDef(Attr(el, "name")?.Trim() ?? "", NullIfEmpty(Attr(el, "grade")));
        }

        var rooms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in root.Element("classrooms")?.Elements("classroom") ?? Enumerable.Empty<XElement>())
        {
            var id = Attr(el, "id"); if (string.IsNullOrEmpty(id)) continue;
            var name = Attr(el, "name")?.Trim();
            if (!string.IsNullOrEmpty(name)) rooms[id] = name;
        }

        var daysDefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in root.Element("daysdefs")?.Elements("daysdef") ?? Enumerable.Empty<XElement>())
        {
            var id = Attr(el, "id"); var days = Attr(el, "days");
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(days)) daysDefs[id] = days;
        }

        var lessons = new Dictionary<string, AscLessonDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in root.Element("lessons")?.Elements("lesson") ?? Enumerable.Empty<XElement>())
        {
            var id = Attr(el, "id"); if (string.IsNullOrEmpty(id)) continue;
            lessons[id] = new AscLessonDef(
                Attr(el, "subjectid") ?? "",
                Attr(el, "classids") ?? "",
                Attr(el, "teacherids") ?? "",
                NullIfEmpty(Attr(el, "classroomids")),
                NullIfEmpty(Attr(el, "daysdefid")));
        }

        var cards = new List<AscCard>();
        foreach (var el in root.Element("cards")?.Elements("card") ?? Enumerable.Empty<XElement>())
        {
            var lid = Attr(el, "lessonid"); if (string.IsNullOrEmpty(lid)) continue;
            if (!int.TryParse(Attr(el, "period"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p0) || p0 < 0) continue;

            var days = Attr(el, "days")?.Replace(" ", "").Trim();
            if (string.IsNullOrEmpty(days) && lessons.TryGetValue(lid, out var ldef) && ldef.DaysDefId != null)
                daysDefs.TryGetValue(ldef.DaysDefId, out days);
            if (string.IsNullOrEmpty(days)) continue;

            cards.Add(new AscCard(lid, p0, days!));
        }

        return new AscParsedFile
        {
            PeriodsCount = periodCount,
            Subjects = subjects,
            Teachers = teachers,
            Classes = classes,
            Rooms = rooms,
            Lessons = lessons,
            Cards = cards,
        };
    }

    /// <summary>قراءة نص الملف مع استشعار الترميز من ترويسة XML (يعالج windows-1256)</summary>
    public static string ReadXmlText(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var enc = Encoding.UTF8;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            enc = new UTF8Encoding(true);
        }
        else
        {
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 300));
            var m = Regex.Match(head, "encoding\\s*=\\s*[\"']([\\w-]+)[\"']", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                try { enc = Encoding.GetEncoding(m.Groups[1].Value); } catch { enc = Encoding.UTF8; }
            }
        }

        return enc.GetString(bytes);
    }

    /// <summary>
    /// قناع أيام بخمس خانات: "10000" الخانة 0 = الأحد … 4 = الخميس.
    /// يدعم الصيغة المفصولة بفواصل "10000,01000,..." باتحاد الأيام بلا تكرار.
    /// enums.DayOfWeek: السبت=0 الأحد=1 … الخميس=5.
    /// </summary>
    public static IEnumerable<DayOfWeek> ParseDayBits(string? daysMask)
    {
        if (string.IsNullOrWhiteSpace(daysMask)) yield break;

        var days = new List<DayOfWeek>();
        foreach (var part in daysMask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var clean = new string(part.Where(c => c == '0' || c == '1').ToArray());
            for (var i = 0; i < clean.Length && i < 5; i++)
            {
                if (clean[i] != '1') continue;
                var day = (DayOfWeek)(i + 1);
                if (!days.Contains(day)) days.Add(day);
            }
        }

        foreach (var d in days) yield return d;
    }

    public static List<string> SplitIds(string? ids) =>
        string.IsNullOrWhiteSpace(ids)
            ? new List<string>()
            : ids!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string? Attr(XElement el, string name) => el.Attribute(name)?.Value;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

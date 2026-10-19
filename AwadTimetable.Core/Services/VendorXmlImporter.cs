using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace AwadTimetable.Core.Services;

/// <summary>
/// مستورد XML للأنظمة المدرسية (Untis / WinLD / Vulcan / SchILD)، متسامح مع أسماء الوسوم
/// بالعربية والإنجليزية والألمانية والتشيكية والبولندية، ويدعم:
/// - حصص مجدولة (يوم + فترة) → حصص موضوعة.
/// - بيانات أساسية فقط (فصل/مادة/معلم/عدد حصص أسبوعي) → حصص غير موضوعة (Period = 0).
/// ملف aSc XML يُفوَّض إلى AscXmlReader.
/// </summary>
public static class VendorXmlImporter
{
    public const string Untis = "untis";
    public const string WinLd = "winld";
    public const string Vulcan = "vulcan";
    public const string Schild = "schild";

    private static readonly string[] LessonNames =
        { "lesson", "stunde", "hodina", "lekcja", "activity", "unterricht", "zajecia", "zajęcia" };

    private static readonly string[] TeacherNames = { "teacher", "lehrer", "učitel", "ucitel", "nauczyciel", "معلم" };
    private static readonly string[] ClassNames = { "class", "klasse", "třída", "trida", "klasa", "oddzial", "oddział", "فصل", "شعبة" };
    private static readonly string[] SubjectNames = { "subject", "fach", "předmět", "predmet", "przedmiot", "مادة" };
    private static readonly string[] RoomNames = { "room", "raum", "učebna", "ucebna", "sala", "قاعة" };
    private static readonly string[] DayNames = { "day", "tag", "den", "dzień", "dzien", "يوم" };
    private static readonly string[] PeriodNames = { "period", "stunde", "hodina", "lekcja", "hour", "فترة", "حصة" };
    private static readonly string[] WeeklyNames = { "periodsperweek", "weeklyperiods", "periods", "hours", "stunden", "godziny" };

    public static IReadOnlyList<LessonImportRow> Parse(string xml, string formatHint = Untis)
    {
        if (string.IsNullOrWhiteSpace(xml)) return Array.Empty<LessonImportRow>();

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return Array.Empty<LessonImportRow>(); }

        var root = doc.Root;
        if (root is null) return Array.Empty<LessonImportRow>();

        // aSc: تفويض للقارئ المخصص (بطاقات + دروس)
        if (string.Equals(root.Name.LocalName, "timetable", StringComparison.OrdinalIgnoreCase))
            return ParseAsc(xml);

        return ParseGeneric(root, formatHint);
    }

    private static IReadOnlyList<LessonImportRow> ParseAsc(string xml)
    {
        var parsed = AscXmlReader.Parse(xml);
        var result = new List<LessonImportRow>();

        foreach (var card in parsed.Cards)
        {
            if (!parsed.Lessons.TryGetValue(card.LessonId, out var lesson)) continue;

            var subject = parsed.Subjects.TryGetValue(lesson.SubjectId, out var s) ? s.Name : null;
            var classIds = AscXmlReader.SplitIds(lesson.ClassIds);
            var teacherIds = AscXmlReader.SplitIds(lesson.TeacherIds);
            if (string.IsNullOrWhiteSpace(subject) || classIds.Count == 0 || teacherIds.Count == 0) continue;

            var teacher = parsed.Teachers.TryGetValue(teacherIds[0], out var t) ? t.Full : teacherIds[0];
            string? room = null;
            if (lesson.RoomIds is not null)
            {
                var rid = AscXmlReader.SplitIds(lesson.RoomIds).FirstOrDefault();
                if (rid is not null && parsed.Rooms.TryGetValue(rid, out var rn)) room = rn;
            }

            foreach (var classId in classIds)
            {
                if (!parsed.Classes.TryGetValue(classId, out var c)) continue;

                foreach (var day in AscXmlReader.ParseDayBits(card.Days))
                {
                    result.Add(new LessonImportRow(c.Name, teacher, subject!, room, day, card.Period0 + 1));
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<LessonImportRow> ParseGeneric(XElement root, string formatHint)
    {
        var result = new List<LessonImportRow>();

        // مرشحو الدروس: العناصر المسمّاة كدرس أولاً (أدق)، وإلا heuristic بنيوي
        var named = root.DescendantsAndSelf()
            .Where(e => LessonNames.Any(k => e.Name.LocalName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var candidates = named.Count > 0
            ? named
                // استبعاد الحاويات (مثل <lessons>) التي تضم عناصر دروس أعمق
                .Where(e => !e.Descendants().Any(d =>
                    LessonNames.Any(k => d.Name.LocalName.Contains(k, StringComparison.OrdinalIgnoreCase))))
                .Where(e => HasChildOrAttribute(e, ClassNames) && HasChildOrAttribute(e, SubjectNames))
                .ToList()
            : root.DescendantsAndSelf()
                .Where(e => HasChildOrAttribute(e, ClassNames)
                            && HasChildOrAttribute(e, SubjectNames)
                            && HasChildOrAttribute(e, TeacherNames))
                // استبعاد الحاويات التي تضم دروساً أعمق
                .Where(e => !e.Descendants().Any(d =>
                    LessonNames.Any(k => d.Name.LocalName.Contains(k, StringComparison.OrdinalIgnoreCase))))
                .ToList();

        foreach (var el in candidates)
        {
            var teacher = FindValue(el, TeacherNames);
            var subject = FindValue(el, SubjectNames);
            var classText = FindValue(el, ClassNames);
            var room = FindValue(el, RoomNames);

            if (string.IsNullOrWhiteSpace(teacher) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(classText))
                continue;

            var weekly = TryParseInt(FindValue(el, WeeklyNames)) ?? 1;
            weekly = Math.Clamp(weekly, 1, 40);

            var dayText = FindValue(el, DayNames);
            var periodText = FindValue(el, PeriodNames);

            var classes = classText.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var hasDay = LessonCsvImporter.TryParseDay(dayText, out var day);
            var hasPeriod = LessonCsvImporter.TryParsePeriod(periodText, out var period);
            var hasSlot = hasDay && hasPeriod;

            foreach (var cls in classes)
            {
                if (hasSlot)
                {
                    result.Add(new LessonImportRow(cls, teacher!.Trim(), subject!.Trim(),
                        string.IsNullOrWhiteSpace(room) ? null : room.Trim(), day, period));
                }
                else
                {
                    // بيانات أساسية فقط: حصص غير موضوعة بعدد الحصص الأسبوعية
                    for (var i = 0; i < weekly; i++)
                    {
                        result.Add(new LessonImportRow(cls, teacher!.Trim(), subject!.Trim(),
                            string.IsNullOrWhiteSpace(room) ? null : room.Trim(),
                            AwadTimetable.Core.Enums.DayOfWeek.Saturday, 0));
                    }
                }
            }
        }

        return result;
    }

    private static bool HasChildOrAttribute(XElement el, string[] aliases)
        => el.Attributes().Any(a => Matches(a.Name.LocalName, aliases))
           || el.Descendants().Any(d => Matches(d.Name.LocalName, aliases));

    private static bool Matches(string name, string[] aliases)
        => aliases.Any(a => name.Contains(a, StringComparison.OrdinalIgnoreCase));

    private static string? FindValue(XElement el, string[] aliases)
    {
        // أولاً: سمة مباشرة
        var attr = el.Attributes().FirstOrDefault(a => Matches(a.Name.LocalName, aliases));
        if (attr is not null) return attr.Value;

        // ثانياً: عنصر ابن
        var child = el.Elements().FirstOrDefault(e => Matches(e.Name.LocalName, aliases));
        if (child is not null)
        {
            var nameAttr = child.Attribute("name")?.Value ?? child.Attribute("Name")?.Value;
            return string.IsNullOrWhiteSpace(child.Value) ? nameAttr : child.Value;
        }

        // ثالثاً: أي عنصر داخلي
        var deep = el.Descendants().FirstOrDefault(e => Matches(e.Name.LocalName, aliases));
        if (deep is not null)
        {
            var nameAttr = deep.Attribute("name")?.Value ?? deep.Attribute("Name")?.Value;
            return string.IsNullOrWhiteSpace(deep.Value) ? nameAttr : deep.Value;
        }

        return null;
    }

    private static int? TryParseInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}

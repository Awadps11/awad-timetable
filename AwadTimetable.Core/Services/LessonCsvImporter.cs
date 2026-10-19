using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Core.Services;

/// <summary>حصة مستوردة من ملف جدولي (CSV/Excel)</summary>
public sealed record LessonImportRow(
    string ClassName,
    string TeacherName,
    string SubjectName,
    string? RoomName,
    DomainDayOfWeek Day,
    int Period);

/// <summary>
/// مستورد جدولي عام للدروس (يغطي صادرات Untis / WinLD / Vulcan / SchILD وغيرها):
/// يكتشف الترويسة ويطابق أسماء الأعمدة بلغات متعددة (عربي/إنجليزي/ألماني/تشيكي/بولندي).
/// بلا ترويسة: الترتيب الافتراضي: الفصل، المعلم، المادة، اليوم، الفترة، القاعة.
/// </summary>
public static class LessonCsvImporter
{
    private static readonly string[] ClassAliases = { "class", "klasse", "třída", "trida", "oddział", "oddzial", "فصل", "الشعبة", "شعبة" };
    private static readonly string[] TeacherAliases = { "teacher", "lehrer", "učitel", "ucitel", "nauczyciel", "معلم", "المعلم" };
    private static readonly string[] SubjectAliases = { "subject", "fach", "předmět", "predmet", "przedmiot", "مادة", "المادة" };
    private static readonly string[] RoomAliases = { "room", "raum", "učebna", "ucebna", "sala", "قاعة", "القاعة", "غرفة" };
    private static readonly string[] DayAliases = { "day", "tag", "den", "dzień", "dzien", "يوم", "اليوم" };
    private static readonly string[] PeriodAliases = { "period", "stunde", "hodina", "lekcja", "فترة", "الفترة", "حصة" };

    public static IReadOnlyList<LessonImportRow> ParseCsv(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<LessonImportRow>();

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (lines.Count == 0) return Array.Empty<LessonImportRow>();

        var delimiter = DetectDelimiter(lines[0]);
        var rows = lines.Select(l => SubjectListImporter.SplitLine(l, delimiter)).ToList();
        return FromRows(rows);
    }

    public static IReadOnlyList<LessonImportRow> ParseXlsx(Stream stream)
        => FromRows(SubjectListImporter.ReadXlsxRows(stream));

    public static IReadOnlyList<LessonImportRow> FromRows(IReadOnlyList<string[]> rows)
    {
        var list = rows
            .Select(r => r.Select(c => (c ?? string.Empty).Trim()).ToArray())
            .Where(r => r.Any(c => c.Length > 0))
            .ToList();

        if (list.Count == 0) return Array.Empty<LessonImportRow>();

        var map = BuildHeaderMap(list[0]);
        var hasHeader = map.Count >= 3;
        var start = hasHeader ? 1 : 0;

        if (!hasHeader)
        {
            map = new Dictionary<string, int>
            {
                ["class"] = 0, ["teacher"] = 1, ["subject"] = 2, ["day"] = 3, ["period"] = 4, ["room"] = 5,
            };
        }

        var result = new List<LessonImportRow>();

        for (var i = start; i < list.Count; i++)
        {
            var r = list[i];
            string? Get(string key) => map.TryGetValue(key, out var idx) && idx < r.Length ? r[idx] : null;

            var className = Get("class");
            var teacher = Get("teacher");
            var subject = Get("subject");
            if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(teacher) || string.IsNullOrWhiteSpace(subject))
                continue;

            if (!TryParseDay(Get("day"), out var day)) continue;
            if (!TryParsePeriod(Get("period"), out var period)) continue;

            result.Add(new LessonImportRow(
                className!.Trim(),
                teacher!.Trim(),
                subject!.Trim(),
                string.IsNullOrWhiteSpace(Get("room")) ? null : Get("room")!.Trim(),
                day,
                period));
        }

        return result;
    }

    private static Dictionary<string, int> BuildHeaderMap(string[] header)
    {
        var map = new Dictionary<string, int>();

        for (var i = 0; i < header.Length; i++)
        {
            var h = header[i].Trim().Trim('"').ToLowerInvariant();
            if (h.Length == 0) continue;

            string? key = null;
            if (ClassAliases.Any(a => h.Contains(a, StringComparison.OrdinalIgnoreCase))) key = "class";
            else if (TeacherAliases.Any(a => h.Contains(a, StringComparison.OrdinalIgnoreCase))) key = "teacher";
            else if (SubjectAliases.Any(a => h.Contains(a, StringComparison.OrdinalIgnoreCase))) key = "subject";
            else if (RoomAliases.Any(a => h.Contains(a, StringComparison.OrdinalIgnoreCase))) key = "room";
            else if (DayAliases.Any(a => h.Contains(a, StringComparison.OrdinalIgnoreCase))) key = "day";
            else if (PeriodAliases.Any(a => h.Contains(a, StringComparison.OrdinalIgnoreCase))) key = "period";

            if (key is not null && !map.ContainsKey(key)) map[key] = i;
        }

        return map;
    }

    public static bool TryParseDay(string? text, out DomainDayOfWeek day)
    {
        day = DomainDayOfWeek.Saturday;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var t = text.Trim().ToLowerInvariant();

        var names = new (string[] Aliases, DomainDayOfWeek Day)[]
        {
            (new[] { "سبت", "saturday", "sat", "samstag", "sobota" }, DomainDayOfWeek.Saturday),
            (new[] { "أحد", "احد", "sunday", "sun", "sonntag", "neděle", "nedele", "niedziela" }, DomainDayOfWeek.Sunday),
            (new[] { "إثنين", "اثنين", "monday", "mon", "montag", "pondělí", "pondeli", "poniedziałek" }, DomainDayOfWeek.Monday),
            (new[] { "ثلاثاء", "tuesday", "tue", "dienstag", "úterý", "utery", "wtorek" }, DomainDayOfWeek.Tuesday),
            (new[] { "أربعاء", "اربعاء", "wednesday", "wed", "mittwoch", "středa", "streda", "środa" }, DomainDayOfWeek.Wednesday),
            (new[] { "خميس", "thursday", "thu", "donnerstag", "čtvrtek", "ctvrtek", "czwartek" }, DomainDayOfWeek.Thursday),
            (new[] { "جمعة", "friday", "fri", "freitag", "pátek", "patek", "piątek" }, DomainDayOfWeek.Friday),
        };

        foreach (var (aliases, value) in names)
        {
            if (aliases.Any(a => t.Contains(a, StringComparison.OrdinalIgnoreCase)))
            {
                day = value;
                return true;
            }
        }

        if (int.TryParse(new string(t.Where(char.IsAsciiDigit).ToArray()), out var num) && num is >= 0 and <= 6)
        {
            day = (DomainDayOfWeek)num;
            return true;
        }

        return false;
    }

    public static bool TryParsePeriod(string? text, out int period)
    {
        period = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return false;

        // بعض الأنظمة ترقّم الفترات من 0
        if (n == 0) n = 1;
        if (n is < 1 or > 12) return false;

        period = n;
        return true;
    }

    private static char DetectDelimiter(string header)
    {
        var comma = header.Count(c => c == ',');
        var semi = header.Count(c => c == ';');
        var tab = header.Count(c => c == '\t');

        if (tab >= comma && tab >= semi && tab > 0) return '\t';
        if (semi > comma) return ';';
        return ',';
    }
}

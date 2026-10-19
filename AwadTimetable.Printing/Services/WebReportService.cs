using System.Text;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Printing.Templates;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Printing.Services;

/// <summary>خيارات تقرير الويب (HTML)</summary>
public sealed class WebReportOptions
{
    public string SchoolName { get; init; } = string.Empty;
    public string ProductName { get; init; } = "نظام عوض للجداول المدرسية";
}

/// <summary>
/// توليد تقرير ويب HTML كامل بأسلوب aSc Timetables:
/// صفحة واحدة تتضمن نظرة عامة + جدول أسبوعي لكل فصل + جدول أسبوعي لكل معلم،
/// بتصاميم ألوان المواد وتنقّل داخلي وترويسة/تذييل.
/// </summary>
public interface IWebReportService
{
    string GenerateHtml(
        Timetable timetable,
        WebReportOptions options,
        Func<Lesson, (string Subject, string Teacher, string Class, string Room)> resolveNames,
        PrintTemplate? template = null);
}

public sealed class WebReportService : IWebReportService
{
    private static readonly string[] DayArabic =
        { "السبت", "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    public string GenerateHtml(
        Timetable timetable,
        WebReportOptions options,
        Func<Lesson, (string Subject, string Teacher, string Class, string Room)> resolveNames,
        PrintTemplate? template = null)
    {
        var tpl = template ?? PrintTemplatePresets.Default;
        var lessons = timetable.Lessons.ToList();

        var days = Enum.GetValues<DomainDayOfWeek>()
            .Where(d => lessons.Count == 0 || lessons.Any(l => l.Day == d))
            .ToList();
        if (days.Count == 0)
            days.Add(DomainDayOfWeek.Saturday);

        var maxPeriod = lessons.Count == 0
            ? 8
            : Math.Clamp((int)lessons.Max(l => l.Period.Value), 8, 24);

        var classGroups = lessons
            .GroupBy(l => resolveNames(l).Class)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderBy(g => g.Key, StringComparer.CurrentCulture)
            .ToList();

        var teacherGroups = lessons
            .GroupBy(l => resolveNames(l).Teacher)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderBy(g => g.Key, StringComparer.CurrentCulture)
            .ToList();

        var roomGroups = lessons
            .GroupBy(l => resolveNames(l).Room)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderBy(g => g.Key, StringComparer.CurrentCulture)
            .ToList();

        var subjectGroups = lessons
            .GroupBy(l => resolveNames(l).Subject)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderByDescending(g => g.Count())
            .ToList();

        var title = string.IsNullOrWhiteSpace(timetable.Name) ? "الجدول الأسبوعي" : timetable.Name;
        var sb = new StringBuilder(64 * 1024);

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html dir=\"rtl\" lang=\"ar\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{Esc(title)}</title>");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine(BuildCss(tpl));
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // ===== الترويسة =====
        sb.AppendLine("<header>");
        var logo = BuildLogoTag(tpl);
        if (logo is not null)
            sb.AppendLine(logo);
        if (!string.IsNullOrWhiteSpace(options.SchoolName))
            sb.AppendLine($"<div class=\"school\">{Esc(options.SchoolName)}</div>");
        sb.AppendLine($"<h1>{Esc(title)}</h1>");
        sb.AppendLine($"<div class=\"meta\">العام الدراسي {timetable.AcademicYearStart}/{timetable.AcademicYearEnd} — الفصل {timetable.TermNumber}</div>");
        sb.AppendLine($"<div class=\"meta\">{lessons.Count} حصة — {classGroups.Count} فصل — {teacherGroups.Count} معلم</div>");
        sb.AppendLine("</header>");

        // ===== شريط التنقل =====
        if (tpl.ShowLegend)
        {
            sb.AppendLine("<nav class=\"tabs\">");
            sb.AppendLine("<a href=\"#overview\">نظرة عامة</a>");
            sb.AppendLine($"<a href=\"#classes\">الفصول ({classGroups.Count})</a>");
            sb.AppendLine($"<a href=\"#teachers\">المعلمون ({teacherGroups.Count})</a>");
            if (roomGroups.Count > 0) sb.AppendLine($"<a href=\"#rooms\">القاعات ({roomGroups.Count})</a>");
            if (tpl.ShowSummary) sb.AppendLine("<a href=\"#summary\">الملخص</a>");
            sb.AppendLine("</nav>");
        }

        Func<int, string?>? timeLabel = tpl.ShowPeriodTimes
            ? p => timetable.GetPeriodTimeForDisplay(p)?.RangeText
            : null;

        sb.AppendLine("<main>");

        // ===== نظرة عامة: المصفوفة الكاملة (كل الحصص لكل فترات اليوم) =====
        sb.AppendLine("<section id=\"overview\">");
        sb.AppendLine("<h2>نظرة عامة — كل الحصص</h2>");
        AppendGrid(sb, days, maxPeriod,
            (d, p) => lessons.Where(l => l.Day == d && l.Period.Value == p).ToList(),
            l => tpl.ShowClass ? resolveNames(l).Class : resolveNames(l).Subject,
            l => resolveNames(l).Subject,
            l => tpl.ShowTeacher ? resolveNames(l).Teacher : null,
            timeLabel,
            tpl.CellStyle, tpl.MergeConsecutive);
        sb.AppendLine("</section>");

        // ===== جداول الفصول =====
        sb.AppendLine("<section id=\"classes\">");
        sb.AppendLine("<h2>جداول الفصول</h2>");
        foreach (var group in classGroups)
        {
            var classLessons = group.ToList();
            sb.AppendLine($"<h3>{Esc(group.Key)}</h3>");
            AppendGrid(sb, days, maxPeriod,
                (d, p) => classLessons.Where(l => l.Day == d && l.Period.Value == p).ToList(),
                l => resolveNames(l).Subject,
                l => tpl.ShowTeacher ? resolveNames(l).Teacher : string.Empty,
                l => tpl.ShowRoom && !string.IsNullOrWhiteSpace(resolveNames(l).Room) ? resolveNames(l).Room : null,
                timeLabel,
                tpl.CellStyle, tpl.MergeConsecutive);
        }
        sb.AppendLine("</section>");

        // ===== جداول المعلمين =====
        sb.AppendLine("<section id=\"teachers\">");
        sb.AppendLine("<h2>جداول المعلمين</h2>");
        foreach (var group in teacherGroups)
        {
            var teacherLessons = group.ToList();
            sb.AppendLine($"<h3>{Esc(group.Key)}</h3>");
            AppendGrid(sb, days, maxPeriod,
                (d, p) => teacherLessons.Where(l => l.Day == d && l.Period.Value == p).ToList(),
                l => tpl.ShowClass ? resolveNames(l).Class : resolveNames(l).Subject,
                l => resolveNames(l).Subject,
                null,
                timeLabel,
                tpl.CellStyle, tpl.MergeConsecutive);
        }
        sb.AppendLine("</section>");

        // ===== جداول القاعات =====
        if (roomGroups.Count > 0)
        {
            sb.AppendLine("<section id=\"rooms\">");
            sb.AppendLine("<h2>جداول القاعات</h2>");
            foreach (var group in roomGroups)
            {
                var roomLessons = group.ToList();
                sb.AppendLine($"<h3>{Esc(group.Key)}</h3>");
                AppendGrid(sb, days, maxPeriod,
                    (d, p) => roomLessons.Where(l => l.Day == d && l.Period.Value == p).ToList(),
                    l => resolveNames(l).Class,
                    l => resolveNames(l).Subject,
                    l => resolveNames(l).Teacher,
                    timeLabel,
                    tpl.CellStyle, tpl.MergeConsecutive);
            }
            sb.AppendLine("</section>");
        }

        // ===== تقرير الملخص =====
        if (tpl.ShowSummary)
        {
            sb.AppendLine("<section id=\"summary\">");
            sb.AppendLine("<h2>تقرير الملخص</h2>");
            sb.AppendLine("<div class=\"summary-grid\">");
            AppendSummaryTable(sb, "حصص المعلمين (أسبوعياً)", teacherGroups.Select(g => (g.Key, g.Count())));
            AppendSummaryTable(sb, "حصص الفصول", classGroups.Select(g => (g.Key, g.Count())));
            AppendSummaryTable(sb, "حصص المواد", subjectGroups.Select(g => (g.Key, g.Count())));
            sb.AppendLine("</div>");
            sb.AppendLine("</section>");
        }

        sb.AppendLine("</main>");

        // ===== التذييل =====
        sb.AppendLine("<footer>");
        sb.AppendLine($"<div>{Esc(options.ProductName)}</div>");
        sb.AppendLine($"<div>تم إنشاء التقرير في {DateTime.Now:yyyy-MM-dd HH:mm}</div>");
        sb.AppendLine("</footer>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    /// <summary>جدول أسبوعي: صفوف = أيام (يمين)، أعمدة = فترات مرقّمة من اليمين — بأسلوب معاينة aSc</summary>
    private static void AppendGrid(
        StringBuilder sb,
        IReadOnlyList<DomainDayOfWeek> days,
        int maxPeriod,
        Func<DomainDayOfWeek, int, IReadOnlyList<Lesson>> cellQuery,
        Func<Lesson, string> primary,
        Func<Lesson, string> secondary,
        Func<Lesson, string?>? tertiary = null,
        Func<int, string?>? periodTime = null,
        PrintCellStyle cellStyle = PrintCellStyle.Filled,
        bool mergeConsecutive = false)
    {
        sb.AppendLine("<table class=\"tt\">");
        sb.AppendLine("<thead><tr><th class=\"corner\">اليوم / الفترة</th>");
        for (var p = 1; p <= maxPeriod; p++)
        {
            var name = PeriodNames.GetFullName(p);
            var time = periodTime?.Invoke(p);
            sb.AppendLine($"<th><span class=\"pn\">{p}</span><br><span class=\"pname\">{Esc(name)}</span>" +
                          (string.IsNullOrWhiteSpace(time) ? "" : $"<br><span class=\"ptime\">{Esc(time)}</span>") +
                          "</th>");
        }
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var day in days)
        {
            sb.AppendLine($"<tr><th class=\"day\">{Esc(DayArabic[(int)day])}</th>");

            var p = 1;
            while (p <= maxPeriod)
            {
                var cellLessons = cellQuery(day, p);

                // دمج الحصص الفردية المتتالية المتماثلة (طباعة فقط)
                if (mergeConsecutive && cellLessons.Count == 1)
                {
                    var key = CellKey(cellLessons[0], primary, secondary, tertiary);
                    var span = 1;

                    while (p + span <= maxPeriod)
                    {
                        var next = cellQuery(day, p + span);
                        if (next.Count != 1 || CellKey(next[0], primary, secondary, tertiary) != key)
                            break;
                        span++;
                    }

                    AppendCell(sb, cellLessons[0], primary, secondary, tertiary, cellStyle, span);
                    p += span;
                    continue;
                }

                sb.Append("<td>");
                foreach (var lesson in cellLessons)
                    AppendChip(sb, lesson, primary, secondary, tertiary, cellStyle);
                sb.AppendLine("</td>");
                p++;
            }

            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");
    }

    private static string CellKey(
        Lesson lesson,
        Func<Lesson, string> primary,
        Func<Lesson, string> secondary,
        Func<Lesson, string?>? tertiary)
        => primary(lesson) + "\u0001" + secondary(lesson) + "\u0001" + (tertiary?.Invoke(lesson) ?? string.Empty);

    private static void AppendCell(
        StringBuilder sb,
        Lesson lesson,
        Func<Lesson, string> primary,
        Func<Lesson, string> secondary,
        Func<Lesson, string?>? tertiary,
        PrintCellStyle cellStyle,
        int colspan)
    {
        sb.Append(colspan > 1 ? $"<td colspan=\"{colspan}\">" : "<td>");
        AppendChip(sb, lesson, primary, secondary, tertiary, cellStyle);
        sb.AppendLine("</td>");
    }

    private static void AppendChip(
        StringBuilder sb,
        Lesson lesson,
        Func<Lesson, string> primary,
        Func<Lesson, string> secondary,
        Func<Lesson, string?>? tertiary,
        PrintCellStyle cellStyle)
    {
        var color = SubjectColor(lesson);
        var p1 = primary(lesson);
        var p2 = secondary(lesson);
        var p3 = tertiary?.Invoke(lesson);

        var chipStyle = cellStyle switch
        {
            PrintCellStyle.Outlined => $"background:#fff;color:{color};border:1px solid {color}",
            PrintCellStyle.Minimal => $"background:transparent;color:{color}",
            _ => $"background:{color};color:#fff",
        };

        sb.Append($"<div class=\"chip\" style=\"{chipStyle}\">");
        sb.Append($"<b>{Esc(p1)}</b>");
        if (!string.IsNullOrWhiteSpace(p2))
            sb.Append($"<span>{Esc(p2)}</span>");
        if (!string.IsNullOrWhiteSpace(p3))
            sb.Append($"<small>{Esc(p3)}</small>");
        sb.Append("</div>");
    }

    /// <summary>وسم شعار المدرسة في الترويسة (base64 ليعمل دون شبكة/ملفات خارجية)</summary>
    private static string? BuildLogoTag(PrintTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.LogoPath) || !File.Exists(template.LogoPath))
            return null;

        try
        {
            var bytes = File.ReadAllBytes(template.LogoPath);
            var ext = Path.GetExtension(template.LogoPath).ToLowerInvariant();
            var mime = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                _ => "image/png",
            };
            return $"<img class=\"logo\" alt=\"logo\" src=\"data:{mime};base64,{Convert.ToBase64String(bytes)}\">";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>جدول ملخص: اسم + عدد الحصص، مرتّب تنازلياً</summary>
    private static void AppendSummaryTable(StringBuilder sb, string heading, IEnumerable<(string Name, int Count)> rows)
    {
        var list = rows.OrderByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        sb.AppendLine("<div class=\"summary-card\">");
        sb.AppendLine($"<h4>{Esc(heading)}</h4>");
        sb.AppendLine("<table class=\"sum\"><tbody>");
        foreach (var (name, count) in list)
            sb.AppendLine($"<tr><td>{Esc(name)}</td><td class=\"n\">{count}</td></tr>");
        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</div>");
    }

    /// <summary>لون مستقر للمادة: من الكيان إن وُجد، وإلا توليد لون ثابت من الاسم (بديل ألوان aSc)</summary>
    private static string SubjectColor(Lesson lesson)
    {
        var hex = lesson.Subject?.ColorHex;
        if (!string.IsNullOrWhiteSpace(hex))
        {
            var h = hex.Trim();
            if (!h.StartsWith('#')) h = "#" + h;
            if (h.Length == 7) return h;
        }
        var name = lesson.Subject?.Name ?? lesson.SubjectId?.ToString() ?? "x";
        var hash = 0;
        foreach (var ch in name)
            hash = unchecked(hash * 31 + ch);
        var hue = Math.Abs(hash) % 360;
        return HslToHex(hue, 0.55, 0.38);
    }

    private static string HslToHex(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }
        byte V(double v) => (byte)Math.Clamp((int)Math.Round((v + m) * 255), 0, 255);
        return $"#{V(r1):X2}{V(g1):X2}{V(b1):X2}";
    }

    private static string Esc(string? text) =>
        System.Security.SecurityElement.Escape(text ?? string.Empty) ?? string.Empty;

    /// <summary>يولّد CSS من القالب: ألوان المتغيّرات، الخط، حجم الصفحة، والتخطيط.</summary>
    private static string BuildCss(PrintTemplate t)
    {
        var headerText = IsLight(t.HeaderColor) ? t.TextColor : "#FFFFFF";
        var dayText = IsLight(t.DayColor) ? t.TextColor : "#FFFFFF";
        var page = t.PageSize switch
        {
            PrintPageSize.A3 => "A3",
            PrintPageSize.Letter => "letter",
            PrintPageSize.Legal => "legal",
            _ => "A4",
        };
        var orient = t.Orientation == PrintOrientation.Landscape ? "landscape" : "portrait";
        var row1 = t.Zebra ? t.RowColor1 : "#FFFFFF";
        var row2 = t.Zebra ? t.RowColor2 : "#FFFFFF";
        var border = t.ShowGridLines ? "1px solid #cfd8e8" : "none";

        var sb = new StringBuilder(4096);
        sb.AppendLine("<style>");
        sb.AppendLine($"body {{ font-family:\"{t.FontFamily}\",Tahoma,Arial,sans-serif; font-size:{t.FontSize}px; margin:0; background:#f4f7fb; color:{t.TextColor}; }}");
        sb.AppendLine("* { box-sizing:border-box; }");
        sb.AppendLine($"header {{ background:{t.HeaderColor}; color:{headerText}; padding:16px 18px; text-align:center; }}");
        sb.AppendLine("header .logo { max-height:64px; margin:0 auto 6px; display:block; }");
        sb.AppendLine("header .school { font-size:14px; opacity:.95; margin-bottom:4px; }");
        sb.AppendLine("header h1 { margin:0; font-size:20px; }");
        sb.AppendLine("header .meta { opacity:.9; margin-top:6px; font-size:12px; }");
        sb.AppendLine($"nav.tabs {{ display:flex; gap:8px; flex-wrap:wrap; justify-content:center; background:#fff; border-bottom:2px solid #d7e3f4; padding:10px; position:sticky; top:0; z-index:5; }}");
        sb.AppendLine($"nav.tabs a {{ color:{t.AccentColor}; text-decoration:none; border:1px solid #c9d9f0; border-radius:6px; padding:6px 12px; background:#f0f6ff; font-weight:600; }}");
        sb.AppendLine($"nav.tabs a:hover {{ background:{t.AccentColor}; color:#fff; }}");
        sb.AppendLine("main { padding:16px; max-width:1280px; margin:0 auto; }");
        sb.AppendLine("section { background:#fff; border-radius:10px; box-shadow:0 1px 4px rgba(0,0,0,.08); padding:14px; margin-bottom:18px; }");
        sb.AppendLine($"h2 {{ margin:0 0 10px; color:{t.AccentColor}; border-bottom:1px solid #e3ecf9; padding-bottom:6px; font-size:16px; }}");
        sb.AppendLine("h3 { margin:14px 0 6px; color:#234; font-size:14px; }");
        sb.AppendLine("table.tt { border-collapse:collapse; width:100%; }");
        sb.AppendLine($"table.tt th {{ background:{t.DayColor}; color:{dayText}; padding:6px; border:1px solid {t.DayColor}; font-weight:600; }}");
        sb.AppendLine("table.tt th.corner { min-width:84px; }");
        sb.AppendLine("table.tt th.day { white-space:nowrap; min-width:70px; }");
        sb.AppendLine("table.tt th .pn { font-size:13px; }");
        sb.AppendLine("table.tt th .pname { font-size:10px; opacity:.9; font-weight:400; }");
        sb.AppendLine("table.tt th .ptime { display:block; font-size:9px; opacity:.75; font-weight:400; }");
        sb.AppendLine($"table.tt td {{ border:{border}; padding:4px; vertical-align:middle; text-align:center; }}");
        sb.AppendLine($"table.tt tr:nth-child(even) td {{ background:{row1}; }}");
        sb.AppendLine($"table.tt tr:nth-child(odd) td {{ background:{row2}; }}");
        sb.AppendLine(".chip { border-radius:4px; padding:3px 4px; margin:2px 0; font-size:11px; line-height:1.35; color:#fff; }");
        sb.AppendLine(".chip b { display:block; font-size:12px; }");
        sb.AppendLine(".chip span { display:block; opacity:.95; }");
        sb.AppendLine(".chip small { display:block; opacity:.85; font-size:10px; }");
        sb.AppendLine(".summary-grid { display:flex; flex-wrap:wrap; gap:14px; }");
        sb.AppendLine(".summary-card { flex:1 1 260px; min-width:240px; }");
        sb.AppendLine($"table.sum {{ border-collapse:collapse; width:100%; }}");
        sb.AppendLine("table.sum td { border:1px solid #dbe4f2; padding:4px 8px; }");
        sb.AppendLine($"table.sum td.n {{ width:56px; text-align:center; font-weight:700; color:{t.AccentColor}; background:#f2f7ff; }}");
        sb.AppendLine("footer { text-align:center; color:#678; padding:10px 0 24px; font-size:11px; }");
        sb.AppendLine($"@page {{ size:{page} {orient}; margin:10mm; }}");
        sb.AppendLine("@media print { nav.tabs { display:none; } section { box-shadow:none; page-break-inside:avoid; } body { background:#fff; } }");
        sb.AppendLine("</style>");
        return sb.ToString();
    }

    private static bool IsLight(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6) return false;

        try
        {
            var r = Convert.ToInt32(h.Substring(0, 2), 16);
            var g = Convert.ToInt32(h.Substring(2, 2), 16);
            var b = Convert.ToInt32(h.Substring(4, 2), 16);
            var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
            return luminance > 0.65;
        }
        catch
        {
            return false;
        }
    }

    private const string Css = """
<style>
:root { --th:#4B6BAC; --row1:#E9F7FF; --row2:#D3E8FC; }
* { box-sizing:border-box; }
body { font-family:"Segoe UI",Tahoma,Arial,sans-serif; font-size:12px; margin:0; background:#f4f7fb; color:#123; }
header { background:var(--th); color:#fff; padding:16px 18px; text-align:center; }
header .school { font-size:14px; opacity:.95; margin-bottom:4px; }
header h1 { margin:0; font-size:20px; }
header .meta { opacity:.9; margin-top:6px; font-size:12px; }
nav.tabs { display:flex; gap:8px; flex-wrap:wrap; justify-content:center; background:#fff; border-bottom:2px solid #d7e3f4; padding:10px; position:sticky; top:0; z-index:5; }
nav.tabs a { color:var(--th); text-decoration:none; border:1px solid #c9d9f0; border-radius:6px; padding:6px 12px; background:#f0f6ff; font-weight:600; }
nav.tabs a:hover { background:var(--th); color:#fff; }
main { padding:16px; max-width:1280px; margin:0 auto; }
section { background:#fff; border-radius:10px; box-shadow:0 1px 4px rgba(0,0,0,.08); padding:14px; margin-bottom:18px; }
h2 { margin:0 0 10px; color:var(--th); border-bottom:1px solid #e3ecf9; padding-bottom:6px; font-size:16px; }
h3 { margin:14px 0 6px; color:#234; font-size:14px; }
table.tt { border-collapse:collapse; width:100%; }
table.tt th { background:var(--th); color:#fff; padding:6px; border:1px solid #3d5990; font-weight:600; }
table.tt th.corner { min-width:84px; }
table.tt th.day { white-space:nowrap; min-width:70px; }
table.tt th .pn { font-size:13px; }
table.tt th .pname { font-size:10px; opacity:.9; font-weight:400; }
table.tt th .ptime { display:block; font-size:9px; opacity:.75; font-weight:400; }
table.tt td { border:1px solid #cfd8e8; padding:4px; vertical-align:middle; text-align:center; }
table.tt tr:nth-child(even) td { background:var(--row1); }
table.tt tr:nth-child(odd) td { background:var(--row2); }
.chip { border-radius:4px; padding:3px 4px; margin:2px 0; color:#fff; font-size:11px; line-height:1.35; }
.chip b { display:block; font-size:12px; }
.chip span { display:block; opacity:.95; }
.chip small { display:block; opacity:.85; font-size:10px; }
.summary-grid { display:flex; flex-wrap:wrap; gap:14px; }
.summary-card { flex:1 1 260px; min-width:240px; }
.summary-card h4 { margin:6px 0; color:var(--th); font-size:13px; }
table.sum { border-collapse:collapse; width:100%; }
table.sum td { border:1px solid #dbe4f2; padding:4px 8px; }
table.sum td.n { width:56px; text-align:center; font-weight:700; color:var(--th); background:#f2f7ff; }
footer { text-align:center; color:#678; padding:10px 0 24px; font-size:11px; }
footer div { margin-top:2px; }
@media print {
  nav.tabs { display:none; }
  section { box-shadow:none; page-break-inside:avoid; }
  body { background:#fff; }
}
</style>
""";
}

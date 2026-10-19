using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Printing.Services;

/// <summary>
/// خدمة الطباعة والتصدير
/// </summary>
public interface IPrintService
{
    Task<byte[]> GeneratePdfAsync(Timetable timetable, PrintOptions options);
    Task<byte[]> GenerateTeacherSchedulePdfAsync(Teacher teacher, Timetable timetable, PrintOptions options);
    Task<byte[]> GenerateClassSchedulePdfAsync(Class @class, Timetable timetable, PrintOptions options);
    Task<byte[]> GenerateStudentSchedulePdfAsync(Class @class, Timetable timetable, PrintOptions options);
    Task<byte[]> GenerateRoomSchedulePdfAsync(Room room, Timetable timetable, PrintOptions options);
    Task<string> GenerateHtmlAsync(Timetable timetable, PrintOptions options);
}

/// <summary>
/// خيارات الطباعة
/// </summary>
public sealed class PrintOptions
{
    public string SchoolName { get; init; } = string.Empty;
    public string? LogoPath { get; init; }
    public string WatermarkText { get; init; } = string.Empty;
    public string CopyrightText { get; init; } = string.Empty;
    public string FontFamily { get; init; } = "Segoe UI";
    public int FontSize { get; init; } = 8;
    public float MarginTop { get; init; } = 20f;
    public float MarginBottom { get; init; } = 20f;
    public float MarginLeft { get; init; } = 20f;
    public float MarginRight { get; init; } = 20f;
    public bool ShowWatermark { get; init; } = true;
    public bool ShowGridLines { get; init; } = true;
    public PrintOrientation Orientation { get; init; } = PrintOrientation.Landscape;
    public PrintPageSize PageSize { get; init; } = PrintPageSize.A4;
}

public enum PrintOrientation
{
    Portrait,
    Landscape
}

public enum PrintPageSize
{
    A4,
    A3,
    Letter,
    Legal
}

/// <summary>
/// خدمة الطباعة باستخدام QuestPDF
/// </summary>
public class PrintService : IPrintService
{
    private static readonly string[] DayArabic =
        { "السبت", "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    public PrintService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static PageSize ResolveSize(PrintOptions options)
    {
        var size = options.PageSize switch
        {
            PrintPageSize.A3 => PageSizes.A3,
            PrintPageSize.Letter => PageSizes.Letter,
            PrintPageSize.Legal => PageSizes.Legal,
            _ => PageSizes.A4
        };
        return options.Orientation == PrintOrientation.Landscape ? size.Landscape() : size.Portrait();
    }

    public async Task<byte[]> GeneratePdfAsync(Timetable timetable, PrintOptions options)
    {
        return await Task.Run(() =>
        {
            var document = CreateTimetableDocument(timetable, options);
            return document.GeneratePdf();
        });
    }

    public async Task<byte[]> GenerateTeacherSchedulePdfAsync(Teacher teacher, Timetable timetable, PrintOptions options)
    {
        return await Task.Run(() =>
        {
            var document = CreateTeacherDocument(teacher, timetable, options);
            return document.GeneratePdf();
        });
    }

    public async Task<byte[]> GenerateClassSchedulePdfAsync(Class @class, Timetable timetable, PrintOptions options)
    {
        return await Task.Run(() =>
        {
            var document = CreateClassDocument(@class, timetable, options);
            return document.GeneratePdf();
        });
    }

    public async Task<byte[]> GenerateStudentSchedulePdfAsync(Class @class, Timetable timetable, PrintOptions options)
    {
        return await GenerateClassSchedulePdfAsync(@class, timetable, options);
    }

    public async Task<byte[]> GenerateRoomSchedulePdfAsync(Room room, Timetable timetable, PrintOptions options)
    {
        return await Task.Run(() =>
        {
            var lessons = timetable.Lessons.Where(l => l.RoomId is not null && l.RoomId == room.Id).ToList();

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(ResolveSize(options));
                    page.MarginTop(options.MarginTop);
                    page.MarginRight(options.MarginRight);
                    page.MarginBottom(options.MarginBottom);
                    page.MarginLeft(options.MarginLeft);
                    page.DefaultTextStyle(x => x.FontFamily(options.FontFamily).FontSize(options.FontSize));

                    page.Header().Element(c =>
                    {
                        c.Column(col =>
                        {
                            col.Item().Text($"جدول القاعة: {room.Name}").FontSize(16).Bold().AlignCenter();
                            col.Item().Text($"السعة: {room.Capacity}" +
                                            (string.IsNullOrWhiteSpace(room.Building) ? "" : $" — المبنى: {room.Building}"))
                                .FontSize(11).AlignCenter().FontColor(Colors.Grey.Medium);
                            col.Item().PaddingTop(4).AlignCenter().LineHorizontal(1).LineColor(Colors.Blue.Medium);
                        });
                    });

                    page.Content().Element(c => DrawClassSchedule(c, lessons, options));
                    page.Footer().Element(DrawFooter);
                });
            });

            return document.GeneratePdf();
        });
    }

    public async Task<string> GenerateHtmlAsync(Timetable timetable, PrintOptions options)
    {
        return await Task.Run(() =>
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html dir=\"rtl\"><head><meta charset=\"utf-8\">");
            sb.AppendLine($"<title>{System.Security.SecurityElement.Escape(timetable.Name)}</title>");
            sb.AppendLine("<style>body{font-family:Segoe UI,Arial;direction:rtl}table{border-collapse:collapse;width:100%}th,td{border:1px solid #666;padding:6px;text-align:center;font-size:13px}th{background:#2563EB;color:#fff}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine($"<h2>{System.Security.SecurityElement.Escape(timetable.Name)}</h2>");
            sb.AppendLine("<table><tr><th>الفترة</th>");
            foreach (var d in Enum.GetValues<DomainDayOfWeek>()) sb.AppendLine($"<th>{DayArabic[(int)d]}</th>");
            sb.AppendLine("</tr>");
            for (var p = 1; p <= 12; p++)
            {
                var t = timetable.GetPeriodTimeForDisplay(p);
                var label = t == null ? PeriodNames.GetFullName(p)
                    : $"{PeriodNames.GetFullName(p)} ({t.RangeText})";
                sb.AppendLine($"<tr><th>{System.Security.SecurityElement.Escape(label)}</th>");
                foreach (var d in Enum.GetValues<DomainDayOfWeek>())
                {
                    var lesson = timetable.Lessons.FirstOrDefault(l => l.Day == d && l.Period.Value == p);
                    var cell = lesson == null ? "" : System.Security.SecurityElement.Escape(
                        (lesson.Subject?.ShortName ?? lesson.Subject?.Name ?? "") + " - " +
                        (lesson.Teacher?.ShortName ?? ""));
                    sb.AppendLine($"<td>{cell}</td>");
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table></body></html>");
            return sb.ToString();
        });
    }

    private IDocument CreateTimetableDocument(Timetable timetable, PrintOptions options)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(ResolveSize(options));
                page.MarginTop(options.MarginTop);
                page.MarginRight(options.MarginRight);
                page.MarginBottom(options.MarginBottom);
                page.MarginLeft(options.MarginLeft);
                page.DefaultTextStyle(x => x.FontFamily(options.FontFamily).FontSize(options.FontSize));

                page.Header().Element(c => DrawHeader(c, timetable, options));
                page.Content().Element(c => DrawTimetableGrid(c, timetable, options));
                page.Footer().Element(DrawFooter);
            });
        });
    }

    private IDocument CreateTeacherDocument(Teacher teacher, Timetable timetable, PrintOptions options)
    {
        var teacherLessons = timetable.Lessons.Where(l => l.TeacherId == teacher.Id).ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(ResolveSize(options));
                page.MarginTop(options.MarginTop);
                page.MarginRight(options.MarginRight);
                page.MarginBottom(options.MarginBottom);
                page.MarginLeft(options.MarginLeft);
                page.DefaultTextStyle(x => x.FontFamily(options.FontFamily).FontSize(options.FontSize));

                page.Header().Element(c => DrawTeacherHeader(c, teacher));
                page.Content().Element(c => DrawTeacherSchedule(c, teacherLessons, options));
                page.Footer().Element(DrawFooter);
            });
        });
    }

    private IDocument CreateClassDocument(Class @class, Timetable timetable, PrintOptions options)
    {
        var classLessons = timetable.Lessons.Where(l => l.ClassId == @class.Id).ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(ResolveSize(options));
                page.MarginTop(options.MarginTop);
                page.MarginRight(options.MarginRight);
                page.MarginBottom(options.MarginBottom);
                page.MarginLeft(options.MarginLeft);
                page.DefaultTextStyle(x => x.FontFamily(options.FontFamily).FontSize(options.FontSize));

                page.Header().Element(c => DrawClassHeader(c, @class));
                page.Content().Element(c => DrawClassSchedule(c, classLessons, options));
                page.Footer().Element(DrawFooter);
            });
        });
    }

    private void DrawHeader(IContainer container, Timetable timetable, PrintOptions options)
    {
        container.Column(col =>
        {
            if (!string.IsNullOrEmpty(options.SchoolName))
                col.Item().Text(options.SchoolName).FontSize(15).Bold().AlignCenter();
            col.Item().Text($"جدول الحصص الأسبوعي - {timetable.Name}").FontSize(16).Bold().FontColor(Colors.Blue.Darken2).AlignCenter();
            col.Item().Text($"العام الدراسي {timetable.AcademicYearStart}/{timetable.AcademicYearEnd} - الفصل {timetable.TermNumber}")
                .FontSize(10).AlignCenter().FontColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(6).AlignCenter().LineHorizontal(1).LineColor(Colors.Blue.Medium);
        });
    }

    private void DrawTeacherHeader(IContainer container, Teacher teacher)
    {
        container.Column(col =>
        {
            col.Item().Text($"جدول المعلم: {teacher.FullName}").FontSize(16).Bold().AlignCenter();
            col.Item().Text($"التخصص: {teacher.Specialization ?? "غير محدد"}").FontSize(11).AlignCenter().FontColor(Colors.Grey.Medium);
            col.Item().PaddingTop(4).AlignCenter().LineHorizontal(1).LineColor(Colors.Blue.Medium);
        });
    }

    private void DrawClassHeader(IContainer container, Class @class)
    {
        container.Column(col =>
        {
            col.Item().Text($"جدول الفصل: {@class.FullName}").FontSize(16).Bold().AlignCenter();
            col.Item().Text($"عدد الطلاب: {@class.StudentCount}").FontSize(11).AlignCenter().FontColor(Colors.Grey.Medium);
            col.Item().PaddingTop(4).AlignCenter().LineHorizontal(1).LineColor(Colors.Blue.Medium);
        });
    }

    private static string DayName(DomainDayOfWeek day) => DayArabic[(int)day];

    private static string SafeColor(string? hex, string fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var h = hex.Trim();
        if (!h.StartsWith('#')) h = "#" + h;
        if (h.Length != 7) return fallback;
        return h;
    }

    private void DrawTimetableGrid(IContainer container, Timetable timetable, PrintOptions options)
    {
        var days = Enum.GetValues<DomainDayOfWeek>().Reverse().ToList(); // ترتيب عربي: الجمعة يساراً
        var periods = Enumerable.Range(1, 12).ToList();

        container.PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in days)
                    columns.RelativeColumn();
                columns.ConstantColumn(90); // عمود الفترة أخيراً = أقصى اليمين
            });

            table.Header(header =>
            {
                foreach (var day in days)
                {
                    header.Cell().Background(Colors.Blue.Darken2).AlignCenter().Padding(5)
                        .Text(DayName(day)).FontColor(Colors.White).Bold();
                }
                header.Cell().Background(Colors.Blue.Darken2).AlignCenter().Padding(5)
                    .Text("الفترة").FontColor(Colors.White).Bold();
            });

            foreach (var period in periods)
            {
                var pTime = timetable.GetPeriodTimeForDisplay(period);
                var pLabel = pTime == null
                    ? PeriodNames.GetFullName(period)
                    : $"{PeriodNames.GetFullName(period)}\n{PeriodTime.FormatMinutes(pTime.StartMinutes)}-{PeriodTime.FormatMinutes(pTime.EndMinutes)}";

                foreach (var day in days)
                {
                    // الجدول الشامل: كل الحصص في الفترة/اليوم (عدة فصول) — تُكدَّس الخلايا بدل إهمال الزيادة
                    var cellLessons = timetable.Lessons.Where(l => l.Day == day && l.Period.Value == period).ToList();
                    var cell = table.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(3);

                    if (cellLessons.Count > 0)
                    {
                        cell.AlignCenter().Column(col =>
                        {
                            foreach (var lesson in cellLessons)
                            {
                                var color = SafeColor(lesson.Subject?.ColorHex, "#2563EB");
                                col.Item().Background(color).AlignCenter().Padding(2).Column(sub =>
                                {
                                    sub.Item().Text(lesson.Subject?.ShortName ?? lesson.Subject?.Name ?? "").Bold().FontSize(9).FontColor(Colors.White);
                                    var classRoom = lesson.Class?.ShortName ?? lesson.Class?.Name ?? "";
                                    if (!string.IsNullOrEmpty(lesson.Room?.ShortName))
                                        classRoom = string.IsNullOrEmpty(classRoom) ? lesson.Room.ShortName! : $"{classRoom} - {lesson.Room.ShortName}";
                                    if (!string.IsNullOrEmpty(classRoom))
                                        sub.Item().Text(classRoom).FontSize(7).FontColor(Colors.White);
                                });
                            }
                        });
                    }
                    else
                    {
                        cell.Background(period % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White);
                    }
                }

                table.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Medium)
                    .AlignCenter().Padding(3).Text(pLabel).Bold().FontSize(8);
            }
        });
    }

    private void DrawTeacherSchedule(IContainer container, List<Lesson> lessons, PrintOptions options)
    {
        var days = Enum.GetValues<DomainDayOfWeek>().Reverse().ToList();
        var periods = Enumerable.Range(1, 12).ToList();

        container.PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in days)
                    columns.RelativeColumn();
                columns.ConstantColumn(70);
            });

            table.Header(header =>
            {
                foreach (var day in days)
                    header.Cell().Background(Colors.Blue.Darken2).AlignCenter().Padding(5)
                        .Text(DayName(day)).FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Darken2).AlignCenter().Padding(5)
                    .Text("الفترة").FontColor(Colors.White).Bold();
            });

            foreach (var period in periods)
            {
                foreach (var day in days)
                {
                    var lesson = lessons.FirstOrDefault(l => l.Day == day && l.Period.Value == period);
                    var cell = table.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(3);
                    if (lesson != null)
                    {
                        var color = SafeColor(lesson.Subject?.ColorHex, "#2563EB");
                        cell.Background(color).AlignCenter().Column(col =>
                        {
                            col.Item().Text(lesson.Class?.ShortName ?? lesson.Class?.Name ?? "").Bold().FontSize(9).FontColor(Colors.White);
                            col.Item().Text(lesson.Subject?.ShortName ?? "").FontSize(7).FontColor(Colors.White);
                        });
                    }
                    else
                    {
                        cell.Background(period % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White);
                    }
                }
                table.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Medium)
                    .AlignCenter().Padding(3)
                    .Text(PeriodNames.GetFullName(period)).Bold().FontSize(8);
            }
        });
    }

    private void DrawClassSchedule(IContainer container, List<Lesson> lessons, PrintOptions options)
    {
        var days = Enum.GetValues<DomainDayOfWeek>().Reverse().ToList();
        var periods = Enumerable.Range(1, 12).ToList();

        container.PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in days)
                    columns.RelativeColumn();
                columns.ConstantColumn(70);
            });

            table.Header(header =>
            {
                foreach (var day in days)
                    header.Cell().Background(Colors.Blue.Darken2).AlignCenter().Padding(5)
                        .Text(DayName(day)).FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Darken2).AlignCenter().Padding(5)
                    .Text("الفترة").FontColor(Colors.White).Bold();
            });

            foreach (var period in periods)
            {
                foreach (var day in days)
                {
                    var lesson = lessons.FirstOrDefault(l => l.Day == day && l.Period.Value == period);
                    var cell = table.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(3);
                    if (lesson != null)
                    {
                        var color = SafeColor(lesson.Subject?.ColorHex, "#2563EB");
                        cell.Background(color).AlignCenter().Column(col =>
                        {
                            col.Item().Text(lesson.Subject?.ShortName ?? lesson.Subject?.Name ?? "").Bold().FontSize(9).FontColor(Colors.White);
                            col.Item().Text(lesson.Teacher?.ShortName ?? "").FontSize(7).FontColor(Colors.White);
                        });
                    }
                    else
                    {
                        cell.Background(period % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White);
                    }
                }
                table.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Medium)
                    .AlignCenter().Padding(3)
                    .Text(PeriodNames.GetFullName(period)).Bold().FontSize(8);
            }
        });
    }

    private void DrawFooter(IContainer container)
    {
        container.PaddingTop(4).Row(row =>
        {
            row.RelativeItem().AlignRight().Text(txt =>
            {
                txt.Span($"جميع الحقوق محفوظة © {DateTime.Now.Year} - عوض لافي الزبيدي").FontSize(7).FontColor(Colors.Grey.Medium);
            });

            row.ConstantItem(160).AlignCenter().Text(txt =>
            {
                txt.Span("صفحة ").FontSize(7).FontColor(Colors.Grey.Medium);
                txt.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Medium);
                txt.Span(" من ").FontSize(7).FontColor(Colors.Grey.Medium);
                txt.TotalPages().FontSize(7).FontColor(Colors.Grey.Medium);
            });

            row.RelativeItem().AlignLeft().Text(txt =>
            {
                txt.Span("نظام عوض للجداول المدرسية v1.0.0").FontSize(7).FontColor(Colors.Grey.Medium);
            });
        });
    }
}

using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Printing.Services;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>اختبارات خدمة الطباعة (QuestPDF + HTML)</summary>
public class PrintServiceTests
{
    private static Timetable BuildTimetable()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "جدول الطباعة", 2026, 2027, 1);

        var subject = new Subject(school, "رياضيات");
        var teacher = new Teacher(school, "أحمد علي", "رياضيات");
        var @class = new Class(school, "أول متوسط 1", 1, 'أ');

        var lesson = new Lesson(
            (TimetableId)timetable.Id,
            (SubjectId)subject.Id,
            (TeacherId)teacher.Id,
            (ClassId)@class.Id,
            DomainDayOfWeek.Saturday,
            PeriodNumber.Create(1));
        lesson.AttachReferences(subject, teacher, @class);
        timetable.AddLesson(lesson);

        var lesson2 = new Lesson(
            (TimetableId)timetable.Id,
            (SubjectId)subject.Id,
            (TeacherId)teacher.Id,
            (ClassId)@class.Id,
            DomainDayOfWeek.Sunday,
            PeriodNumber.Create(2));
        lesson2.AttachReferences(subject, teacher, @class);
        timetable.AddLesson(lesson2);

        return timetable;
    }

    [Fact]
    public async Task GeneratePdfAsync_ReturnsRealPdfBytes()
    {
        var printer = new PrintService();
        var bytes = await printer.GeneratePdfAsync(BuildTimetable(), new PrintOptions { SchoolName = "مدرسة الاختبار" });

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1000, $"حجم PDF صغير جداً: {bytes.Length}");
        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
        Assert.Equal("%PDF", header);
    }

    [Fact]
    public async Task GenerateHtmlAsync_ContainsTitleAndLessonNames()
    {
        var printer = new PrintService();
        var html = await printer.GenerateHtmlAsync(BuildTimetable(), new PrintOptions { SchoolName = "مدرسة الاختبار" });

        Assert.Contains("جدول الطباعة", html);
        Assert.Contains("رياضيات", html);
        Assert.Contains("dir=\"rtl\"", html);
    }

    [Fact]
    public async Task GenerateClassSchedulePdfAsync_ReturnsRealPdfBytes()
    {
        var timetable = BuildTimetable();
        var classEnt = timetable.Lessons.First().Class!;
        var printer = new PrintService();

        var bytes = await printer.GenerateClassSchedulePdfAsync(classEnt, timetable, new PrintOptions());

        Assert.True(bytes.Length > 500);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}

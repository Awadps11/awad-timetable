using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Printing.Services;
using System;
using System.IO;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>اختبارات تقرير الويب HTML (ب-style aSc: نظرة عامة + فصول + معلمون)</summary>
public class WebReportServiceTests
{
    private static (Timetable Timetable, List<Subject> Subjects, List<Teacher> Teachers, List<Class> Classes) BuildSchool()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "جدول اختباري", 2026, 2027, 1);

        var subjects = new List<Subject>
        {
            new(school, "رياضيات"),
            new(school, "علوم"),
            new(school, "عربي")
        };
        subjects[0].SetColor("#AA0000");
        var teachers = new List<Teacher>
        {
            new(school, "أحمد علي", "رياضيات"),
            new(school, "سارة محمود", "علوم"),
            new(school, "خالد سعيد", "لغة عربية")
        };
        var classes = new List<Class>
        {
            new(school, "الأول أ", 1, 'أ'),
            new(school, "الأول ب", 1, 'ب')
        };

        // حصة واحدة على الأقل لكل معلم/فصل/مادة في يومين مختلفين
        var slots = new (int Class, int Subject, DomainDayOfWeek Day, int Period)[]
        {
            (0, 0, DomainDayOfWeek.Saturday, 1),
            (0, 1, DomainDayOfWeek.Sunday, 2),
            (1, 0, DomainDayOfWeek.Saturday, 1), // تضارب مكاني مقصود: نفس الفترة لفصلين
            (1, 2, DomainDayOfWeek.Monday, 3)
        };

        foreach (var (c, s, day, period) in slots)
        {
            var lesson = new Lesson(
                (TimetableId)timetable.Id,
                (SubjectId)subjects[s].Id,
                (TeacherId)teachers[s].Id,
                (ClassId)classes[c].Id,
                day,
                PeriodNumber.Create((byte)period));
            lesson.AttachReferences(subjects[s], teachers[s], classes[c]);
            timetable.AddLesson(lesson);
        }

        return (timetable, subjects, teachers, classes);
    }

    private static (string Subject, string Teacher, string Class, string Room) Resolver(Lesson l) =>
        (l.Subject?.Name ?? "", l.Teacher?.FullName ?? "", l.Class?.FullName ?? "", "");

    [Fact]
    public void GenerateHtml_ContainsHeaderNavAndAllSections()
    {
        var (timetable, _, _, _) = BuildSchool();
        var html = new WebReportService().GenerateHtml(
            timetable, new WebReportOptions { SchoolName = "مدرسة الاختبار" }, Resolver);

        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("مدرسة الاختبار", html);
        Assert.Contains("جدول اختباري", html);
        Assert.Contains("id=\"overview\"", html);
        Assert.Contains("id=\"classes\"", html);
        Assert.Contains("id=\"teachers\"", html);
        Assert.Contains("نظرة عامة", html);
    }

    [Fact]
    public void GenerateHtml_ShowsSubjectsTeachersAndClasses()
    {
        var (timetable, _, _, _) = BuildSchool();
        var html = new WebReportService().GenerateHtml(
            timetable, new WebReportOptions { SchoolName = "مدرسة الاختبار" }, Resolver);

        Assert.Contains("رياضيات", html);
        Assert.Contains("سارة محمود", html);
        Assert.Contains("الأول أ", html);
        Assert.Contains("الأول ب", html);
        Assert.Contains("#AA0000", html); // لون المادة المضبوط من الكيان
    }

    [Fact]
    public void GenerateHtml_SamePeriodDifferentClasses_BothAppearInOverview()
    {
        var (timetable, _, _, _) = BuildSchool();
        var html = new WebReportService().GenerateHtml(
            timetable, new WebReportOptions(), Resolver);

        // الفصلان لهما حصة في السبت/الperiod 1 — يجب ألا يُهمَّل أحدهما كما في القديم (FirstOrDefault)
        var overview = html.Split("id=\"overview\"")[1].Split("id=\"classes\"")[0];
        Assert.Contains("الأول أ", overview);
        Assert.Contains("الأول ب", overview);
    }

    [Fact]
    public void GenerateHtml_AlwaysHasSummarySection()
    {
        var (timetable, _, _, _) = BuildSchool();
        var html = new WebReportService().GenerateHtml(timetable, new WebReportOptions(), Resolver);

        Assert.Contains("id=\"summary\"", html);
        Assert.Contains("تقرير الملخص", html);
        Assert.Contains("حصص المعلمين", html);
        Assert.Contains("حصص المواد", html);
    }

    [Fact]
    public void GenerateHtml_RendersRoomSection_WhenRoomsPresent()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "جدول قاعات", 2026, 2027, 1);
        var subject = new Subject(school, "رياضيات");
        var teacher = new Teacher(school, "أحمد", "علي");
        var cls = new Class(school, "الأول أ", 1, 'أ');
        var room = new Room(school, "قاعة 101", capacity: 30);

        var lesson = new Lesson((TimetableId)timetable.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id,
            (ClassId)cls.Id, DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
        lesson.AttachReferences(subject, teacher, cls, room);
        lesson.AssignRoom((RoomId)room.Id);
        timetable.AddLesson(lesson);

        (string Subject, string Teacher, string Class, string Room) Resolver2(Lesson l) =>
            (l.Subject?.Name ?? "", l.Teacher?.FullName ?? "", l.Class?.FullName ?? "", l.Room?.Name ?? "");

        var html = new WebReportService().GenerateHtml(timetable, new WebReportOptions(), Resolver2);

        Assert.Contains("id=\"rooms\"", html);
        Assert.Contains("جداول القاعات", html);
        Assert.Contains("قاعة 101", html);
    }

    [Fact]
    public void GenerateHtml_NoRooms_OmitsRoomSection()
    {
        var (timetable, _, _, _) = BuildSchool();
        var html = new WebReportService().GenerateHtml(timetable, new WebReportOptions(), Resolver);

        Assert.DoesNotContain("id=\"rooms\"", html);
    }

    [Fact]
    public void GenerateHtml_EmptyTimetable_StillProducesShell()
    {
        var timetable = new Timetable(SchoolId.New(), "فارغ", 2026, 2027, 1);
        var html = new WebReportService().GenerateHtml(
            timetable, new WebReportOptions { SchoolName = "مدرسة" }, Resolver);

        Assert.Contains("id=\"overview\"", html);
        Assert.Contains("مدرسة", html);
        Assert.Contains("<table class=\"tt\">", html);
    }

    [Fact]
    public void GenerateHtml_MergeConsecutive_EmitsColspan()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "دمج", 2026, 2027, 1);
        var subject = new Subject(school, "رياضيات");
        var teacher = new Teacher(school, "أحمد", "علي");
        var cls = new Class(school, "الأول أ", 1, 'أ');

        foreach (var period in new byte[] { 1, 2 })
        {
            var lesson = new Lesson((TimetableId)timetable.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id,
                (ClassId)cls.Id, DomainDayOfWeek.Saturday, PeriodNumber.Create(period));
            lesson.AttachReferences(subject, teacher, cls);
            timetable.AddLesson(lesson);
        }

        var template = AwadTimetable.Printing.Templates.PrintTemplatePresets.AscClassic();
        template.MergeConsecutive = true;

        var html = new WebReportService().GenerateHtml(timetable, new WebReportOptions(), Resolver, template);

        Assert.Contains("colspan=\"2\"", html);
    }

    [Fact]
    public void GenerateHtml_LogoPath_EmbedsBase64Image()
    {
        var (timetable, _, _, _) = BuildSchool();

        var logo = Path.Combine(Path.GetTempPath(), $"awad-logo-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(logo, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        try
        {
            var template = AwadTimetable.Printing.Templates.PrintTemplatePresets.AscClassic();
            template.LogoPath = logo;

            var html = new WebReportService().GenerateHtml(timetable, new WebReportOptions(), Resolver, template);

            Assert.Contains("data:image/png;base64,", html);
            Assert.Contains("class=\"logo\"", html);
        }
        finally
        {
            File.Delete(logo);
        }
    }
}

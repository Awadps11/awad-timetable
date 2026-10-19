using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Printing.Services;
using AwadTimetable.Printing.Templates;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class PrintTemplateTests
{
    private static Timetable BuildTimetable()
    {
        var school = SchoolId.New();
        var tt = new Timetable(school, "جدول", 2026, 2027, 1);
        var subject = new Subject(school, "رياضيات");
        var teacher = new Teacher(school, "أحمد", "علي");
        var cls = new Class(school, "الأول أ", 1, 'أ');
        var room = new Room(school, "101");

        var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id, (ClassId)cls.Id,
            DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
        lesson.AttachReferences(subject, teacher, cls, room);
        lesson.AssignRoom((RoomId)room.Id);
        tt.AddLesson(lesson);
        return tt;
    }

    private static (string Subject, string Teacher, string Class, string Room) Resolver(Lesson l)
        => (l.Subject?.Name ?? "", l.Teacher?.FullName ?? "", l.Class?.FullName ?? "", l.Room?.Name ?? "");

    [Fact]
    public void Presets_HasAtLeastEightBuiltInTemplates()
    {
        Assert.True(PrintTemplatePresets.All.Count >= 8);
        Assert.All(PrintTemplatePresets.All, t => Assert.True(t.IsBuiltIn));
        Assert.Contains(PrintTemplatePresets.All, t => t.Id == "builtin-room-card");
    }

    [Fact]
    public void Clone_CreatesCustomCopyWithNewId()
    {
        var source = PrintTemplatePresets.AscClassic();
        var copy = source.Clone();

        Assert.NotEqual(source.Id, copy.Id);
        Assert.False(copy.IsBuiltIn);
        Assert.Equal(source.HeaderColor, copy.HeaderColor);
    }

    [Fact]
    public void Store_RoundTripsCustomTemplates()
    {
        var path = Path.Combine(Path.GetTempPath(), "pt-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var custom = PrintTemplatePresets.ModernMinimal().Clone();
            custom.Name = "قالبي";

            PrintTemplateStore.Save(new[] { custom }, path);
            var loaded = PrintTemplateStore.Load(path);

            Assert.Single(loaded);
            Assert.Equal("قالبي", loaded[0].Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Render_UsesTemplateColorsAndToggles()
    {
        var tt = BuildTimetable();
        var template = PrintTemplatePresets.ModernMinimal();

        var html = new WebReportService().GenerateHtml(tt, new WebReportOptions(), Resolver, template);

        Assert.Contains(template.HeaderColor, html);
        Assert.DoesNotContain("id=\"summary\"", html);
        Assert.DoesNotContain("nav class=\"tabs\"", html);
    }

    [Fact]
    public void Render_ClassicTemplate_IncludesSummary()
    {
        var tt = BuildTimetable();
        var html = new WebReportService().GenerateHtml(tt, new WebReportOptions(), Resolver, PrintTemplatePresets.AscClassic());

        Assert.Contains("id=\"summary\"", html);
        Assert.Contains("nav class=\"tabs\"", html);
    }

    [Fact]
    public void Render_OutlinedCellStyle_UsesBorder()
    {
        var tt = BuildTimetable();
        var template = PrintTemplatePresets.CompactA3(); // Outlined

        var html = new WebReportService().GenerateHtml(tt, new WebReportOptions(), Resolver, template);

        Assert.Contains("border:1px solid", html);
    }

    [Fact]
    public void Render_HidesPeriodTimesWhenDisabled()
    {
        var tt = BuildTimetable();
        tt.PeriodTimes.Add(new PeriodTime { Period = 1, Day = -1, StartMinutes = 420, EndMinutes = 465 });

        var template = PrintTemplatePresets.AscClassic();
        template.ShowPeriodTimes = false;

        var html = new WebReportService().GenerateHtml(tt, new WebReportOptions(), Resolver, template);

        Assert.DoesNotContain("07:00", html);
    }
}

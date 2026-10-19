using System.Text;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class AscEngineTests
{
    private static readonly DomainDayOfWeek[] Days =
    {
        DomainDayOfWeek.Sunday, DomainDayOfWeek.Monday, DomainDayOfWeek.Tuesday,
    };

    private static AscEngine Make(string folder)
        => new(NullLogger<AscEngine>.Instance, new ConstraintValidator(), ascExePath: string.Empty, exportFolder: folder);

    [Fact]
    public async Task Generate_ExportsFile_AndReturnsGuidance_NotAFailure()
    {
        var folder = Path.Combine(Path.GetTempPath(), "awad-asc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var engine = Make(folder);
            var timetable = SampleTimetable.Create(classCount: 2, subjectCount: 2);

            var result = await engine.GenerateAsync(timetable, new GenerationOptions());

            // لا يُدّعى النجاح: aSc يولّد داخل واجهته فقط
            Assert.False(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));

            var file = Directory.GetFiles(folder, "*.xml").Single();
            var text = File.ReadAllText(file);
            Assert.Contains("ascttversion", text);

            // الترميز المكتوب هو windows-1256
            var head = Encoding.ASCII.GetString(File.ReadAllBytes(file).Take(60).ToArray());
            Assert.Contains("windows-1256", head);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Generate_FailsClearly_OnEmptyTimetable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "awad-asc-" + Guid.NewGuid().ToString("N"));
        var engine = Make(folder);
        var timetable = new Timetable(SchoolId.New(), "فارغ", 2026, 2027, 1);

        var result = await engine.GenerateAsync(timetable, new GenerationOptions());

        Assert.False(result.Success);
        Assert.Contains("حصص", result.ErrorMessage);
    }

    [Fact]
    public async Task Generate_DoesNotMoveLessons()
    {
        var folder = Path.Combine(Path.GetTempPath(), "awad-asc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var timetable = SampleTimetable.Create(classCount: 2, subjectCount: 2);
            var before = timetable.Lessons.Select(l => (l.Id, l.Day, l.Period.Value)).ToList();

            await Make(folder).GenerateAsync(timetable, new GenerationOptions());

            var after = timetable.Lessons.Select(l => (l.Id, l.Day, l.Period.Value)).ToList();
            Assert.Equal(before, after);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}

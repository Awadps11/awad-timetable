using AwadTimetable.Core.Services;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبارات محرك FET. تتطلب تثبيت fet-cl في المسار المحدد بمتغير البيئة AWAD_FET_CL.
/// إن لم يُحدَّد المتغير تُتخطّى (لا تفشل).
/// </summary>
public class FetEngineTests
{
    private static string? FetPath => Environment.GetEnvironmentVariable("AWAD_FET_CL");

    [Fact]
    public async Task FailsClearly_WhenFetExecutableIsMissing()
    {
        var engine = new FetEngine(
            NullLogger<FetEngine>.Instance,
            new ConstraintValidator(),
            @"C:\definitely\missing\fet-cl.exe");

        var timetable = SampleTimetable.Create(classCount: 2, subjectCount: 2);
        var result = await engine.GenerateAsync(timetable, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(5) });

        Assert.False(result.Success);
        Assert.Contains("fet-cl", result.ErrorMessage);
    }

    [Fact]
    public async Task GeneratesTimetable_WithFet_WhenAvailable()
    {
        var path = FetPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        var engine = new FetEngine(NullLogger<FetEngine>.Instance, new ConstraintValidator(), path);
        var timetable = SampleTimetable.Create(classCount: 3, subjectCount: 4);

        var result = await engine.GenerateAsync(timetable, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(15) });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(12, result.LessonsScheduled);
        Assert.Equal(0, result.ConflictsCount);
        // الحصص كانت كلها في الفترة 1 قبل التوليد؛ إن وزّعها FET فعلاً فيجب أن تتعدد الخانات
        var distinctSlots = timetable.Lessons.Select(l => (l.Day, l.Period.Value)).Distinct().Count();
        Assert.True(distinctSlots > 1, $"FET لم يوزّع الحصص: {distinctSlots} خانة فقط");
    }
}

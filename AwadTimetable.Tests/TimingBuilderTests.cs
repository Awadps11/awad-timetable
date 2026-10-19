using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Enums;

namespace AwadTimetable.Tests;

public class TimingBuilderTests
{
    private static List<int> Gaps(IReadOnlyList<(int Period, int Start, int End)> slots)
    {
        var gaps = new List<int>();
        for (var i = 0; i < slots.Count - 1; i++)
            gaps.Add(slots[i + 1].Start - slots[i].End);
        return gaps;
    }

    private static List<(int Period, int Start, int End)> Slots(SchoolType type, int lessons)
        => TimingBuilder.Build(type, new List<int> { 0 }, lessons)
            .OrderBy(p => p.Period)
            .Select(p => ((int)p.Period, p.StartMinutes, p.EndMinutes))
            .ToList();

    [Fact]
    public void Middle_HasTwoTenMinuteBreaks_AfterLessonsTwoAndFour()
    {
        var slots = Slots(SchoolType.Middle, 7);
        var gaps = Gaps(slots);

        // بعد الحصة 2 و 4 فسحة 10 دقائق، وبين الباقي لا فراغ
        Assert.Equal(10, gaps[1]); // بين 2 و 3
        Assert.Equal(10, gaps[3]); // بين 4 و 5
        Assert.Equal(0, gaps[0]);
        Assert.Equal(0, gaps[2]);
        Assert.Equal(0, gaps[4]);
        Assert.Equal(0, gaps[5]);
    }

    [Fact]
    public void Secondary_HasSingleTwentyMinuteBreak_AfterLessonThree()
    {
        var slots = Slots(SchoolType.Secondary, 7);
        var gaps = Gaps(slots);

        Assert.Equal(20, gaps[2]); // بين 3 و 4
        Assert.Equal(0, gaps[0]);
        Assert.Equal(0, gaps[1]);
        Assert.Equal(0, gaps[3]);
    }

    [Fact]
    public void Primary_HasSingleTenMinuteBreak_AfterLessonThree()
    {
        var slots = Slots(SchoolType.Primary, 7);
        var gaps = Gaps(slots);

        Assert.Equal(10, gaps[2]);
        Assert.Equal(0, gaps[0]);
    }

    [Fact]
    public void LessonDuration_IsFortyFive()
    {
        var slots = Slots(SchoolType.Middle, 3);

        Assert.All(slots, s => Assert.Equal(45, s.End - s.Start));
    }

    [Fact]
    public void BuildsSlotForEveryWorkDay()
    {
        var list = TimingBuilder.Build(SchoolType.Secondary, new List<int> { 0, 1, 2 }, lessonsPerDay: 4);

        Assert.Equal(12, list.Count);
        Assert.Equal(3, list.Select(p => p.Day).Distinct().Count());
    }

    [Fact]
    public void BuildForStages_ProducesIndependentTimingPerStage()
    {
        var stages = new List<(SchoolType, string, int, int)>
        {
            (SchoolType.Middle, "متوسط", 7, 45),
            (SchoolType.Secondary, "ثانوي", 6, 50),
        };

        var list = TimingBuilder.BuildForStages(stages, new List<int> { 0 });

        var middle = list.Where(p => p.Stage == "متوسط").OrderBy(p => p.Period).ToList();
        var secondary = list.Where(p => p.Stage == "ثانوي").OrderBy(p => p.Period).ToList();

        Assert.Equal(7, middle.Count);
        Assert.Equal(6, secondary.Count);
        Assert.All(middle, p => Assert.Equal(45, p.EndMinutes - p.StartMinutes));
        Assert.All(secondary, p => Assert.Equal(50, p.EndMinutes - p.StartMinutes));

        // الفسح تختلف: المتوسط بعد 2 و4، الثانوي بعد 3
        var mg = Gaps(middle.Select(p => ((int)p.Period, p.StartMinutes, p.EndMinutes)).ToList());
        var sg = Gaps(secondary.Select(p => ((int)p.Period, p.StartMinutes, p.EndMinutes)).ToList());
        Assert.Equal(10, mg[1]);
        Assert.Equal(10, mg[3]);
        Assert.Equal(20, sg[2]);
    }

    [Fact]
    public void BuildForStages_RespectsPerDayCounts()
    {
        var stages = new List<(SchoolType, string, int, int)> { (SchoolType.Middle, "متوسط", 7, 45) };
        var perDay = new Dictionary<string, IReadOnlyDictionary<int, int>>
        {
            ["متوسط"] = new Dictionary<int, int> { [0] = 5, [1] = 7 },
        };

        var list = TimingBuilder.BuildForStages(stages, new List<int> { 0, 1 }, perDay);

        Assert.Equal(5, list.Count(p => p.Day == 0));
        Assert.Equal(7, list.Count(p => p.Day == 1));
    }

    [Fact]
    public void StageConfig_GetLessonsPerDay_FallsBackToGeneral()
    {
        var cfg = new AwadTimetable.Core.Entities.StageScheduleConfig
        {
            LessonsPerDay = 7,
            LessonsPerDayByDay = new Dictionary<int, int> { [0] = 5 },
        };

        Assert.Equal(5, cfg.GetLessonsPerDay(0));
        Assert.Equal(7, cfg.GetLessonsPerDay(1));
    }

    [Fact]
    public void BuildForStages_TagsDayAndStage()
    {
        var stages = new List<(SchoolType, string, int, int)> { (SchoolType.Secondary, "ثانوي", 3, 45) };
        var list = TimingBuilder.BuildForStages(stages, new List<int> { 0, 1 });

        Assert.Equal(6, list.Count);
        Assert.All(list, p => Assert.Equal("ثانوي", p.Stage));
        Assert.Equal(2, list.Select(p => p.Day).Distinct().Count());
    }

    [Fact]
    public void BreakOnlyAddedWhenLessonReached()
    {
        // الحصص أقل من موضع الفسحة: لا فراغ
        var slots = Slots(SchoolType.Secondary, 2);
        var gaps = Gaps(slots);

        Assert.All(gaps, g => Assert.Equal(0, g));
    }
}

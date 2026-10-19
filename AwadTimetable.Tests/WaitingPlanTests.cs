using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;

namespace AwadTimetable.Tests;

public class WaitingPlanTests
{
    private static Timetable MakeTimetable()
    {
        var tt = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = new List<int> { 0, 1, 2, 3, 4, 5 }, LessonsPerDay = 7 });
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "ثانوي", WorkDays = new List<int> { 0, 1, 2, 3, 4, 5 }, LessonsPerDay = 7 });
        return tt;
    }

    [Fact]
    public void Capacity_IsDaysTimesPeriods()
    {
        var tt = MakeTimetable();

        Assert.Equal(42, WaitingPlan.Capacity(tt, "متوسط"));
    }

    [Fact]
    public void Validate_AcceptsReasonablePlan()
    {
        var tt = MakeTimetable();
        var entries = new List<WaitingPlanEntry>
        {
            new("متوسط", 1, Guid.NewGuid(), 4),
            new("متوسط", 1, Guid.NewGuid(), 4),
            new("ثانوي", 2, Guid.NewGuid(), 5),
        };

        Assert.Empty(WaitingPlan.Validate(tt, entries));
    }

    [Fact]
    public void Validate_RejectsOverCapacity()
    {
        var tt = MakeTimetable();
        var entries = new List<WaitingPlanEntry>
        {
            new("متوسط", 1, Guid.NewGuid(), 30),
            new("متوسط", 1, Guid.NewGuid(), 20),
        };

        var errors = WaitingPlan.Validate(tt, entries);

        Assert.Contains(errors, e => e.Contains("يتجاوز السعة"));
    }

    [Fact]
    public void Validate_RejectsBadTierAndPeriods()
    {
        var tt = MakeTimetable();
        var entries = new List<WaitingPlanEntry>
        {
            new("متوسط", 4, Guid.NewGuid(), 2),
            new("متوسط", 1, Guid.NewGuid(), 0),
        };

        var errors = WaitingPlan.Validate(tt, entries);

        Assert.Contains(errors, e => e.Contains("الفئة 4 غير صحيحة"));
        Assert.Contains(errors, e => e.Contains("يجب أن يكون 1"));
    }

    [Fact]
    public void Validate_RejectsUnknownStage()
    {
        var tt = MakeTimetable();
        var entries = new List<WaitingPlanEntry> { new("ابتدائي", 1, Guid.NewGuid(), 2) };

        var errors = WaitingPlan.Validate(tt, entries);

        Assert.Contains(errors, e => e.Contains("غير معرّفة"));
    }

    [Fact]
    public void Names_FollowAscConvention()
    {
        Assert.Equal("انتظار متوسط 1", WaitingPlan.ClassName("متوسط", 1));
        Assert.Equal("انتظار 2", WaitingPlan.SubjectName(2));
    }

    [Fact]
    public void Validate_SameTeacherInMultipleTiers_IsAllowed()
    {
        var tt = MakeTimetable();
        var teacher = Guid.NewGuid();
        var entries = new List<WaitingPlanEntry>
        {
            new("متوسط", 1, teacher, 3),
            new("متوسط", 2, teacher, 3),
            new("ثانوي", 3, teacher, 3),
        };

        Assert.Empty(WaitingPlan.Validate(tt, entries));
    }
}

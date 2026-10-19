using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.ValueObjects;

namespace AwadTimetable.Tests;

public class GeneratorEngineTests
{
    [Fact]
    public void NewTimetable_DefaultsToCpSat()
    {
        var timetable = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);

        Assert.Equal(GeneratorEngine.CpSat, timetable.Engine);
    }

    [Fact]
    public void NewTimetable_DefaultsToMiddleSchoolType()
    {
        var timetable = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);

        Assert.Equal(SchoolType.Middle, timetable.SchoolType);
    }

    [Fact]
    public void SetSchoolType_ChangesType()
    {
        var timetable = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);

        timetable.SetSchoolType(SchoolType.Secondary);

        Assert.Equal(SchoolType.Secondary, timetable.SchoolType);
    }

    [Fact]
    public void SetEngine_ChangesEngineAndTouchesUpdatedAt()
    {
        var timetable = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);

        timetable.SetEngine(GeneratorEngine.Fet);

        Assert.Equal(GeneratorEngine.Fet, timetable.Engine);
        Assert.NotNull(timetable.UpdatedAt);
    }
}

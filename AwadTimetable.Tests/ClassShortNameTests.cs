using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;

namespace AwadTimetable.Tests;

public class ClassShortNameTests
{
    [Fact]
    public void DisplayShortName_FallsBackToName_WhenNoShort()
    {
        var cls = new Class(SchoolId.New(), "أول متوسط", 7, 'أ');
        cls.SetShortName(null);

        Assert.Equal("أول متوسط", cls.DisplayShortName);
    }

    [Fact]
    public void SetShortName_IsUsedForDisplay()
    {
        var cls = new Class(SchoolId.New(), "أول متوسط", 7, 'أ');

        cls.SetShortName("م1");

        Assert.Equal("م1", cls.DisplayShortName);
        Assert.Equal("م1", cls.ShortName);
    }

    [Fact]
    public void SetShortName_Blank_ClearsToNull_AndFallsBackToName()
    {
        var cls = new Class(SchoolId.New(), "أول متوسط", 7, 'أ');
        cls.SetShortName("م1");

        cls.SetShortName("   ");

        Assert.Null(cls.ShortName);
        Assert.Equal("أول متوسط", cls.DisplayShortName);
    }

    [Fact]
    public void FullName_RemainsDetailed()
    {
        var cls = new Class(SchoolId.New(), "أول متوسط", 7, 'أ');
        cls.SetShortName("م1");

        Assert.Equal("أول متوسط - 7أ", cls.FullName);
    }
}

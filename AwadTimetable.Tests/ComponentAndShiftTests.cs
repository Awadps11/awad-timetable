using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class ComponentTransferTests
{
    [Fact]
    public void Csv_RoundTrips()
    {
        var rows = new List<ComponentRow>
        {
            new("رياضيات", "ريا", 5),
            new("لغة عربية", "ع", 4),
            new("بلا اختصار", null, null),
        };

        var csv = ComponentTransfer.ToCsv(rows);
        var back = ComponentTransfer.FromCsv(csv);

        Assert.Equal(3, back.Count);
        Assert.Equal("رياضيات", back[0].Name);
        Assert.Equal("ريا", back[0].ShortName);
        Assert.Equal(5, back[0].Number);
        Assert.Null(back[2].ShortName);
    }

    [Fact]
    public void Csv_EscapesCommas()
    {
        var csv = ComponentTransfer.ToCsv(new[] { new ComponentRow("مادة, خاصة", "خ", 2) });
        var back = ComponentTransfer.FromCsv(csv);

        Assert.Single(back);
        Assert.Equal("مادة, خاصة", back[0].Name);
    }

    [Fact]
    public void Csv_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ComponentTransfer.FromCsv(""));
    }
}

public class DayShifterTests
{
    private static Timetable MakeTimetable(params (DomainDayOfWeek Day, int Period)[] slots)
    {
        var tt = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        foreach (var (day, period) in slots)
        {
            var lesson = new Lesson((TimetableId)tt.Id, SubjectId.New(), TeacherId.New(), ClassId.New(),
                day, PeriodNumber.Create((byte)period));
            tt.AddLesson(lesson);
        }
        return tt;
    }

    [Fact]
    public void Shift_MovesRemovedDay_ToNearestExisting()
    {
        // القديم: سبت..خميس (0..5) والجديد: أحد..خميس (1..5) — حصص السبت تنتقل للأحد
        var tt = MakeTimetable(
            (DomainDayOfWeek.Saturday, 1),
            (DomainDayOfWeek.Sunday, 2));

        var moved = DayShifter.Shift(tt, new List<int> { 0, 1, 2, 3, 4, 5 }, new List<int> { 1, 2, 3, 4, 5 });

        Assert.Equal(2, moved);
        Assert.DoesNotContain(tt.Lessons, l => l.Day == DomainDayOfWeek.Saturday);
    }

    [Fact]
    public void Shift_AddsDayAtStart_PullsLessonsBackward()
    {
        // إضافة السبت في بداية الأسبوع: أول يوم (الأحد) يصبح السبت (سحب للخلف)
        var tt = MakeTimetable((DomainDayOfWeek.Sunday, 1));

        DayShifter.Shift(tt, new List<int> { 1, 2, 3, 4, 5 }, new List<int> { 0, 1, 2, 3, 4, 5 });

        Assert.Equal(DomainDayOfWeek.Saturday, tt.Lessons.Single().Day);
    }

    [Fact]
    public void Shift_RemovingFirstDay_PullsLessonsForward()
    {
        // حذف السبت: الأحد يصبح أول يوم (سحب للأمام)
        var tt = MakeTimetable((DomainDayOfWeek.Sunday, 1), (DomainDayOfWeek.Saturday, 2));

        DayShifter.Shift(tt, new List<int> { 0, 1, 2 }, new List<int> { 1, 2 });

        var sundayLesson = tt.Lessons.First(l => l.Period.Value == 1);
        Assert.Equal(DomainDayOfWeek.Monday, sundayLesson.Day);
    }

    [Fact]
    public void Shift_NoChange_WhenSameDays()
    {
        var tt = MakeTimetable((DomainDayOfWeek.Sunday, 1));

        var moved = DayShifter.Shift(tt, new List<int> { 0, 1, 2 }, new List<int> { 0, 1, 2 });

        Assert.Equal(0, moved);
        Assert.Equal(DomainDayOfWeek.Sunday, tt.Lessons.Single().Day);
    }

    [Fact]
    public void Shift_ReturnsZero_WhenNoNewDays()
    {
        var tt = MakeTimetable((DomainDayOfWeek.Sunday, 1));

        Assert.Equal(0, DayShifter.Shift(tt, new List<int> { 0, 1 }, Array.Empty<int>()));
    }
}

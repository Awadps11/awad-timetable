using System.Globalization;
using System.Text;
using System.Xml.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبارات مُصدِّر aSc: الترميز، البنية، وتطابق الحصص عند القراءة بمنطق المستورِد.
/// </summary>
public class AscXmlFormatTests
{
    private static Timetable BuildTimetable()
    {
        var timetable = SampleTimetable.Create(classCount: 2, subjectCount: 3);
        var index = 0;
        foreach (var lesson in timetable.Lessons)
        {
            lesson.Reschedule(index % 2 == 0 ? DomainDayOfWeek.Sunday : DomainDayOfWeek.Tuesday,
                PeriodNumber.Create((byte)(index % 4 + 1)));
            index++;
        }
        return timetable;
    }

    [Fact]
    public void Export_WritesWindowsArabicEncodingDeclaration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"asc-{Guid.NewGuid():N}.xml");
        try
        {
            AscXmlFormat.Export(BuildTimetable(), path);

            var head = Encoding.ASCII.GetString(File.ReadAllBytes(path).Take(80).ToArray());
            Assert.Contains("windows-1256", head);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Export_ProducesOneCardPerLesson_LinkedToLessons()
    {
        var timetable = BuildTimetable();
        var doc = AscXmlFormat.Build(timetable);

        var lessonIds = doc.Root!.Element("lessons")!.Elements("lesson").Select(e => e.Attribute("id")!.Value).ToHashSet();
        var cards = doc.Root!.Element("cards")!.Elements("card").ToList();

        Assert.Equal(timetable.Lessons.Count, cards.Count);
        Assert.All(cards, c => Assert.Contains(c.Attribute("lessonid")!.Value, lessonIds));
    }

    [Fact]
    public void Export_DaysAndPeriodsRoundTrip()
    {
        var timetable = BuildTimetable();
        var doc = AscXmlFormat.Build(timetable);

        var maskToDay = new Dictionary<string, DomainDayOfWeek>
        {
            ["10000"] = DomainDayOfWeek.Sunday,
            ["01000"] = DomainDayOfWeek.Monday,
            ["00100"] = DomainDayOfWeek.Tuesday,
            ["00010"] = DomainDayOfWeek.Wednesday,
            ["00001"] = DomainDayOfWeek.Thursday,
        };

        var exported = doc.Root!.Element("cards")!.Elements("card")
            .Select(c => (Day: maskToDay[c.Attribute("days")!.Value], Period: int.Parse(c.Attribute("period")!.Value, CultureInfo.InvariantCulture) + 1))
            .OrderBy(x => x.Day).ThenBy(x => x.Period)
            .ToList();

        var expected = timetable.Lessons
            .Select(l => (Day: l.Day, Period: (int)l.Period.Value))
            .OrderBy(x => x.Day).ThenBy(x => x.Period)
            .ToList();

        Assert.Equal(expected, exported);
    }

    [Fact]
    public void Export_WeeklyCountMatchesLessonsPerSubjectAndClass()
    {
        var timetable = BuildTimetable();
        var doc = AscXmlFormat.Build(timetable);

        var declared = doc.Root!.Element("lessons")!.Elements("lesson")
            .Sum(e => (int)double.Parse(e.Attribute("periodsperweek")!.Value, CultureInfo.InvariantCulture));

        Assert.Equal(timetable.Lessons.Count, declared);
    }

    [Fact]
    public void Export_WritesBreaks_ForBreakPeriods()
    {
        var timetable = BuildTimetable();
        timetable.PeriodTimes.Add(new PeriodTime { Period = 3, IsBreak = true, Day = -1, StartMinutes = 10 * 60, EndMinutes = 10 * 60 + 30 });

        var doc = AscXmlFormat.Build(timetable);
        var breaks = doc.Root!.Element("breaks")!.Elements("break").ToList();

        Assert.Single(breaks);
        Assert.Equal("10:00", breaks[0].Attribute("starttime")!.Value);
    }

    [Fact]
    public void Export_WritesBuildings_AndLinksRooms()
    {
        var timetable = BuildTimetable();
        var school = timetable.SchoolId;
        var room = new Room(school, "قاعة 1", capacity: 30);
        room.UpdateLocation(1, "المبنى أ");
        var lesson = timetable.Lessons.First();
        lesson.AttachReferences(lesson.Subject, lesson.Teacher, lesson.Class, room);
        lesson.AssignRoom((RoomId)room.Id);

        var doc = AscXmlFormat.Build(timetable);
        var buildings = doc.Root!.Element("buildings")!.Elements("building").ToList();
        var rooms = doc.Root!.Element("classrooms")!.Elements("classroom").ToList();

        Assert.Single(buildings);
        Assert.Equal("المبنى أ", buildings[0].Attribute("name")!.Value);
        Assert.Single(rooms);
        Assert.Equal(buildings[0].Attribute("id")!.Value, rooms[0].Attribute("buildingid")!.Value);
    }

    [Fact]
    public void Export_DaysMaskHasExactlyOneDay()
    {
        Assert.Equal("00100", AscXmlFormat.MaskFor(DomainDayOfWeek.Tuesday));
        Assert.Equal("10000", AscXmlFormat.MaskFor(DomainDayOfWeek.Sunday));
        Assert.Equal("00000", AscXmlFormat.MaskFor(DomainDayOfWeek.Friday));
    }
}

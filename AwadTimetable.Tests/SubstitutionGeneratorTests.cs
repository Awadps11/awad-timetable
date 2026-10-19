using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using Xunit;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبارات مولّد البدلاء بنظام الفئات الثلاث (الأول ثم الثاني ثم الثالث)
/// </summary>
public class SubstitutionGeneratorTests
{
    private static (Timetable Timetable, List<Teacher> Teachers) BuildScenario()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "جدول البدائل", 2026, 2027, 1);

        var subject = new Subject(school, "رياضيات");

        // المعلم الغائب وثلاثة بدلاء بفئات مختلفة
        var absent = new Teacher(school, "غائب", "اختباري");
        var sub1 = new Teacher(school, "بديل", "أول");
        var sub2 = new Teacher(school, "بديل", "ثاني");
        var sub3 = new Teacher(school, "بديل", "ثالث");
        var teachers = new List<Teacher> { absent, sub1, sub2, sub3 };

        absent.UpdateSubstitutes(sub1.Id, sub2.Id, sub3.Id);

        var cls = new Class(school, "ك1", 1, 'أ');
        timetable.ClassGradeLevels[cls.Id] = 1;

        // حصة واحدة للمعلم الغائب: السبت، الفترة 1
        var lesson = new Lesson(
            (TimetableId)timetable.Id,
            (SubjectId)subject.Id,
            (TeacherId)absent.Id,
            (ClassId)cls.Id,
            DomainDayOfWeek.Saturday,
            PeriodNumber.Create(1));
        lesson.AttachReferences(subject, absent, cls);
        timetable.AddLesson(lesson);

        return (timetable, teachers);
    }

    [Fact]
    public void Generate_ShouldAssignFirstTierSubstitute_WhenFree()
    {
        var (timetable, teachers) = BuildScenario();
        var absent = teachers[0];
        var sub1 = teachers[1];

        // السبت = 2026/08/15
        var date = new DateOnly(2026, 8, 15);
        Assert.Equal(System.DayOfWeek.Saturday, date.DayOfWeek);

        var rows = SubstitutionGenerator.Generate(timetable, teachers, new[] { absent.Id }, date);

        var row = Assert.Single(rows);
        Assert.Equal(sub1.Id, row.SubstituteTeacherId);
        Assert.Equal(1, row.Tier);
        Assert.Equal("معيّن", row.Status);
    }

    [Fact]
    public void Generate_ShouldFallToSecondTier_WhenFirstIsBusyTeaching()
    {
        var (timetable, teachers) = BuildScenario();
        var absent = teachers[0];
        var sub1 = teachers[1];
        var sub2 = teachers[2];

        var subject = timetable.Lessons.First().Subject!;
        var cls = timetable.Lessons.First().Class!;

        // البديل الأول مشغول بحصة تدريس في نفس الفترة (السبت ف1)
        var busyLesson = new Lesson(
            (TimetableId)timetable.Id,
            (SubjectId)subject.Id,
            (TeacherId)sub1.Id,
            (ClassId)cls.Id,
            DomainDayOfWeek.Saturday,
            PeriodNumber.Create(1));
        busyLesson.AttachReferences(subject, sub1, cls);
        timetable.AddLesson(busyLesson);

        var date = new DateOnly(2026, 8, 15);
        var rows = SubstitutionGenerator.Generate(timetable, teachers, new[] { absent.Id }, date);

        var row = rows.Single(r => r.AbsentTeacherId == absent.Id);
        Assert.Equal(sub2.Id, row.SubstituteTeacherId);
        Assert.Equal(2, row.Tier);
    }

    [Fact]
    public void Generate_ShouldSkipAbsentCandidate()
    {
        var (timetable, teachers) = BuildScenario();
        var absent = teachers[0];
        var sub1 = teachers[1];
        var sub2 = teachers[2];

        // البديل الأول غائب أيضاً فنجاوز إلى الثاني
        var date = new DateOnly(2026, 8, 15);
        var rows = SubstitutionGenerator.Generate(timetable, teachers, new[] { absent.Id, sub1.Id }, date);

        var row = rows.Single(r => r.AbsentTeacherId == absent.Id);
        Assert.Equal(sub2.Id, row.SubstituteTeacherId);
        Assert.Equal(2, row.Tier);
    }

    [Fact]
    public void Generate_ShouldMarkWithoutSubstitute_WhenAllTiersUnavailable()
    {
        var (timetable, teachers) = BuildScenario();
        var absent = teachers[0];
        var sub1 = teachers[1];
        var sub2 = teachers[2];
        var sub3 = teachers[3];

        // كل البدلاء غائبون
        var date = new DateOnly(2026, 8, 15);
        var rows = SubstitutionGenerator.Generate(
            timetable, teachers, new[] { absent.Id, sub1.Id, sub2.Id, sub3.Id }, date);

        var row = rows.Single(r => r.AbsentTeacherId == absent.Id);
        Assert.Null(row.SubstituteTeacherId);
        Assert.Null(row.Tier);
        Assert.Equal("بدون بديل", row.Status);
    }

    [Fact]
    public void Generate_ShouldReturnEmpty_OnFriday()
    {
        var (timetable, teachers) = BuildScenario();
        var absent = teachers[0];

        // الجمعة = 2026/08/14
        var date = new DateOnly(2026, 8, 14);
        Assert.Equal(System.DayOfWeek.Friday, date.DayOfWeek);

        var rows = SubstitutionGenerator.Generate(timetable, teachers, new[] { absent.Id }, date);
        Assert.Empty(rows);
    }

    [Fact]
    public void GetTimetableDayIndex_ShouldMapSaturdayToZeroAndFridayToSix()
    {
        Assert.Equal(0, SubstitutionGenerator.GetTimetableDayIndex(new DateOnly(2026, 8, 15)));
        Assert.Equal(6, SubstitutionGenerator.GetTimetableDayIndex(new DateOnly(2026, 8, 14)));
    }
}
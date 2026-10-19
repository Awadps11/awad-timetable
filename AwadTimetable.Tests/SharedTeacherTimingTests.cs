using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// معلم مشترك بين المرحلتين (متوسط + ثانوي) بتوقيتين مختلفين:
/// التضارب يجب أن يُحسب بتداخل الزمن الفعلي، لا برقم الفترة.
/// </summary>
public class SharedTeacherTimingTests
{
    private static Timetable BigSchool(bool overlapping)
    {
        var tt = new Timetable(SchoolId.New(), "مجمع", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = new List<int> { 0 }, LessonsPerDay = 4 });
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "ثانوي", WorkDays = new List<int> { 0 }, LessonsPerDay = 4 });

        var midStart = 7 * 60;                 // 07:00
        var secStart = overlapping ? 7 * 60 + 15 : 8 * 60; // متداخل أو منفصل

        for (byte p = 1; p <= 3; p++)
        {
            tt.PeriodTimes.Add(new PeriodTime { Period = p, Day = 0, Stage = "متوسط", StartMinutes = midStart + (p - 1) * 45, EndMinutes = midStart + (p - 1) * 45 + 45 });
            tt.PeriodTimes.Add(new PeriodTime { Period = p, Day = 0, Stage = "ثانوي", StartMinutes = secStart + (p - 1) * 45, EndMinutes = secStart + (p - 1) * 45 + 45 });
        }

        return tt;
    }

    private static (Teacher Teacher, Class Mid, Class Sec) AddClasses(Timetable tt)
    {
        var school = tt.SchoolId;
        var teacher = new Teacher(school, "مشترك", "معلم");
        var mid = new Class(school, "أول متوسط 1", 7, 'أ');
        var sec = new Class(school, "أول ثانوي 1", 10, 'أ');
        tt.ClassGradeLevels[mid.Id] = 7;
        tt.ClassGradeLevels[sec.Id] = 10;
        return (teacher, mid, sec);
    }

    private static Lesson AddLesson(Timetable tt, Teacher t, Class c, int period)
    {
        var school = tt.SchoolId;
        var subject = new Subject(school, "مادة " + Guid.NewGuid().ToString("N")[..4]);
        var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)t.Id, (ClassId)c.Id,
            DomainDayOfWeek.Saturday, PeriodNumber.Create((byte)period));
        lesson.AttachReferences(subject, t, c);
        tt.AddLesson(lesson);
        return lesson;
    }

    [Fact]
    public void Validator_NoFalseConflict_WhenTimingsDoNotOverlap()
    {
        var tt = BigSchool(overlapping: false);
        var (teacher, mid, sec) = AddClasses(tt);
        AddLesson(tt, teacher, mid, 1);
        AddLesson(tt, teacher, sec, 1);

        var violation = new NoTeacherConflictConstraint().Evaluate(TimetableContextFactory.Build(tt));

        Assert.Null(violation);
    }

    [Fact]
    public void Validator_ReportsConflict_WhenTimingsOverlap()
    {
        var tt = BigSchool(overlapping: true);
        var (teacher, mid, sec) = AddClasses(tt);
        AddLesson(tt, teacher, mid, 1);
        AddLesson(tt, teacher, sec, 1);

        var violation = new NoTeacherConflictConstraint().Evaluate(TimetableContextFactory.Build(tt));

        Assert.NotNull(violation);
    }

    [Fact]
    public async Task Solver_NeverOverlapsSharedTeacherAcrossStages()
    {
        // توقيتان متداخلان تماماً: المعلم لا يمكن أن يدرّس حصتين في نفس الوقت
        var tt = BigSchool(overlapping: true);
        var (teacher, mid, sec) = AddClasses(tt);
        AddLesson(tt, teacher, mid, 1);
        AddLesson(tt, teacher, sec, 1);

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(5), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        AssertNoTeacherOverlap(tt);
    }

    [Fact]
    public async Task Solver_AllowsSharedTeacherAtSamePeriod_WhenTimingsAreSeparate()
    {
        // توقيتان منفصلان: يمكن للمعلم أن يدرّس الفترتين في اليوم نفسه بلا تضارب
        var tt = BigSchool(overlapping: false);
        var (teacher, mid, sec) = AddClasses(tt);
        AddLesson(tt, teacher, mid, 1);
        AddLesson(tt, teacher, sec, 1);

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(5), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(0, result.ConflictsCount);
        AssertNoTeacherOverlap(tt);
    }

    private static void AssertNoTeacherOverlap(Timetable tt)
    {
        foreach (var group in tt.Lessons.GroupBy(l => (l.TeacherId, l.Day)))
        {
            var intervals = group
                .Select(l =>
                {
                    var stage = tt.GetStageForClass(l.ClassId);
                    var ok = tt.TryGetPeriodInterval(l.Period.Value, (int)l.Day, stage, out var s, out var e);
                    return (ok, s, e);
                })
                .Where(x => x.ok)
                .ToList();

            for (var i = 0; i < intervals.Count; i++)
            {
                for (var j = i + 1; j < intervals.Count; j++)
                {
                    var overlap = intervals[i].s < intervals[j].e && intervals[j].s < intervals[i].e;
                    Assert.False(overlap, "المعلم المشترك له حصتان متداخلتان زمنياً");
                }
            }
        }
    }
}

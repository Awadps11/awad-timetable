using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class WaitingScheduleGeneratorTests
{
    private static Timetable EmptyTimetable()
        => new(SchoolId.New(), "جدول", 2026, 2027, 1);

    private static Teacher TeacherAt(Timetable tt, string name)
        => new(tt.SchoolId, name, "أ");

    private static Class NewClass(Timetable tt, string name)
    {
        var cls = new Class(tt.SchoolId, name, 7, 'أ');
        tt.ClassGradeLevels[cls.Id] = 7;
        return cls;
    }

    private static void AddLesson(Timetable tt, Teacher t, Class c, DomainDayOfWeek day, int period)
    {
        var lesson = new Lesson((TimetableId)tt.Id, SubjectId.New(), (TeacherId)t.Id, (ClassId)c.Id,
            day, PeriodNumber.Create((byte)period));
        tt.AddLesson(lesson);
    }

    [Fact]
    public void Generate_OneDutyPerClassSlot_NoTeacherTwiceInSamePeriod()
    {
        var tt = EmptyTimetable();
        var c1 = NewClass(tt, "1");
        var c2 = NewClass(tt, "2");
        var t1 = TeacherAt(tt, "أ");
        var t2 = TeacherAt(tt, "ب");
        var t3 = TeacherAt(tt, "ج");
        var t4 = TeacherAt(tt, "د");

        AddLesson(tt, t1, c1, DomainDayOfWeek.Saturday, 1);
        AddLesson(tt, t2, c2, DomainDayOfWeek.Saturday, 1);

        var schedule = WaitingScheduleGenerator.Generate(
            tt, new[] { t1, t2, t3, t4 }, new[] { c1.Id, c2.Id }, new[] { 0 }, 1);

        var duties = schedule.Duties.Where(d => d.Day == DomainDayOfWeek.Saturday && d.Period == 1).ToList();
        Assert.Equal(2, duties.Count);
        Assert.Equal(2, duties.Select(d => d.TeacherId).Distinct().Count());
    }

    [Fact]
    public void Generate_TiersFollowLoad_LowestIsTier1()
    {
        var tt = EmptyTimetable();
        var c1 = NewClass(tt, "1");
        var low = TeacherAt(tt, "قليل");
        var mid = TeacherAt(tt, "متوسط");
        var high = TeacherAt(tt, "كثير");

        for (var p = 1; p <= 2; p++) AddLesson(tt, mid, c1, DomainDayOfWeek.Saturday, p);
        for (var p = 1; p <= 4; p++) AddLesson(tt, high, c1, DomainDayOfWeek.Sunday, p);

        var schedule = WaitingScheduleGenerator.Generate(
            tt, new[] { low, mid, high }, new[] { c1.Id }, new[] { 0, 1 }, 2);

        Assert.Equal(1, schedule.Tiers[low.Id]);
        Assert.Equal(3, schedule.Tiers[high.Id]);
    }

    [Fact]
    public void Generate_NeverAssignsDuty_WhileTeacherIsTeaching()
    {
        var tt = EmptyTimetable();
        var c1 = NewClass(tt, "1");
        var c2 = NewClass(tt, "2");
        var busy = TeacherAt(tt, "مشغول");
        var free = TeacherAt(tt, "حر");

        AddLesson(tt, busy, c1, DomainDayOfWeek.Saturday, 1);
        AddLesson(tt, free, c2, DomainDayOfWeek.Saturday, 1);

        var schedule = WaitingScheduleGenerator.Generate(
            tt, new[] { busy, free }, new[] { c1.Id, c2.Id }, new[] { 0 }, 1);

        Assert.DoesNotContain(schedule.Duties, d =>
            d.Day == DomainDayOfWeek.Saturday && d.Period == 1 && d.TeacherId == busy.Id);
    }

    [Fact]
    public void Generate_DistributesDutiesFairly()
    {
        var tt = EmptyTimetable();
        var c1 = NewClass(tt, "1");
        var c2 = NewClass(tt, "2");
        var teachers = Enumerable.Range(1, 6).Select(i => TeacherAt(tt, "م" + i)).ToList();

        // بلا حصص بعد: كل المعلمين أحرار، والتكليفات تحدد النصاب الأساسي فقط
        for (var i = 0; i < teachers.Count; i++)
        {
            tt.Assignments.Add(new TeachingAssignment
            {
                TeacherId = teachers[i].Id,
                SubjectId = Guid.NewGuid(),
                ClassId = c1.Id,
                WeeklyPeriods = 4 + i, // أنصبة متفاوتة
            });
        }

        var schedule = WaitingScheduleGenerator.Generate(
            tt, teachers, new[] { c1.Id, c2.Id }, new[] { 0 }, 3);

        // 6 تكليفات (فصلان × 3 فترات): تُعطى للأقل نصاباً أولاً حتى تتقارب الأنصبة
        Assert.Equal(6, schedule.Duties.Count);

        var totals = teachers.ToDictionary(t => t.Id, t => schedule.BaseLoads[t.Id] + schedule.WaitingLoads[t.Id]);
        Assert.True(totals.Values.Max() - totals.Values.Min() <= 2,
            string.Join(",", totals.Values));

        // الأقل نصاباً أساسياً يجب أن يحصل على تكليفات أكثر من الأعلى
        Assert.True(schedule.WaitingLoads[teachers[0].Id] >= schedule.WaitingLoads[teachers[5].Id]);
    }

    [Fact]
    public void Generate_ReportsUncovered_WhenNotEnoughFreeTeachers()
    {
        var tt = EmptyTimetable();
        var c1 = NewClass(tt, "1");
        var c2 = NewClass(tt, "2");
        var only = TeacherAt(tt, "وحيد");

        AddLesson(tt, only, c1, DomainDayOfWeek.Saturday, 1);
        AddLesson(tt, only, c2, DomainDayOfWeek.Saturday, 1);

        var schedule = WaitingScheduleGenerator.Generate(
            tt, new[] { only }, new[] { c1.Id, c2.Id }, new[] { 0 }, 1);

        Assert.Empty(schedule.Duties);
        Assert.Equal(2, schedule.Uncovered.Count);
    }

    [Fact]
    public void Generate_ExcludesAbsentAndExcluded()
    {
        var tt = EmptyTimetable();
        var c1 = NewClass(tt, "1");
        var a = TeacherAt(tt, "أ");
        var b = TeacherAt(tt, "ب");
        var c = TeacherAt(tt, "ج");

        AddLesson(tt, a, c1, DomainDayOfWeek.Saturday, 1);

        var schedule = WaitingScheduleGenerator.Generate(
            tt, new[] { a, b, c }, new[] { c1.Id }, new[] { 0 }, 1,
            excludedTeacherIds: new[] { b.Id },
            absentTeacherIds: new[] { c.Id });

        Assert.DoesNotContain(schedule.Duties, d => d.TeacherId == b.Id || d.TeacherId == c.Id);
    }

    [Fact]
    public void Resolve_PrefersDuty_ThenFallsBackToBackup()
    {
        var tt = EmptyTimetable();
        var c1 = NewClass(tt, "1");
        var t1 = TeacherAt(tt, "أ");
        var t2 = TeacherAt(tt, "ب");
        var t3 = TeacherAt(tt, "ج");

        AddLesson(tt, t1, c1, DomainDayOfWeek.Saturday, 1);

        var schedule = WaitingScheduleGenerator.Generate(
            tt, new[] { t1, t2, t3 }, new[] { c1.Id }, new[] { 0 }, 1);

        var duty = schedule.Duties.Single(d => d.Day == DomainDayOfWeek.Saturday && d.Period == 1 && d.ClassId == c1.Id);

        var normal = WaitingScheduleGenerator.Resolve(schedule, DomainDayOfWeek.Saturday, 1, c1.Id, Array.Empty<Guid>());
        Assert.Equal(duty.TeacherId, normal);

        var fallback = WaitingScheduleGenerator.Resolve(schedule, DomainDayOfWeek.Saturday, 1, c1.Id, new[] { duty.TeacherId });
        Assert.NotNull(fallback);
        Assert.NotEqual(duty.TeacherId, fallback);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenDutyAndBackupsAllAbsent()
    {
        var tt = EmptyTimetable();
        var c1 = NewClass(tt, "1");
        var only = TeacherAt(tt, "وحيد");

        AddLesson(tt, only, c1, DomainDayOfWeek.Saturday, 1);

        var schedule = WaitingScheduleGenerator.Generate(
            tt, new[] { only }, new[] { c1.Id }, new[] { 0 }, 1);

        var absentIds = schedule.Duties.Select(d => d.TeacherId)
            .Concat(schedule.Backups.Select(b => b.TeacherId))
            .ToArray();

        var result = WaitingScheduleGenerator.Resolve(schedule, DomainDayOfWeek.Saturday, 1, c1.Id, absentIds);
        Assert.Null(result);
    }

    [Fact]
    public void Generate_UsesAssignments_WhenNoLessonsYet()
    {
        var tt = EmptyTimetable();
        var a = TeacherAt(tt, "أ");
        var b = TeacherAt(tt, "ب");
        var c1 = NewClass(tt, "1");

        tt.Assignments.Add(new TeachingAssignment { TeacherId = a.Id, SubjectId = Guid.NewGuid(), ClassId = c1.Id, WeeklyPeriods = 10 });
        tt.Assignments.Add(new TeachingAssignment { TeacherId = b.Id, SubjectId = Guid.NewGuid(), ClassId = c1.Id, WeeklyPeriods = 2 });

        var schedule = WaitingScheduleGenerator.Generate(
            tt, new[] { a, b }, new[] { c1.Id }, new[] { 0 }, 2);

        Assert.True(schedule.BaseLoads[a.Id] > schedule.BaseLoads[b.Id]);
        Assert.Equal(1, schedule.Tiers[b.Id]);
    }
}

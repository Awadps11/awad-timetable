using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class WaitingLessonPlannerTests
{
    private static Timetable MakeTimetable(out Teacher t1, out Teacher t2, out Teacher t3, out Class c1)
    {
        var tt = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = new List<int> { 0 }, LessonsPerDay = 3 });

        var school = tt.SchoolId;
        t1 = new Teacher(school, "أ", "أ");
        t2 = new Teacher(school, "ب", "ب");
        t3 = new Teacher(school, "ج", "ج");
        c1 = new Class(school, "أول متوسط 1", 7, 'أ');
        tt.ClassGradeLevels[c1.Id] = 7;

        // حصة في كل فترة (المرحلة لها دروس في الفترات 1..3)
        for (var p = 1; p <= 3; p++)
        {
            var lesson = new Lesson((TimetableId)tt.Id, SubjectId.New(), (TeacherId)t1.Id, (ClassId)c1.Id,
                DomainDayOfWeek.Saturday, PeriodNumber.Create((byte)p));
            lesson.AttachReferences(null, t1, c1);
            tt.AddLesson(lesson);
        }

        return tt;
    }

    [Fact]
    public void Plan_CreatesOneLessonPerWaitingClassPerSlot()
    {
        var tt = MakeTimetable(out var t1, out var t2, out var t3, out _);
        var waiting = new List<(string, string)> { ("متوسط", "انتظار متوسط 1"), ("متوسط", "انتظار متوسط 2") };

        var plans = WaitingLessonPlanner.Plan(tt, new[] { t1, t2, t3 }, waiting);

        // 3 فترات × 2 شعبة انتظار
        Assert.Equal(6, plans.Count);
    }

    [Fact]
    public void Plan_NeverUsesTeacherTwiceInSameSlot()
    {
        var tt = MakeTimetable(out var t1, out var t2, out var t3, out _);
        var waiting = new List<(string, string)> { ("متوسط", "انتظار متوسط 1"), ("متوسط", "انتظار متوسط 2") };

        var plans = WaitingLessonPlanner.Plan(tt, new[] { t1, t2, t3 }, waiting);

        foreach (var slot in plans.GroupBy(p => (p.Day, p.Period)))
            Assert.Equal(slot.Count(), slot.Select(p => p.TeacherId).Distinct().Count());
    }

    [Fact]
    public void Plan_NeverAssignsTeacherWhoIsTeaching()
    {
        var tt = MakeTimetable(out var t1, out var t2, out var t3, out _);
        var waiting = new List<(string, string)> { ("متوسط", "انتظار متوسط 1") };

        var plans = WaitingLessonPlanner.Plan(tt, new[] { t1, t2, t3 }, waiting);

        // t1 يدرّس الفترات 1..3، فلا يُكلَّف انتظاراً فيها
        Assert.DoesNotContain(plans, p => p.TeacherId == t1.Id);
    }

    [Fact]
    public void Plan_EqualizesLoads()
    {
        var tt = MakeTimetable(out var t1, out var t2, out var t3, out _);
        var waiting = new List<(string, string)> { ("متوسط", "انتظار متوسط 1"), ("متوسط", "انتظار متوسط 2") };

        var plans = WaitingLessonPlanner.Plan(tt, new[] { t1, t2, t3 }, waiting);

        var totals = new[] { t1, t2, t3 }.ToDictionary(
            t => t.Id,
            t => tt.Lessons.Count(l => l.TeacherId.Value == t.Id) + plans.Count(p => p.TeacherId == t.Id));

        // t1 لديه 3 تدريس و0 انتظار؛ الآخران يتقاسمان 6 انتظار
        Assert.True(totals.Values.Max() - totals.Values.Min() <= 1, string.Join(",", totals.Values));
    }

    [Fact]
    public void Plan_ExcludesAbsentTeachers()
    {
        var tt = MakeTimetable(out var t1, out var t2, out var t3, out _);
        var waiting = new List<(string, string)> { ("متوسط", "انتظار متوسط 1") };

        var plans = WaitingLessonPlanner.Plan(tt, new[] { t1, t2, t3 }, waiting,
            absentTeacherIds: new[] { t2.Id });

        Assert.DoesNotContain(plans, p => p.TeacherId == t2.Id);
    }

    [Fact]
    public void Plan_RealisticSchool_36Teachers_6WaitingClasses_FullyCovered_AndBalanced()
    {
        // 36 معلماً، 12 فصلاً (6 متوسط + 6 ثانوي)، 6 أيام × 7 فترات، و6 شعب انتظار
        var tt = new Timetable(SchoolId.New(), "مدرسة كبيرة", 2026, 2027, 1);
        var days = new List<int> { 0, 1, 2, 3, 4, 5 };
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = days, LessonsPerDay = 7 });
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "ثانوي", WorkDays = days, LessonsPerDay = 7 });

        var school = tt.SchoolId;
        var teachers = Enumerable.Range(1, 36).Select(i => new Teacher(school, "م", i.ToString())).ToList();

        var classes = new List<Class>();
        for (var g = 7; g <= 9; g++)
            for (var s = 0; s < 2; s++)
            {
                var c = new Class(school, $"متوسط {g}-{s + 1}", g, (char)('أ' + s));
                tt.ClassGradeLevels[c.Id] = g;
                classes.Add(c);
            }
        for (var g = 10; g <= 12; g++)
            for (var s = 0; s < 2; s++)
            {
                var c = new Class(school, $"ثانوي {g}-{s + 1}", g, (char)('أ' + s));
                tt.ClassGradeLevels[c.Id] = g;
                classes.Add(c);
            }

        // كل خانة (يوم/فترة) فيها درس لكل فصل، والمعلم بالتناوب
        var ti = 0;
        foreach (var day in days)
            for (var p = 1; p <= 7; p++)
                foreach (var c in classes)
                {
                    var t = teachers[ti++ % teachers.Count];
                    var lesson = new Lesson((TimetableId)tt.Id, SubjectId.New(), (TeacherId)t.Id, (ClassId)c.Id,
                        (DomainDayOfWeek)day, PeriodNumber.Create((byte)p));
                    lesson.AttachReferences(null, t, c);
                    tt.AddLesson(lesson);
                }

        var waiting = new List<(string, string)>();
        for (var i = 1; i <= 3; i++)
        {
            waiting.Add(("متوسط", $"انتظار متوسط {i}"));
            waiting.Add(("ثانوي", $"انتظار ثانوي {i}"));
        }

        var plans = WaitingLessonPlanner.Plan(tt, teachers, waiting);

        // تغطية كاملة: 6 شعب × 6 أيام × 7 فترات
        Assert.Equal(252, plans.Count);

        foreach (var name in waiting.Select(w => w.Item2))
            Assert.Equal(42, plans.Count(p => p.WaitingClassName == name));

        // لا معلم مرتين في الفترة الواحدة
        foreach (var slot in plans.GroupBy(p => (p.Day, p.Period)))
            Assert.Equal(slot.Count(), slot.Select(p => p.TeacherId).Distinct().Count());

        // تقارب الأنصبة الإجمالية (تدريس + انتظار)
        var totals = teachers.ToDictionary(
            t => t.Id,
            t => tt.Lessons.Count(l => l.TeacherId.Value == t.Id) + plans.Count(p => p.TeacherId == t.Id));

        var min = totals.Values.Min();
        var max = totals.Values.Max();
        Assert.True(max - min <= 2, $"الأنصبة: {min}..{max}");

        // متوسط حول 21 حصة
        Assert.InRange(totals.Values.Average(), 20, 22);
    }

    [Fact]
    public void Plan_FillsAllPeriods_EvenWithoutRealLessonsInSomePeriods()
    {
        var tt = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = new List<int> { 0 }, LessonsPerDay = 3 });
        var school = tt.SchoolId;
        var t = new Teacher(school, "أ", "أ");
        var free = new Teacher(school, "ب", "ب");
        var c = new Class(school, "فصل", 7, 'أ');
        tt.ClassGradeLevels[c.Id] = 7;

        // حصة واحدة فقط في الفترة 1، ومع ذلك يجب ملء الفترات 1..3 للانتظار
        var lesson = new Lesson((TimetableId)tt.Id, SubjectId.New(), (TeacherId)t.Id, (ClassId)c.Id,
            DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
        lesson.AttachReferences(null, t, c);
        tt.AddLesson(lesson);

        var plans = WaitingLessonPlanner.Plan(tt, new[] { t, free }, new List<(string, string)> { ("متوسط", "انتظار متوسط 1") });

        Assert.Equal(3, plans.Count);
        Assert.Equal(new[] { 1, 2, 3 }, plans.Select(p => p.Period).OrderBy(p => p).ToArray());
    }

    [Fact]
    public void Plan_SixWaitingClasses_GetSevenPeriodsEveryDay()
    {
        var tt = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        var days = new List<int> { 0, 1 };
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = days, LessonsPerDay = 7 });
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "ثانوي", WorkDays = days, LessonsPerDay = 7 });

        var school = tt.SchoolId;
        var teachers = Enumerable.Range(1, 36).Select(i => new Teacher(school, "م", i.ToString())).ToList();

        var waiting = new List<(string, string)>();
        for (var i = 1; i <= 3; i++)
        {
            waiting.Add(("متوسط", $"انتظار متوسط {i}"));
            waiting.Add(("ثانوي", $"انتظار ثانوي {i}"));
        }

        var plans = WaitingLessonPlanner.Plan(tt, teachers, waiting);

        // 6 شعب × يومان × 7 فترات
        Assert.Equal(84, plans.Count);
        foreach (var ( _, name) in waiting)
            Assert.Equal(14, plans.Count(p => p.WaitingClassName == name));
    }
}

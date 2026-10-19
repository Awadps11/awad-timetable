using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

public class RuleCompilerTests
{
    private static Timetable OneLesson(out Teacher teacher, out Class cls, out Subject subject, int periods = 3)
    {
        var tt = new Timetable(SchoolId.New(), "جدول", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = new List<int> { 0 }, LessonsPerDay = periods });
        for (byte p = 1; p <= periods; p++)
            tt.PeriodTimes.Add(new PeriodTime { Period = p, Day = -1, Stage = null, StartMinutes = 420 + (p - 1) * 50, EndMinutes = 465 + (p - 1) * 50 });

        var school = tt.SchoolId;
        teacher = new Teacher(school, "م", "علم");
        cls = new Class(school, "فصل", 7, 'أ');
        subject = new Subject(school, "مادة");
        tt.ClassGradeLevels[cls.Id] = 7;

        var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id, (ClassId)cls.Id,
            DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
        lesson.AttachReferences(subject, teacher, cls);
        tt.AddLesson(lesson);
        return tt;
    }

    private static void AddRule(Timetable tt, RuleKind kind, RuleScope scope, Guid target, RulePriority priority, string parameters, bool enabled = true)
        => tt.Rules.Add(new RuleItem
        {
            Kind = kind,
            Scope = scope,
            TargetId = target,
            Priority = priority,
            Parameters = parameters,
            IsEnabled = enabled,
            Title = kind.ToString(),
        });

    [Fact]
    public void Compile_IgnoresIgnorePriority()
    {
        var tt = OneLesson(out var t, out _, out _, 1);
        AddRule(tt, RuleKind.Availability, RuleScope.Teacher, t.Id, RulePriority.Ignore, "{\"days\":[0],\"periods\":[1]}");

        var program = RuleCompiler.Compile(tt);

        Assert.True(program.IsEmpty);
    }

    [Fact]
    public void Compile_SkipsDisabledRules()
    {
        var tt = OneLesson(out var t, out _, out _, 1);
        AddRule(tt, RuleKind.Availability, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"periods\":[1]}", enabled: false);

        Assert.True(RuleCompiler.Compile(tt).IsEmpty);
    }

    [Fact]
    public void Compile_InvalidJson_DoesNotThrow_AndProducesNoRule()
    {
        var tt = OneLesson(out var t, out _, out _, 1);
        AddRule(tt, RuleKind.Availability, RuleScope.Teacher, t.Id, RulePriority.Hard, "not json");

        Assert.True(RuleCompiler.Compile(tt).IsEmpty);
    }

    [Fact]
    public void Compile_ParsesAvailabilityBlock()
    {
        var tt = OneLesson(out var t, out _, out _, 1);
        AddRule(tt, RuleKind.Availability, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"days\":[0],\"periods\":[2]}");

        var program = RuleCompiler.Compile(tt);

        Assert.Single(program.SlotBlocks);
        Assert.True(program.SlotBlocks[0].IsHard);
        Assert.Equal(new[] { 2 }, program.SlotBlocks[0].Periods);
    }

    [Fact]
    public async Task Solver_NeverUses_HardBlockedSlotByRule()
    {
        // فترة واحدة فقط، والقاعدة الصلبة تحجبها → فشل
        var tt = OneLesson(out var t, out _, out _, 1);
        AddRule(tt, RuleKind.Availability, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"periods\":[1]}");

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(3), UseParallel = false });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Solver_Respects_EndTimeHardRule()
    {
        var tt = OneLesson(out var t, out _, out var s, 4);
        // لا حصص بعد الفترة 1 لهذه المادة → يجب أن تُوضع في الفترة 1 فقط
        AddRule(tt, RuleKind.EndTime, RuleScope.Subject, s.Id, RulePriority.Hard, "{\"afterPeriod\":1}");

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(3), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, tt.Lessons.Single().Period.Value);
    }

    [Fact]
    public async Task Solver_Respects_MaxPerDayHardRule()
    {
        // مادتان لنفس الفصل في نفس اليوم، الحد اليومي 1 → لا يمكن وضع الاثنتين في يوم واحد
        var tt = OneLesson(out var t, out var c, out var s, 4);
        // يومان حتى تكون القاعدة قابلة للتحقق
        tt.StageConfigs[0].WorkDays = new List<int> { 0, 1 };
        AddRule(tt, RuleKind.SubjectDistribution, RuleScope.Subject, s.Id, RulePriority.Hard, "{\"maxPerDay\":1}");

        var second = new Lesson((TimetableId)tt.Id, (SubjectId)s.Id, (TeacherId)t.Id, (ClassId)c.Id,
            DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
        second.AttachReferences(s, t, c);
        tt.AddLesson(second);

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(3), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        var byDay = tt.Lessons.GroupBy(l => l.Day).Select(g => g.Count());
        Assert.All(byDay, count => Assert.True(count <= 1));
    }

    [Fact]
    public void Validator_ReportsHardRuleViolation()
    {
        var tt = OneLesson(out var t, out _, out _, 3);
        tt.Lessons.Single().Reschedule(DomainDayOfWeek.Saturday, PeriodNumber.Create(3));
        AddRule(tt, RuleKind.EndTime, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"afterPeriod\":1}");

        var violation = new UserRulesConstraint().Evaluate(TimetableContextFactory.Build(tt));

        Assert.NotNull(violation);
        Assert.Single(violation!.AffectedLessons);
    }

    [Fact]
    public void Validator_SilentWhenNoRules()
    {
        var tt = OneLesson(out _, out _, out _, 2);

        Assert.Null(new UserRulesConstraint().Evaluate(TimetableContextFactory.Build(tt)));
    }

    [Fact]
    public void Compile_ParsesMaxConsecutive_DailyLoad_AndGaps()
    {
        var tt = OneLesson(out var t, out _, out _, 2);
        AddRule(tt, RuleKind.MaxConsecutive, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxConsecutive\":4}");
        AddRule(tt, RuleKind.MaxGapsPerDay, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxGapsPerDay\":1}");
        AddRule(tt, RuleKind.DailyLoad, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"minPerDay\":3,\"maxPerDay\":6}");

        var program = RuleCompiler.Compile(tt);

        Assert.Single(program.MaxConsecutives);
        Assert.Equal(4, program.MaxConsecutives[0].Max);
        Assert.Single(program.MaxGapsPerDays);
        Assert.Equal(1, program.MaxGapsPerDays[0].Max);
        Assert.Single(program.DailyLoads);
        Assert.Equal(3, program.DailyLoads[0].Min);
        Assert.Equal(6, program.DailyLoads[0].Max);
    }

    [Fact]
    public async Task Solver_Respects_MaxConsecutiveHardRule()
    {
        var tt = OneLesson(out var t, out var c, out var s, 5);
        AddRule(tt, RuleKind.MaxConsecutive, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxConsecutive\":2}");

        for (var i = 0; i < 3; i++)
        {
            var extra = new Lesson((TimetableId)tt.Id, (SubjectId)s.Id, (TeacherId)t.Id, (ClassId)c.Id,
                DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
            extra.AttachReferences(s, t, c);
            tt.AddLesson(extra);
        }

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(5), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(MaxRun(tt.Lessons.Select(l => (int)l.Period.Value)) <= 2);
    }

    [Fact]
    public async Task Solver_Respects_DailyLoadMaxHardRule()
    {
        var tt = OneLesson(out var t, out var c, out var s, 5);
        tt.StageConfigs[0].WorkDays = new List<int> { 0, 1 };
        AddRule(tt, RuleKind.DailyLoad, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxPerDay\":3}");

        for (var i = 0; i < 3; i++)
        {
            var extra = new Lesson((TimetableId)tt.Id, (SubjectId)s.Id, (TeacherId)t.Id, (ClassId)c.Id,
                DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
            extra.AttachReferences(s, t, c);
            tt.AddLesson(extra);
        }

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(5), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.All(tt.Lessons.GroupBy(l => l.Day), g => Assert.True(g.Count() <= 3));
    }

    [Fact]
    public void Validator_Reports_MaxConsecutiveViolation()
    {
        var tt = OneLesson(out var t, out var c, out var s, 3);
        AddRule(tt, RuleKind.MaxConsecutive, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxConsecutive\":2}");

        for (byte p = 1; p <= 3; p++)
        {
            var lesson = tt.Lessons.SingleOrDefault(l => l.Period.Value == p);
            if (lesson is null)
            {
                lesson = new Lesson((TimetableId)tt.Id, (SubjectId)s.Id, (TeacherId)t.Id, (ClassId)c.Id,
                    DomainDayOfWeek.Saturday, PeriodNumber.Create(p));
                lesson.AttachReferences(s, t, c);
                tt.AddLesson(lesson);
            }
            lesson.Reschedule(DomainDayOfWeek.Saturday, PeriodNumber.Create(p));
        }

        var violation = new UserRulesConstraint().Evaluate(TimetableContextFactory.Build(tt));

        Assert.NotNull(violation);
    }

    [Fact]
    public void Compile_Parses_DayScopeRules()
    {
        var tt = OneLesson(out var t, out _, out _, 2);
        AddRule(tt, RuleKind.MaxDaysPerWeek, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxDaysPerWeek\":4}");
        AddRule(tt, RuleKind.MaxGapsPerWeek, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxGapsPerWeek\":3}");
        AddRule(tt, RuleKind.MaxSpanPerDay, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxSpanPerDay\":6}");
        AddRule(tt, RuleKind.BeginsEarly, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxBeginningsSecondHour\":1}");

        var program = RuleCompiler.Compile(tt);

        Assert.Equal(4, Assert.Single(program.MaxDaysPerWeeks).Max);
        Assert.Equal(3, Assert.Single(program.MaxGapsPerWeeks).Max);
        Assert.Equal(6, Assert.Single(program.MaxSpanPerDays).Max);
        Assert.Equal(1, Assert.Single(program.BeginsEarlys).Max);
    }

    [Fact]
    public void Compile_Parses_SubjectPair_And_TwicePerDay()
    {
        var tt = OneLesson(out var t, out _, out var s, 2);
        var s2 = new Subject(tt.SchoolId, "مادة2");

        AddRule(tt, RuleKind.SubjectPair, RuleScope.Subject, s.Id, RulePriority.Hard,
            $"{{\"secondTargetId\":\"{s2.Id}\",\"noSameDay\":true,\"noConsecutive\":false}}");
        AddRule(tt, RuleKind.SubjectTwicePerDay, RuleScope.Subject, s.Id, RulePriority.Hard,
            "{\"mustBeConsecutive\":true}");

        var program = RuleCompiler.Compile(tt);

        var pair = Assert.Single(program.SubjectPairs);
        Assert.Equal(s.Id, pair.SubjectA);
        Assert.Equal(s2.Id, pair.SubjectB);
        Assert.True(pair.NoSameDay);
        Assert.False(pair.NoConsecutive);
        Assert.True(Assert.Single(program.SubjectTwicePerDays).MustBeConsecutive);
    }

    [Fact]
    public void Compile_Parses_Block_MinDays_And_RoomRules()
    {
        var tt = OneLesson(out var t, out _, out _, 2);
        var room = new Room(tt.SchoolId, "قاعة");

        AddRule(tt, RuleKind.BlockLessons, RuleScope.Teacher, t.Id, RulePriority.Hard, "{}");
        AddRule(tt, RuleKind.MinDaysBetweenActivities, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"minDaysBetween\":2}");
        AddRule(tt, RuleKind.PreferredRoom, RuleScope.Subject, t.Id, RulePriority.Hard,
            $"{{\"secondTargetId\":\"{room.Id}\"}}");
        AddRule(tt, RuleKind.RoomChangeLimit, RuleScope.Teacher, t.Id, RulePriority.Hard,
            "{\"maxRoomChangesPerDay\":2}");

        var program = RuleCompiler.Compile(tt);

        Assert.Single(program.BlockLessons);
        Assert.Equal(2, Assert.Single(program.MinDaysBetweens).MinDays);
        Assert.Equal(room.Id, Assert.Single(program.PreferredRooms).RoomId);
        Assert.Equal(2, Assert.Single(program.RoomChangeLimits).PerDay);
    }

    [Fact]
    public async Task Solver_Respects_MaxDaysPerWeekHardRule()
    {
        var tt = OneLesson(out var t, out var c, out var s, 4);
        tt.StageConfigs[0].WorkDays = new List<int> { 0, 1 };
        AddRule(tt, RuleKind.MaxDaysPerWeek, RuleScope.Class, c.Id, RulePriority.Hard, "{\"maxDaysPerWeek\":1}");

        for (var i = 0; i < 2; i++)
        {
            var extra = new Lesson((TimetableId)tt.Id, (SubjectId)s.Id, (TeacherId)t.Id, (ClassId)c.Id,
                DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
            extra.AttachReferences(s, t, c);
            tt.AddLesson(extra);
        }

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(5), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Single(tt.Lessons.Select(l => l.Day).Distinct());
    }

    [Fact]
    public void Validator_Reports_SubjectPairNoSameDayViolation()
    {
        var tt = OneLesson(out var t, out var c, out var s, 4);
        tt.StageConfigs[0].WorkDays = new List<int> { 0, 1 };
        var s2 = new Subject(tt.SchoolId, "مادة2");

        var b = new Lesson((TimetableId)tt.Id, (SubjectId)s2.Id, (TeacherId)t.Id, (ClassId)c.Id,
            DomainDayOfWeek.Saturday, PeriodNumber.Create(2));
        b.AttachReferences(s2, t, c);
        tt.AddLesson(b);

        AddRule(tt, RuleKind.SubjectPair, RuleScope.Subject, s.Id, RulePriority.Hard,
            $"{{\"secondTargetId\":\"{s2.Id}\",\"noSameDay\":true}}");

        var violation = new UserRulesConstraint().Evaluate(TimetableContextFactory.Build(tt));

        Assert.NotNull(violation);
    }

    [Fact]
    public void Compile_Parses_MinDaysPerWeek()
    {
        var tt = OneLesson(out var t, out _, out _, 2);
        AddRule(tt, RuleKind.MinDaysPerWeek, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"minDaysPerWeek\":5}");

        var program = RuleCompiler.Compile(tt);

        Assert.Equal(5, Assert.Single(program.MinDaysPerWeeks).Min);
    }

    [Fact]
    public async Task Solver_Respects_MinDaysPerWeekHardRule()
    {
        var tt = OneLesson(out var t, out var c, out var s, 3);
        tt.StageConfigs[0].WorkDays = new List<int> { 0, 1, 2, 3, 4 };
        AddRule(tt, RuleKind.MinDaysPerWeek, RuleScope.Class, c.Id, RulePriority.Hard, "{\"minDaysPerWeek\":5}");

        for (var i = 0; i < 4; i++)
        {
            var extra = new Lesson((TimetableId)tt.Id, (SubjectId)s.Id, (TeacherId)t.Id, (ClassId)c.Id,
                DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
            extra.AttachReferences(s, t, c);
            tt.AddLesson(extra);
        }

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(5), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(5, tt.Lessons.Select(l => l.Day).Distinct().Count());
    }

    [Fact]
    public void Validator_Reports_MinDaysPerWeekViolation()
    {
        var tt = OneLesson(out var t, out _, out _, 3);
        tt.StageConfigs[0].WorkDays = new List<int> { 0, 1, 2, 3, 4 };
        AddRule(tt, RuleKind.MinDaysPerWeek, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"minDaysPerWeek\":5}");

        var violation = new UserRulesConstraint().Evaluate(TimetableContextFactory.Build(tt));

        Assert.NotNull(violation);
    }

    [Fact]
    public void Validator_Reports_RoomChangeLimitViolation()
    {
        var tt = OneLesson(out var t, out var c, out var s, 3);
        var roomA = new Room(tt.SchoolId, "أ");
        var roomB = new Room(tt.SchoolId, "ب");

        while (tt.Lessons.Count < 3)
        {
            var extra = new Lesson((TimetableId)tt.Id, (SubjectId)s.Id, (TeacherId)t.Id, (ClassId)c.Id,
                DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
            extra.AttachReferences(s, t, c);
            tt.AddLesson(extra);
        }

        var lessons = tt.Lessons.ToList();
        lessons[0].Reschedule(DomainDayOfWeek.Saturday, PeriodNumber.Create(1));
        lessons[1].Reschedule(DomainDayOfWeek.Saturday, PeriodNumber.Create(2));
        lessons[2].Reschedule(DomainDayOfWeek.Saturday, PeriodNumber.Create(3));
        lessons[0].AssignRoom((RoomId)roomA.Id);
        lessons[1].AssignRoom((RoomId)roomB.Id);
        lessons[2].AssignRoom((RoomId)roomA.Id);

        AddRule(tt, RuleKind.RoomChangeLimit, RuleScope.Teacher, t.Id, RulePriority.Hard, "{\"maxRoomChangesPerDay\":1}");

        var violation = new UserRulesConstraint().Evaluate(TimetableContextFactory.Build(tt));

        Assert.NotNull(violation);
    }

    [Fact]
    public void Split_Lesson_Creates_Group_Parts_And_MinDaysRule()
    {
        var tt = OneLesson(out _, out _, out _, 4);
        var source = tt.Lessons.Single();
        source.SetDuration(Duration.FromMinutes(90));

        var result = LessonSplitter.Split(tt, source, 3, minDaysBetween: 2);

        Assert.Equal(3, result.Parts.Count);
        Assert.Equal(3, tt.Lessons.Count);
        Assert.Single(tt.Groups);
        Assert.All(result.Parts, p => Assert.Equal(result.GroupId, p.GroupId));
        Assert.Equal(3, result.Parts.Select(p => p.Day).Distinct().Count());
        Assert.All(result.Parts, p => Assert.Equal(30, (int)p.Duration));

        Assert.NotNull(result.MinDaysRule);
        Assert.Equal(RuleKind.MinDaysBetweenActivities, result.MinDaysRule!.Kind);
        Assert.Equal(RuleScope.Subject, result.MinDaysRule.Scope);

        var program = RuleCompiler.Compile(tt);
        Assert.Equal(2, Assert.Single(program.MinDaysBetweens).MinDays);
    }

    [Fact]
    public void Split_Keeps_Duration_When_Not_Divisible()
    {
        var tt = OneLesson(out _, out _, out _, 4);
        var source = tt.Lessons.Single();

        var result = LessonSplitter.Split(tt, source, 2);

        Assert.All(result.Parts, p => Assert.Equal(45, (int)p.Duration));
        Assert.Null(result.MinDaysRule);
    }

    [Fact]
    public void Split_Throws_On_Invalid_Parts()
    {
        var tt = OneLesson(out _, out _, out _, 4);
        var source = tt.Lessons.Single();

        Assert.Throws<ArgumentOutOfRangeException>(() => LessonSplitter.Split(tt, source, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LessonSplitter.Split(tt, source, 9));
    }

    private static int MaxRun(IEnumerable<int> periods)
    {
        var ordered = periods.Distinct().OrderBy(x => x).ToList();
        var run = 0;
        var prev = int.MinValue;
        var max = 0;

        foreach (var p in ordered)
        {
            run = p == prev + 1 ? run + 1 : 1;
            prev = p;
            if (run > max) max = run;
        }

        return max;
    }
}

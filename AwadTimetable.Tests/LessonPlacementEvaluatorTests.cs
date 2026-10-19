using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>اختبارات تقييم مواضع الحصص وتشخيصها (نفس قواعد السحب والإفلات).</summary>
public class LessonPlacementEvaluatorTests
{
    private static readonly SchoolId School = SchoolId.New();

    private static Timetable NewTimetable()
        => new(School, "اختبار", 2026, 2027, 1);

    private static Lesson AddLesson(
        Timetable tt, Guid subjectId, Guid teacherId, Guid classId,
        DomainDayOfWeek day, int period, Guid? roomId = null, bool fixedLesson = false)
    {
        var lesson = new Lesson(
            (TimetableId)tt.Id,
            (SubjectId)subjectId,
            (TeacherId)teacherId,
            (ClassId)classId,
            day,
            PeriodNumber.Create((byte)period));
        if (roomId.HasValue) lesson.AssignRoom((RoomId)roomId.Value);
        if (fixedLesson) lesson.Fix(true);
        tt.AddLesson(lesson);
        return lesson;
    }

    [Fact]
    public void Evaluate_EmptySlot_ReturnsGood()
    {
        var tt = NewTimetable();
        var cls = Guid.NewGuid();
        var teacher = Guid.NewGuid();
        var moving = AddLesson(tt, Guid.NewGuid(), teacher, cls, DomainDayOfWeek.Saturday, 1);

        var (state, _) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), cls);

        Assert.Equal(PlacementState.Good, state);
    }

    [Fact]
    public void Evaluate_FixedLesson_ReturnsForbidden()
    {
        var tt = NewTimetable();
        var cls = Guid.NewGuid();
        var moving = AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), cls, DomainDayOfWeek.Saturday, 1, fixedLesson: true);

        var (state, reason) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), cls);

        Assert.Equal(PlacementState.Forbidden, state);
        Assert.Contains("مثبتة", reason);
    }

    [Fact]
    public void Evaluate_SameClassOccupiedByMovableLesson_ReturnsAcceptable()
    {
        var tt = NewTimetable();
        var cls = Guid.NewGuid();
        var moving = AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), cls, DomainDayOfWeek.Saturday, 1);
        AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), cls, DomainDayOfWeek.Monday, 3);

        var (state, _) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), cls);

        Assert.Equal(PlacementState.Acceptable, state);
    }

    [Fact]
    public void Evaluate_SameClassOccupiedByFixedLesson_ReturnsForbidden()
    {
        var tt = NewTimetable();
        var cls = Guid.NewGuid();
        var moving = AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), cls, DomainDayOfWeek.Saturday, 1);
        AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), cls, DomainDayOfWeek.Monday, 3, fixedLesson: true);

        var (state, _) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), cls);

        Assert.Equal(PlacementState.Forbidden, state);
    }

    [Fact]
    public void Evaluate_OtherClassTeacherBusy_ReturnsForbidden()
    {
        var tt = NewTimetable();
        var teacher = Guid.NewGuid();
        var moving = AddLesson(tt, Guid.NewGuid(), teacher, Guid.NewGuid(), DomainDayOfWeek.Saturday, 1);
        AddLesson(tt, Guid.NewGuid(), teacher, Guid.NewGuid(), DomainDayOfWeek.Monday, 3);

        var targetClass = Guid.NewGuid();
        var (state, reason) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), targetClass);

        Assert.Equal(PlacementState.Forbidden, state);
        Assert.Contains("مشغولة", reason);
    }

    [Fact]
    public void Evaluate_OtherClassRoomBusy_ReturnsForbidden()
    {
        var tt = NewTimetable();
        var room = Guid.NewGuid();
        var moving = AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Saturday, 1, roomId: room);
        AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Monday, 3, roomId: room);

        var (state, _) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), Guid.NewGuid());

        Assert.Equal(PlacementState.Forbidden, state);
    }

    [Fact]
    public void Evaluate_OtherClassFreeSlotNoConflict_ReturnsGood()
    {
        var tt = NewTimetable();
        var moving = AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Saturday, 1);
        // حصة في نفس الوقت لكن لا تشترك في معلم/قاعة/فصل
        AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Monday, 3);

        var (state, _) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), Guid.NewGuid());

        Assert.Equal(PlacementState.Good, state);
    }

    [Fact]
    public void Evaluate_OtherClassNotAssigned_ReturnsForbidden()
    {
        var tt = NewTimetable();
        var subject = Guid.NewGuid();
        var teacher = Guid.NewGuid();
        var classA = Guid.NewGuid();
        var classB = Guid.NewGuid();

        tt.Assignments.Add(new TeachingAssignment { TeacherId = teacher, SubjectId = subject, ClassId = classA, WeeklyPeriods = 3 });
        var moving = AddLesson(tt, subject, teacher, classA, DomainDayOfWeek.Saturday, 1);

        var (state, reason) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), classB);

        Assert.Equal(PlacementState.Forbidden, state);
        Assert.Contains("غير مُسندة", reason);
    }

    [Fact]
    public void Evaluate_OtherClassAssigned_ReturnsGood()
    {
        var tt = NewTimetable();
        var subject = Guid.NewGuid();
        var teacher = Guid.NewGuid();
        var classA = Guid.NewGuid();
        var classB = Guid.NewGuid();

        tt.Assignments.Add(new TeachingAssignment { TeacherId = teacher, SubjectId = subject, ClassId = classA, WeeklyPeriods = 2 });
        tt.Assignments.Add(new TeachingAssignment { TeacherId = teacher, SubjectId = subject, ClassId = classB, WeeklyPeriods = 2 });
        var moving = AddLesson(tt, subject, teacher, classA, DomainDayOfWeek.Saturday, 1);

        var (state, _) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), classB);

        Assert.Equal(PlacementState.Good, state);
    }

    [Fact]
    public void Evaluate_OtherClass_DisabledAssignment_ReturnsForbidden()
    {
        var tt = NewTimetable();
        var subject = Guid.NewGuid();
        var teacher = Guid.NewGuid();
        var classA = Guid.NewGuid();
        var classB = Guid.NewGuid();

        tt.Assignments.Add(new TeachingAssignment { TeacherId = teacher, SubjectId = subject, ClassId = classA, WeeklyPeriods = 2 });
        tt.Assignments.Add(new TeachingAssignment { TeacherId = teacher, SubjectId = subject, ClassId = classB, WeeklyPeriods = 2, IsEnabled = false });
        var moving = AddLesson(tt, subject, teacher, classA, DomainDayOfWeek.Saturday, 1);

        var (state, _) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), classB);

        Assert.Equal(PlacementState.Forbidden, state);
    }

    [Fact]
    public void Evaluate_CurrentPosition_ReturnsGood()
    {
        var tt = NewTimetable();
        var cls = Guid.NewGuid();
        var moving = AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), cls, DomainDayOfWeek.Monday, 3);

        var (state, _) = LessonPlacementEvaluator.Evaluate(tt, moving, DomainDayOfWeek.Monday, PeriodNumber.Create(3), cls);

        Assert.Equal(PlacementState.Good, state);
    }

    [Fact]
    public void Evaluate_NullArguments_Throw()
    {
        var tt = NewTimetable();
        var lesson = AddLesson(tt, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DomainDayOfWeek.Saturday, 1);

        Assert.Throws<ArgumentNullException>(() =>
            LessonPlacementEvaluator.Evaluate(null!, lesson, DomainDayOfWeek.Monday, PeriodNumber.Create(1), Guid.NewGuid()));
        Assert.Throws<ArgumentNullException>(() =>
            LessonPlacementEvaluator.Evaluate(tt, null!, DomainDayOfWeek.Monday, PeriodNumber.Create(1), Guid.NewGuid()));
    }

    [Fact]
    public void Diagnose_CountsGoodSlots_AndGivesReasons()
    {
        var tt = NewTimetable();
        var teacher = Guid.NewGuid();
        var lesson = AddLesson(tt, Guid.NewGuid(), teacher, Guid.NewGuid(), DomainDayOfWeek.Saturday, 0);

        // المعلم مشغول في السبت الفترة 1 بحصة أخرى لفصل آخر
        AddLesson(tt, Guid.NewGuid(), teacher, Guid.NewGuid(), DomainDayOfWeek.Saturday, 1);

        var days = new[] { DomainDayOfWeek.Saturday };
        var diag = LessonPlacementEvaluator.Diagnose(tt, lesson, days);

        // في السبت: الفترة 1 مشغولة بمعلم الحصة، والفترات 2..12 فارغة بلا تعارض (الفصل بلا حصص أخرى)
        Assert.Equal(11, diag.GoodSlots.Count);
        Assert.DoesNotContain(diag.GoodSlots, s => s.Day == DomainDayOfWeek.Saturday && s.Period == 1);
        Assert.Contains(diag.ReasonCounts, kv => kv.Key.Contains("مشغولة") && kv.Value == 1);
    }

    [Fact]
    public void Diagnose_TeacherUnavailable_AddsAvailabilityReason()
    {
        var tt = NewTimetable();
        var teacher = Guid.NewGuid();
        var lesson = AddLesson(tt, Guid.NewGuid(), teacher, Guid.NewGuid(), DomainDayOfWeek.Saturday, 0);

        tt.TeacherAvailabilities.Add(new TeacherAvailability(
            (TeacherId)teacher, DomainDayOfWeek.Sunday, PeriodNumber.Create(2), isAvailable: false));

        var diag = LessonPlacementEvaluator.Diagnose(tt, lesson, new[] { DomainDayOfWeek.Sunday });

        Assert.Contains(diag.ReasonCounts, kv => kv.Key.Contains("غير متاح"));
        Assert.DoesNotContain(diag.GoodSlots, s => s.Period == 2);
    }

    [Fact]
    public void Diagnose_ReasonsAreSortedByFrequency()
    {
        var tt = NewTimetable();
        var teacher = Guid.NewGuid();
        var lesson = AddLesson(tt, Guid.NewGuid(), teacher, Guid.NewGuid(), DomainDayOfWeek.Saturday, 0);

        // حصص كثيرة للمعلم عبر فترات متعددة لجعل سبب «مشغولة» الأكثر تكرارًا
        for (var p = 1; p <= 5; p++)
            AddLesson(tt, Guid.NewGuid(), teacher, Guid.NewGuid(), DomainDayOfWeek.Sunday, p);

        var diag = LessonPlacementEvaluator.Diagnose(tt, lesson, new[] { DomainDayOfWeek.Sunday });

        Assert.NotEmpty(diag.ReasonCounts);
        for (var i = 1; i < diag.ReasonCounts.Count; i++)
            Assert.True(diag.ReasonCounts[i - 1].Value >= diag.ReasonCounts[i].Value);
    }
}

using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبارات القيود اللينة: كل قيد يجب أن يكتشف انتهاكه فعليًا من بيانات الجدول،
/// وأن يعود null عندما لا تكون هناك مشكلة أو لا تتوفّر بيانات لقياسها.
/// </summary>
public class SoftConstraintTests
{
    private static readonly SchoolId School = SchoolId.New();

    private static TimetableContext Context(
        Timetable timetable,
        IEnumerable<Lesson> lessons,
        IEnumerable<Teacher>? teachers = null,
        IEnumerable<Subject>? subjects = null,
        IEnumerable<Class>? classes = null,
        IEnumerable<Room>? rooms = null,
        IEnumerable<TeacherAvailability>? availabilities = null)
        => new(
            timetable,
            lessons.ToList(),
            teachers?.ToList() ?? [],
            rooms?.ToList() ?? [],
            classes?.ToList() ?? [],
            subjects?.ToList() ?? [],
            availabilities?.ToList() ?? []);

    private static Lesson NewLesson(Timetable t, Subject s, Teacher teacher, Class cls, int day, int period)
        => new((TimetableId)t.Id, (SubjectId)s.Id, (TeacherId)teacher.Id, (ClassId)cls.Id,
            (DomainDayOfWeek)day, PeriodNumber.Create((byte)period));

    private static (Timetable t, Subject s, Teacher teacher, Class cls) NewFixture(string name = "جدول")
    {
        var t = new Timetable(School, name, 2026, 2027, 1);
        return (t, new Subject(School, "مادة"), new Teacher(School, "أحمد", "علي"), new Class(School, "أولى", 1, 'أ'));
    }

    // --------------------------------------------------------------------
    // الفترات المتتالية
    // --------------------------------------------------------------------

    [Fact]
    public void ConsecutivePeriods_ReportsRunLongerThanLimit()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 0, 2),
            NewLesson(t, s, teacher, cls, 0, 3),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        var violation = new ConsecutivePeriodsConstraint().Evaluate(
            Context(t, lessons, [teacher], [s], classes: [cls]));

        Assert.NotNull(violation);
        Assert.Equal(3, violation.AffectedLessons.Count);
        Assert.Equal(ConstraintWeight.Normal.Value * 3, violation.Penalty.Value, 3);
    }

    [Fact]
    public void ConsecutivePeriods_AllowsRunsUpToLimit()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 0, 2),
            NewLesson(t, s, teacher, cls, 0, 5),
            NewLesson(t, s, teacher, cls, 0, 6),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        var violation = new ConsecutivePeriodsConstraint().Evaluate(
            Context(t, lessons, [teacher], [s], classes: [cls]));

        Assert.Null(violation);
    }

    // --------------------------------------------------------------------
    // الحد الأدنى للأيام الأسبوعية
    // --------------------------------------------------------------------

    [Fact]
    public void MinDaysPerWeek_ReportsClassConfinedToOneDay()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 0, 2),
            NewLesson(t, s, teacher, cls, 0, 3),
            NewLesson(t, s, teacher, cls, 0, 4),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        var violation = new MinDaysPerWeekConstraint().Evaluate(
            Context(t, lessons, [teacher], [s], classes: [cls]));

        Assert.NotNull(violation);
        Assert.Contains("3 أيام", violation.Message);
    }

    [Fact]
    public void MinDaysPerWeek_AllowsClassSpreadAcrossThreeDays()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 1, 1),
            NewLesson(t, s, teacher, cls, 2, 1),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        var violation = new MinDaysPerWeekConstraint().Evaluate(
            Context(t, lessons, [teacher], [s], classes: [cls]));

        Assert.Null(violation);
    }

    // --------------------------------------------------------------------
    // توزيع الحصص على الأيام
    // --------------------------------------------------------------------

    [Fact]
    public void Distribution_ReportsDayWithMoreThanItsShare()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 0, 2),
            NewLesson(t, s, teacher, cls, 0, 3),
            NewLesson(t, s, teacher, cls, 1, 1),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        var violation = new DistributionConstraint().Evaluate(
            Context(t, lessons, [teacher], [s], classes: [cls]));

        Assert.NotNull(violation);
        // الهدف = ⌈4 / 6 أيام⟩ = 1 → الحصص الثلاث في السبت تجاوزت الهدف
        Assert.Equal(3, violation.AffectedLessons.Count);
    }

    [Fact]
    public void Distribution_AllowsEvenSpread()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 1, 1),
            NewLesson(t, s, teacher, cls, 2, 1),
            NewLesson(t, s, teacher, cls, 3, 1),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        Assert.Null(new DistributionConstraint().Evaluate(
            Context(t, lessons, [teacher], [s], classes: [cls])));
    }

    // --------------------------------------------------------------------
    // توزيع المواد
    // --------------------------------------------------------------------

    [Fact]
    public void SubjectDistribution_ReportsSubjectRepeatedInSameDay()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 0, 5),
            NewLesson(t, s, teacher, cls, 2, 1),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        var violation = new SubjectDistributionConstraint().Evaluate(
            Context(t, lessons, [teacher], [s], classes: [cls]));

        Assert.NotNull(violation);
        // حصصا المادة في السبت = 2 والهدف ⌈3 / 6⟩ = 1
        Assert.Equal(2, violation.AffectedLessons.Count);
    }

    [Fact]
    public void SubjectDistribution_AllowsSingleLessonPerDay()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 2, 1),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        Assert.Null(new SubjectDistributionConstraint().Evaluate(
            Context(t, lessons, [teacher], [s], classes: [cls])));
    }

    // --------------------------------------------------------------------
    // تفضيل المعلم (الفترات المُعلنة)
    // --------------------------------------------------------------------

    [Fact]
    public void TeacherPreference_ReportsLessonOutsideDeclaredAvailability()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lesson = NewLesson(t, s, teacher, cls, (int)DomainDayOfWeek.Sunday, 1);
        t.AddLesson(lesson);

        var availability = new List<TeacherAvailability>
        {
            new((TeacherId)teacher.Id, DomainDayOfWeek.Saturday, PeriodNumber.Create(1), isAvailable: true)
        };

        var violation = new TeacherPreferenceConstraint().Evaluate(
            Context(t, [lesson], [teacher], [s], classes: [cls], availabilities: availability));

        Assert.NotNull(violation);
        Assert.Contains("لم يُعلن", violation.Message);
    }

    [Fact]
    public void TeacherPreference_AcceptsLessonInDeclaredSlot()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lesson = NewLesson(t, s, teacher, cls, (int)DomainDayOfWeek.Saturday, 1);
        t.AddLesson(lesson);

        var availability = new List<TeacherAvailability>
        {
            new((TeacherId)teacher.Id, DomainDayOfWeek.Saturday, PeriodNumber.Create(1), isAvailable: true)
        };

        Assert.Null(new TeacherPreferenceConstraint().Evaluate(
            Context(t, [lesson], [teacher], [s], classes: [cls], availabilities: availability)));
    }

    [Fact]
    public void TeacherPreference_IsSilentWhenNoAvailabilityDeclared()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lesson = NewLesson(t, s, teacher, cls, 0, 1);
        t.AddLesson(lesson);

        Assert.Null(new TeacherPreferenceConstraint().Evaluate(
            Context(t, [lesson], [teacher], [s], classes: [cls])));
    }

    // --------------------------------------------------------------------
    // تفضيل الغرفة (متطلبات المادة)
    // --------------------------------------------------------------------

    [Fact]
    public void RoomPreference_ReportsLabSubjectInOrdinaryRoom()
    {
        var (t, s, teacher, cls) = NewFixture();
        s.UpdateRequirements(requiresLab: true, requiresSpecialRoom: false, minStudents: 1, maxStudents: 30);

        var room = new Room(School, "قاعة عادية", "ق1", 30);
        var lesson = NewLesson(t, s, teacher, cls, 0, 1);
        lesson.AssignRoom((RoomId)room.Id);
        t.AddLesson(lesson);

        var violation = new RoomPreferenceConstraint().Evaluate(
            Context(t, [lesson], [teacher], [s], [cls], [room]));

        Assert.NotNull(violation);
        Assert.Contains("معمل", violation.Message);
    }

    [Fact]
    public void RoomPreference_AcceptsLabSubjectInLabRoom()
    {
        var (t, s, teacher, cls) = NewFixture();
        s.UpdateRequirements(requiresLab: true, requiresSpecialRoom: false, minStudents: 1, maxStudents: 30);

        var lab = new Room(School, "معمل", "م1", 30);
        lab.UpdateType(isLab: true, isGym: false, isLibrary: false, isMusicRoom: false, isArtRoom: false);

        var lesson = NewLesson(t, s, teacher, cls, 0, 1);
        lesson.AssignRoom((RoomId)lab.Id);
        t.AddLesson(lesson);

        Assert.Null(new RoomPreferenceConstraint().Evaluate(
            Context(t, [lesson], [teacher], [s], [cls], [lab])));
    }

    [Fact]
    public void RoomPreference_IsSilentWhenNoRoomsOrSubjectsKnown()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lesson = NewLesson(t, s, teacher, cls, 0, 1);
        t.AddLesson(lesson);

        Assert.Null(new RoomPreferenceConstraint().Evaluate(Context(t, [lesson])));
    }

    // --------------------------------------------------------------------
    // عبء المعلم
    // --------------------------------------------------------------------

    [Fact]
    public void TeacherWorkload_ReportsTeacherBeyondDailyLimit()
    {
        var (t, s, teacher, cls) = NewFixture();
        teacher.UpdateEmploymentInfo(null, null, null, canTeachMultipleGrades: true,
            maxPeriodsPerDay: 2, maxPeriodsPerWeek: 30);

        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 0, 2),
            NewLesson(t, s, teacher, cls, 0, 3),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        var violation = new TeacherWorkloadBalanceConstraint().Evaluate(
            Context(t, lessons, [teacher], [s], classes: [cls]));

        Assert.NotNull(violation);
        Assert.Equal(3, violation.AffectedLessons.Count);
    }

    [Fact]
    public void TeacherWorkload_IsSilentWhenTeacherListUnknown()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 0, 2),
            NewLesson(t, s, teacher, cls, 0, 3),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        Assert.Null(new TeacherWorkloadBalanceConstraint().Evaluate(
            Context(t, lessons, subjects: [s], classes: [cls])));
    }

    // --------------------------------------------------------------------
    // المدقق: التضاربات صلبة فقط، واللتين تُخصم من الدرجة
    // --------------------------------------------------------------------

    [Fact]
    public void Validator_CountsOnlyHardViolationsAsConflicts_SoftOnesLowerScore()
    {
        var (t, s, teacher, cls) = NewFixture();
        var lessons = new List<Lesson>
        {
            NewLesson(t, s, teacher, cls, 0, 1),
            NewLesson(t, s, teacher, cls, 0, 2),
            NewLesson(t, s, teacher, cls, 0, 3),
            NewLesson(t, s, teacher, cls, 0, 4),
        };
        foreach (var lesson in lessons) t.AddLesson(lesson);

        var context = Context(t, lessons, [teacher], [s], classes: [cls]);
        var result = new AwadTimetable.Solver.ConstraintValidator().Validate(context);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.ConflictsCount);
        Assert.True(result.QualityScore.Value < 100.0, "الانتهاكات اللينة يجب أن تنخفض من الدرجة");
        Assert.NotEmpty(result.Violations);
    }
}

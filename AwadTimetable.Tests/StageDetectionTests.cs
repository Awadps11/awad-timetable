using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// تحديد مرحلة الفصل: أرقام الصفوف تتكرر بين المراحل (1-3)، لذا يجب أن يعتمد التعرف على
/// اسم الفصل («أول ثانوي 1») لا على رقم الصف وحده، وإلا استحال توليد الجدول في المدارس المشتركة.
/// </summary>
public class StageDetectionTests
{
    [Theory]
    [InlineData(1, "أول ثانوي 2", "ثانوي")]
    [InlineData(1, "أول متوسط 1", "متوسط")]
    [InlineData(3, "ثالث ثانوي 1", "ثانوي")]
    [InlineData(1, "أول ابتدائي 1", "ابتدائي")]
    [InlineData(1, null, "ابتدائي")]
    [InlineData(7, null, "متوسط")]
    [InlineData(10, null, "ثانوي")]
    public void GetStage_UsesClassName_ThenGrade(int grade, string? name, string expected)
        => Assert.Equal(expected, StageHelper.GetStage(grade, name));

    [Fact]
    public void GetStageForClass_PrefersClassName_OverAmbiguousGrade()
    {
        var tt = new Timetable(SchoolId.New(), "مجمع", 2026, 2027, 1);
        var classId = Guid.NewGuid();
        tt.RegisterClass(classId, "أول ثانوي 1", gradeLevel: 1);

        Assert.Equal("ثانوي", tt.GetStageForClass(classId));
    }

    [Fact]
    public async Task Solver_PlacesSecondaryClass_WithAmbiguousGrade_WhenOnlySecondaryTimingDefined()
    {
        // المدرسة عرّفت توقيت «ثانوي» فقط، والفصل «أول ثانوي 1» رقمه 1 (يلتبس مع الابتدائي)
        var tt = new Timetable(SchoolId.New(), "ثانوية", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "ثانوي", WorkDays = new List<int> { 0 }, LessonsPerDay = 4 });
        for (byte p = 1; p <= 4; p++)
        {
            tt.PeriodTimes.Add(new PeriodTime
            {
                Period = p,
                Day = 0,
                Stage = "ثانوي",
                StartMinutes = 7 * 60 + (p - 1) * 45,
                EndMinutes = 7 * 60 + (p - 1) * 45 + 45,
            });
        }

        var school = tt.SchoolId;
        var teacher = new Teacher(school, "عوض", "لافي");
        var subject = new Subject(school, "المهارات الرقمية");
        var classRoom = new Class(school, "أول ثانوي 1", 1, 'أ');
        tt.RegisterClass(classRoom.Id, classRoom.Name, classRoom.GradeLevel);

        for (var i = 0; i < 3; i++)
        {
            var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id, (ClassId)classRoom.Id,
                DomainDayOfWeek.Saturday, new PeriodNumber(0));
            lesson.AttachReferences(subject, teacher, classRoom);
            tt.AddLesson(lesson);
        }

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(5), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(3, result.LessonsScheduled);
        Assert.All(tt.Lessons, l => Assert.True(l.Period.Value >= 1));
    }

    [Fact]
    public async Task Solver_FallsBackToConfiguredStage_WhenClassNameAndGradeUnknown()
    {
        // لا اسم فصل ولا رقم صف مُسجّل: يجب ألا يفشل التوليد كلياً — يُستخدم توقيت المرحلة المُعرّفة
        var tt = new Timetable(SchoolId.New(), "ثانوية", 2026, 2027, 1);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "ثانوي", WorkDays = new List<int> { 0 }, LessonsPerDay = 4 });
        for (byte p = 1; p <= 4; p++)
        {
            tt.PeriodTimes.Add(new PeriodTime
            {
                Period = p,
                Day = 0,
                Stage = "ثانوي",
                StartMinutes = 7 * 60 + (p - 1) * 45,
                EndMinutes = 7 * 60 + (p - 1) * 45 + 45,
            });
        }

        var school = tt.SchoolId;
        var teacher = new Teacher(school, "مشترك", "معلم");
        var subject = new Subject(school, "مادة");
        var classRoom = new Class(school, "فصل", 1, 'أ');
        tt.ClassGradeLevels[classRoom.Id] = 1; // ابتدائي بالرقم، ولا توقيت ابتدائي مُعرّف

        var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id, (ClassId)classRoom.Id,
            DomainDayOfWeek.Saturday, new PeriodNumber(0));
        lesson.AttachReferences(subject, teacher, classRoom);
        tt.AddLesson(lesson);

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(5), UseParallel = false });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, result.LessonsScheduled);
    }
}

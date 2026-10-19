using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبار مسار العمل الكامل: إسنادات → حصص غير موضوعة → توليد → جدول بلا تضارب.
/// يحاكي ما يفعله زر «توليد الحصص من الإسنادات» ثم «توليد الجدول».
/// </summary>
public class PipelineIntegrationTests
{
    [Fact]
    public async Task Assignments_To_Lessons_To_GeneratedTimetable()
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "مسار كامل", 2026, 2027, 1)
        {
            PeriodTimes = TimingBuilder.BuildForStages(
                new List<(SchoolType, string, int, int)>
                {
                    (SchoolType.Middle, "متوسط", 5, 45),
                    (SchoolType.Secondary, "ثانوي", 5, 45),
                },
                new List<int> { 0, 1, 2, 3, 4 }),
            StageConfigs = new List<StageScheduleConfig>
            {
                new() { Stage = "متوسط", WorkDays = new List<int> { 0, 1, 2, 3, 4 }, LessonsPerDay = 5 },
                new() { Stage = "ثانوي", WorkDays = new List<int> { 0, 1, 2, 3, 4 }, LessonsPerDay = 5 },
            },
        };

        var teachers = Enumerable.Range(1, 4).Select(i => new Teacher(school, "معلم", i.ToString())).ToList();
        var subjects = Enumerable.Range(1, 4).Select(i => new Subject(school, "مادة " + i)).ToList();
        var classes = new List<Class>
        {
            new(school, "أول متوسط 1", 7, 'أ'),
            new(school, "ثاني متوسط 1", 8, 'أ'),
            new(school, "أول ثانوي 1", 10, 'أ'),
        };
        timetable.ClassGradeLevels[classes[0].Id] = 7;
        timetable.ClassGradeLevels[classes[1].Id] = 8;
        timetable.ClassGradeLevels[classes[2].Id] = 10;

        // إنشاء الإسنادات: كل فصل × 4 مواد، 3 حصص أسبوعياً، المعلم بالتناوب
        var assignments = new List<TeachingAssignment>();
        var ti = 0;
        foreach (var cls in classes)
        {
            for (var s = 0; s < subjects.Count; s++)
            {
                assignments.Add(new TeachingAssignment
                {
                    TeacherId = (Guid)teachers[ti++ % teachers.Count].Id,
                    SubjectId = (Guid)subjects[s].Id,
                    ClassId = (Guid)cls.Id,
                    WeeklyPeriods = 3,
                });
            }
        }

        // محاكاة «توليد الحصص من الإسنادات»: إنشاء حصص غير موضوعة (Period = 0)
        var expectedLessons = 0;
        foreach (var a in assignments)
        {
            var teacher = teachers.First(t => (Guid)t.Id == a.TeacherId);
            var subject = subjects.First(s => (Guid)s.Id == a.SubjectId);
            var cls = classes.First(c => (Guid)c.Id == a.ClassId);

            for (var i = 0; i < a.WeeklyPeriods; i++)
            {
                var lesson = new Lesson((TimetableId)timetable.Id, (SubjectId)a.SubjectId, (TeacherId)a.TeacherId,
                    (ClassId)a.ClassId, DomainDayOfWeek.Saturday, new PeriodNumber(0));
                lesson.AttachReferences(subject, teacher, cls);
                timetable.AddLesson(lesson);
                expectedLessons++;
            }
        }

        Assert.Equal(expectedLessons, timetable.Lessons.Count);
        Assert.All(timetable.Lessons, l => Assert.True(l.Period.Value < 1));

        // التوليد
        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(timetable, new GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(10),
            UseParallel = true,
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(0, result.ConflictsCount);
        Assert.Equal(expectedLessons, result.LessonsScheduled);

        // كل الحصص وُضعت في فترات صحيحة
        Assert.All(timetable.Lessons, l => Assert.True(l.Period.Value >= 1));

        // لا تضارب معلم/فصل/قاعة
        var validator = new ConstraintValidator();
        var validation = validator.Validate(TimetableContextFactory.Build(timetable));
        Assert.Equal(0, validation.ConflictsCount);
        Assert.True(validation.QualityScore.Value > 0);
    }
}

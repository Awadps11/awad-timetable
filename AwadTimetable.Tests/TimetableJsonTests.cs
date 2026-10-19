using System.Text.Json;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبار نسخ الجدول عبر JSON (يُستخدم في Undo/Redo والنسخ/اللصق).
/// يمنع كسر التسلسل عند إضافة مُنشئين لكيان Lesson.
/// </summary>
public class TimetableJsonTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    [Fact]
    public void Timetable_RoundTripsThroughJson_WithLessons()
    {
        var original = SampleTimetable.Create(classCount: 2, subjectCount: 2);
        original.Assignments.Add(new TeachingAssignment
        {
            TeacherId = (Guid)original.Lessons[0].TeacherId,
            SubjectId = (Guid)original.Lessons[0].SubjectId,
            ClassId = (Guid)original.Lessons[0].ClassId,
            WeeklyPeriods = 2,
        });

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<Timetable>(json, Options);

        Assert.NotNull(restored);
        Assert.Equal(original.Lessons.Count, restored!.Lessons.Count);
        Assert.Equal(original.Assignments.Count, restored.Assignments.Count);
        Assert.Equal(original.Lessons[0].Id, restored.Lessons[0].Id);
        Assert.Equal(original.Lessons[0].Period.Value, restored.Lessons[0].Period.Value);
        Assert.Equal((Guid)original.Lessons[0].TeacherId, (Guid)restored.Lessons[0].TeacherId);
        Assert.Equal((Guid)original.Lessons[0].ClassId, (Guid)restored.Lessons[0].ClassId);
    }

    [Fact]
    public void Lesson_WithExplicitId_RoundTrips()
    {
        var id = Guid.NewGuid();
        var lesson = new Lesson(id, TimetableId.New(), SubjectId.New(), TeacherId.New(), ClassId.New(),
            AwadTimetable.Core.Enums.DayOfWeek.Sunday, PeriodNumber.Create(3));

        var json = JsonSerializer.Serialize(lesson, Options);
        var restored = JsonSerializer.Deserialize<Lesson>(json, Options);

        Assert.NotNull(restored);
        Assert.Equal(id, restored!.Id);
        Assert.Equal(3, restored.Period.Value);
    }
}

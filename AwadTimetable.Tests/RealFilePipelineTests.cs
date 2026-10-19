using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبار كامل على ملف aSc الحقيقي: استيراد البيانات → بناء الجدول → توليد → تحقق.
/// </summary>
public class RealFilePipelineTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "asc-full-2024.xml");

    [Fact]
    public async Task RealAscFile_ImportsAndGeneratesValidTimetable()
    {
        var xml = AscXmlReader.ReadXmlText(FixturePath);
        var rows = VendorXmlImporter.Parse(xml, VendorXmlImporter.Untis);

        Assert.True(rows.Count >= 100, $"الحصص: {rows.Count}");

        var school = SchoolId.New();
        var tt = new Timetable(school, "ملف حقيقي", 2026, 2027, 1);

        // أيام الملف: الأحد..الخميس (1..5)، 7 فترات
        var workDays = new List<int> { 1, 2, 3, 4, 5 };
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "متوسط", WorkDays = workDays, LessonsPerDay = 7 });
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "ثانوي", WorkDays = workDays, LessonsPerDay = 7 });

        for (var p = 1; p <= 7; p++)
        {
            tt.PeriodTimes.Add(new PeriodTime
            {
                Period = (byte)p,
                Day = -1,
                Stage = null,
                StartMinutes = 7 * 60 + (p - 1) * 45,
                EndMinutes = 7 * 60 + (p - 1) * 45 + 45,
            });
        }

        var teachers = new Dictionary<string, Teacher>(StringComparer.OrdinalIgnoreCase);
        var classes = new Dictionary<string, Class>(StringComparer.OrdinalIgnoreCase);
        var subjects = new Dictionary<string, Subject>(StringComparer.OrdinalIgnoreCase);
        var rooms = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);

        int gradeOf(string className)
            => className.Contains("ثانوي", StringComparison.Ordinal) ? 10
             : className.Contains("متوسط", StringComparison.Ordinal) ? 7
             : className.Contains("انتظار", StringComparison.Ordinal) ? 1
             : 1;

        foreach (var row in rows)
        {
            if (!teachers.TryGetValue(row.TeacherName, out var teacher))
            {
                var parts = row.TeacherName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var first = parts.Length > 0 ? parts[0] : row.TeacherName;
                var last = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : first;
                teacher = new Teacher(school, first, last);
                teachers[row.TeacherName] = teacher;
            }

            if (!subjects.TryGetValue(row.SubjectName, out var subject))
            {
                subject = new Subject(school, row.SubjectName);
                subjects[row.SubjectName] = subject;
            }

            if (!classes.TryGetValue(row.ClassName, out var cls))
            {
                var grade = gradeOf(row.ClassName);
                cls = new Class(school, row.ClassName, grade, 'أ');
                classes[row.ClassName] = cls;
                tt.ClassGradeLevels[cls.Id] = grade;
            }

            Room? room = null;
            if (!string.IsNullOrWhiteSpace(row.RoomName))
            {
                if (!rooms.TryGetValue(row.RoomName, out room))
                {
                    room = new Room(school, row.RoomName);
                    rooms[row.RoomName] = room;
                }
            }

            var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id,
                (ClassId)cls.Id, row.Day, PeriodNumber.Create((byte)row.Period));
            if (room is not null) lesson.AssignRoom((RoomId)room.Id);
            lesson.AttachReferences(subject, teacher, cls, room);
            tt.AddLesson(lesson);
        }

        Assert.True(tt.Lessons.Count >= 100, $"دروس الجدول: {tt.Lessons.Count}");

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var result = await solver.GenerateAsync(tt, new GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(25),
            UseParallel = true,
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(0, result.ConflictsCount);
        Assert.All(tt.Lessons, l => Assert.True(l.Period.Value >= 1));

        var validation = new ConstraintValidator().Validate(TimetableContextFactory.Build(tt));
        Assert.Equal(0, validation.ConflictsCount);
    }
}

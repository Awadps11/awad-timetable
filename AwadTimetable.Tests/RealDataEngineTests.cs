using System.Diagnostics;
using System.Xml.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Solver;
using Microsoft.Extensions.Logging.Abstractions;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// اختبار على بيانات aSc الحقيقية (ملف 168KB: 17 فصلاً، 38 معلماً، 30+ مادة):
/// 1) إعادة بناء الإسنادات من الحصص الحقيقية وتوليد جدول كامل من الصفر.
/// 2) تصدير الجدول إلى صيغة aSc ثم قراءته مرة أخرى (aSc engine استعمالاً حقيقياً).
/// 3) تصدير صيغة FET والتحقق منها (وتشغيل fet-cl إن كان متاحاً).
/// </summary>
public class RealDataEngineTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "asc-full-2024.xml");

    private sealed class RealSchool
    {
        public required Timetable Timetable { get; init; }
        public required List<Teacher> Teachers { get; init; }
        public required List<Subject> Subjects { get; init; }
        public required List<Class> Classes { get; init; }
        public required List<Room> Rooms { get; init; }
        public required List<int> WorkDays { get; init; }
        public required int MaxPeriod { get; init; }
        public required int Placements { get; init; }
    }

    private static RealSchool BuildFromRealFile()
    {
        var xml = AscXmlReader.ReadXmlText(FixturePath);
        var rows = VendorXmlImporter.Parse(xml, VendorXmlImporter.Untis);
        Assert.True(rows.Count >= 100, $"الحصص الحقيقية: {rows.Count}");

        var school = SchoolId.New();
        var tt = new Timetable(school, "بيانات aSc حقيقية", 2026, 2027, 1);

        var teachers = new Dictionary<string, Teacher>(StringComparer.OrdinalIgnoreCase);
        var subjects = new Dictionary<string, Subject>(StringComparer.OrdinalIgnoreCase);
        var classes = new Dictionary<string, Class>(StringComparer.OrdinalIgnoreCase);
        var rooms = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);

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
                cls = new Class(school, row.ClassName, StageHelper.ParseGrade(row.ClassName), StageHelper.ParseSection(row.ClassName));
                classes[row.ClassName] = cls;
                tt.RegisterClass((Guid)cls.Id, cls.Name, cls.GradeLevel);
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
        }

        var workDays = rows.Select(r => (int)r.Day).Distinct().OrderBy(d => d).ToList();
        var maxPeriod = rows.Max(r => r.Period);

        foreach (var stage in classes.Values.Select(c => StageHelper.GetStage(c.GradeLevel, c.Name)).Distinct())
        {
            tt.StageConfigs.Add(new StageScheduleConfig
            {
                Stage = stage,
                WorkDays = workDays,
                LessonsPerDay = maxPeriod,
            });
        }

        for (var p = 1; p <= maxPeriod; p++)
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

        foreach (var row in rows)
        {
            var teacher = teachers[row.TeacherName];
            var subject = subjects[row.SubjectName];
            var cls = classes[row.ClassName];
            var room = string.IsNullOrWhiteSpace(row.RoomName) ? null : rooms[row.RoomName];

            var lesson = new Lesson((TimetableId)tt.Id, (SubjectId)subject.Id, (TeacherId)teacher.Id,
                (ClassId)cls.Id, row.Day, PeriodNumber.Create((byte)row.Period));
            if (room is not null) lesson.AssignRoom((RoomId)room.Id);
            lesson.AttachReferences(subject, teacher, cls, room);
            tt.AddLesson(lesson);
        }

        return new RealSchool
        {
            Timetable = tt,
            Teachers = teachers.Values.ToList(),
            Subjects = subjects.Values.ToList(),
            Classes = classes.Values.ToList(),
            Rooms = rooms.Values.ToList(),
            WorkDays = workDays,
            MaxPeriod = maxPeriod,
            Placements = rows.Count,
        };
    }

    [Fact]
    public async Task RealData_DeriveAssignments_AndGenerateFromScratch()
    {
        var school = BuildFromRealFile();
        var tt = school.Timetable;

        // اشتقّ الإسنادات من الحصص الحقيقية
        var assignments = tt.Lessons
            .GroupBy(l => ((Guid)l.TeacherId, (Guid)l.SubjectId, (Guid)l.ClassId))
            .Select(g => new TeachingAssignment
            {
                TeacherId = g.Key.Item1,
                SubjectId = g.Key.Item2,
                ClassId = g.Key.Item3,
                WeeklyPeriods = g.Count(),
            })
            .ToList();

        tt.Assignments.Clear();
        tt.Assignments.AddRange(assignments);

        var originalLessons = tt.Lessons.Count;
        tt.Lessons.Clear();

        var sync = TeachingAssignmentLessonSync.Sync(
            tt,
            id => school.Teachers.FirstOrDefault(t => (Guid)t.Id == id),
            id => school.Subjects.FirstOrDefault(s => (Guid)s.Id == id),
            id => school.Classes.FirstOrDefault(c => (Guid)c.Id == id));

        Assert.Equal(originalLessons, sync.Created);

        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var watch = Stopwatch.StartNew();
        var result = await solver.GenerateAsync(tt, new GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(60),
            UseParallel = true,
            AllowPartialSolution = false,
            RandomSeed = 9911,
        });
        watch.Stop();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(0, result.LessonsFailed);
        Assert.Equal(0, result.ConflictsCount);
        Assert.Equal(originalLessons, tt.Lessons.Count);
        Assert.All(tt.Lessons, l => Assert.InRange(l.Period.Value, 1, school.MaxPeriod));
        Assert.All(tt.Lessons, l => Assert.Contains((int)l.Day, school.WorkDays));
        Assert.Empty(new ConflictDetector().DetectConflicts(tt));

        Console.WriteLine(
            $"[real-asc] classes={school.Classes.Count} teachers={school.Teachers.Count} subjects={school.Subjects.Count} " +
            $"placements={school.Placements} assignments={assignments.Count} lessons={originalLessons} " +
            $"durationMs={watch.ElapsedMilliseconds} solutions={result.Iterations}");
    }

    [Fact]
    public async Task RealData_AscEngine_ExportsAndReimports()
    {
        var school = BuildFromRealFile();
        var folder = Path.Combine(Path.GetTempPath(), "awad-real-asc-" + Guid.NewGuid().ToString("N"));

        try
        {
            var engine = new AscEngine(NullLogger<AscEngine>.Instance, new ConstraintValidator(),
                ascExePath: string.Empty, exportFolder: folder);

            var result = await engine.GenerateAsync(school.Timetable, new GenerationOptions());
            Assert.False(result.Success); // aSc لا يولّد من سطر الأوامر — تصدير فقط
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));

            var file = Directory.GetFiles(folder, "*.xml").Single();
            Assert.True(new FileInfo(file).Length > 0);

            var parsed = AscXmlReader.ParseFile(file);

            Assert.True(parsed.Classes.Count >= school.Classes.Count, $"فصول مصدّرة: {parsed.Classes.Count}");
            Assert.True(parsed.Teachers.Count >= school.Teachers.Count, $"معلمون مصدّرون: {parsed.Teachers.Count}");
            Assert.True(parsed.Subjects.Count >= school.Subjects.Count, $"مواد مصدّرة: {parsed.Subjects.Count}");
            Assert.True(parsed.Cards.Count >= school.Timetable.Lessons.Count, $"بطاقات: {parsed.Cards.Count}");
            Assert.All(parsed.Cards, c => Assert.InRange(c.Period0, 0, school.MaxPeriod));

            Console.WriteLine($"[asc-engine] exported classes={parsed.Classes.Count} teachers={parsed.Teachers.Count} " +
                              $"subjects={parsed.Subjects.Count} cards={parsed.Cards.Count}");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void RealData_FetExporter_BuildsValidActivities()
    {
        var school = BuildFromRealFile();

        var doc = FetExporter.Build(school.Timetable);
        var activities = doc.Descendants("Activity").ToList();

        Assert.True(activities.Count >= school.Timetable.Lessons.Count,
            $"أنشطة FET: {activities.Count} مقابل {school.Timetable.Lessons.Count}");
        Assert.NotEmpty(doc.Descendants("Teacher").ToList());
        Assert.NotEmpty(doc.Descendants("Subject").ToList());
        Assert.NotEmpty(doc.Descendants("Students").ToList());

        // ملف aSc الحقيقي لا يحوي قاعات؛ نتحقق فقط إن كانت المصدر يحتوي قاعات
        if (school.Rooms.Count > 0)
            Assert.NotEmpty(doc.Descendants("Room").ToList());

        var path = Path.Combine(Path.GetTempPath(), "awad-real-" + Guid.NewGuid().ToString("N") + ".fet");
        try
        {
            doc.Save(path);
            Assert.True(new FileInfo(path).Length > 0);
            var reloaded = XDocument.Load(path);
            Assert.Equal(activities.Count, reloaded.Descendants("Activity").Count());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        Console.WriteLine($"[fet-export] activities={activities.Count} rooms={doc.Descendants("Room").Count()}");
    }

    [Fact]
    public void FetExport_IncludesRooms_AndPreferredRoomConstraints()
    {
        var school = SchoolId.New();
        var tt = SampleTimetable.Create(classCount: 2, subjectCount: 2);
        tt.PeriodTimes = SampleTimetable.GeneralPeriodTimes(1, 7);
        tt.StageConfigs.Add(new StageScheduleConfig { Stage = "ابتدائي", WorkDays = new List<int> { 0, 1, 2, 3, 4 }, LessonsPerDay = 7 });

        var room = new Room(school, "قاعة 1", "ق1", 30);
        var lessonsWithRoom = 0;
        foreach (var lesson in tt.Lessons.Where((_, i) => i % 2 == 0))
        {
            lesson.AssignRoom((RoomId)room.Id);
            lesson.AttachReferences(lesson.Subject, lesson.Teacher, lesson.Class, room);
            lessonsWithRoom++;
        }

        var doc = FetExporter.Build(tt);

        Assert.Single(doc.Descendants("Rooms_List").Elements("Room"));
        Assert.Equal(lessonsWithRoom, doc.Descendants("ConstraintActivityPreferredRoom").Count());
        Assert.Contains(doc.Descendants("Room"), r => r.Value == "قاعة 1");
    }

    [Fact]
    public async Task RealData_FetEngine_RunsWhenFetCliIsAvailable()
    {
        var fetPath = FindFetCli();
        if (fetPath is null)
        {
            // لا يوجد fet-cl على الجهاز: نتأكد أن المحرك يفشل برسالة واضحة (لا انهيار)
            var engine = new FetEngine(NullLogger<FetEngine>.Instance, new ConstraintValidator(),
                Path.Combine(Path.GetTempPath(), "missing-fet-cl.exe"));
            var school = BuildFromRealFile();
            var result = await engine.GenerateAsync(school.Timetable, new GenerationOptions());
            Assert.False(result.Success);
            Assert.Contains("fet-cl", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            return;
        }

        var real = BuildFromRealFile();
        var solver = new CpSatTimetableSolver(NullLogger<CpSatTimetableSolver>.Instance, new ConstraintValidator());
        var generated = await solver.GenerateAsync(real.Timetable, new GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(30),
            UseParallel = true,
        });
        Assert.True(generated.Success, generated.ErrorMessage);

        var fet = new FetEngine(NullLogger<FetEngine>.Instance, new ConstraintValidator(), fetPath);
        var outcome = await fet.GenerateAsync(real.Timetable, new GenerationOptions
        {
            TimeLimit = TimeSpan.FromSeconds(60),
            UseParallel = true,
        });

        Assert.True(outcome.Success, outcome.ErrorMessage);
        Assert.Equal(0, outcome.ConflictsCount);
        Console.WriteLine($"[fet-real] path={fetPath} scheduled={outcome.LessonsScheduled} conflicts={outcome.ConflictsCount}");
    }

    private static string? FindFetCli()
    {
        var fromEnv = Environment.GetEnvironmentVariable("FET_CL_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;

        foreach (var candidate in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FET", "fet-cl.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FET", "fet-cl.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FET", "fet-cl.exe"),
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}

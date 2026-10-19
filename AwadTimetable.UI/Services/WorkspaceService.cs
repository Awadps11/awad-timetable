using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Data;

namespace AwadTimetable.UI.Services;

/// <summary>ملخّص مساحة عمل/مدرسة لعرضها وإدارتها.</summary>
public sealed record WorkspaceInfo(
    Guid Id,
    string Name,
    int Teachers,
    int Subjects,
    int Classes,
    int Rooms,
    int Timetables,
    DateTime? UpdatedAt)
{
    public string Summary => $"معلمون {Teachers} • مواد {Subjects} • فصول {Classes} • قاعات {Rooms} • جداول {Timetables}";
}

/// <summary>
/// إدارة مساحات العمل/المدارس: عرض، إعادة تسمية، حذف، ونسخ بياناتها.
/// </summary>
public sealed class WorkspaceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSchoolService _school;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(IServiceScopeFactory scopeFactory, CurrentSchoolService school, ILogger<WorkspaceService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _school = school ?? throw new ArgumentNullException(nameof(school));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<WorkspaceInfo>> ListAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimetableDbContext>();

        var schools = await db.Schools.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        var result = new List<WorkspaceInfo>();

        foreach (var school in schools)
        {
            var sid = new SchoolId(school.Id);
            result.Add(new WorkspaceInfo(
                school.Id,
                school.Name,
                await db.Teachers.CountAsync(t => t.SchoolId == sid),
                await db.Subjects.CountAsync(t => t.SchoolId == sid),
                await db.Classes.CountAsync(t => t.SchoolId == sid),
                await db.Rooms.CountAsync(t => t.SchoolId == sid),
                await db.Timetables.CountAsync(t => t.SchoolId == sid),
                school.UpdatedAt ?? school.CreatedAt));
        }

        return result;
    }

    public async Task<bool> RenameAsync(Guid id, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimetableDbContext>();

        var school = await db.Schools.FirstOrDefaultAsync(s => s.Id == id);
        if (school is null) return false;

        school.UpdateBasicInfo(newName.Trim(), school.ShortName, school.Address, school.Phone, school.Email, school.PrincipalName);
        await db.SaveChangesAsync();
        _logger.LogInformation("إعادة تسمية مساحة العمل {Id} إلى {Name}", id, newName);
        return true;
    }

    /// <summary>حذف مساحة العمل بالكامل: جداولها وحصصها وإعداداتها وبياناتها المرجعية ومدرستها.</summary>
    public async Task<bool> DeleteAsync(Guid id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimetableDbContext>();

        var school = await db.Schools.FirstOrDefaultAsync(s => s.Id == id);
        if (school is null) return false;

        var schoolId = new SchoolId(id);

        // جداول المساحة (تُحذف الحصص تبعاً)
        var timetables = await db.Timetables.Where(t => t.SchoolId == schoolId).ToListAsync();
        foreach (var t in timetables)
            DeleteSettings(db, t.Id);

        db.Timetables.RemoveRange(timetables);
        await db.SaveChangesAsync();

        // المراجع: توفر معلميها، ربط المادة/المعلم، الفصول، المواد، القاعات، المعلمون
        var companyKeys = await db.Teachers.Where(t => t.SchoolId == schoolId).Select(t => t.Id).ToListAsync();
        var keySet = companyKeys.ToHashSet();
        var availabilities = (await db.TeacherAvailabilities.ToListAsync())
            .Where(a => keySet.Contains(a.TeacherId.Value)).ToList();
        if (availabilities.Count > 0)
            db.TeacherAvailabilities.RemoveRange(availabilities);

        db.Classes.RemoveRange(await db.Classes.Where(c => c.SchoolId == schoolId).ToListAsync());
        db.Subjects.RemoveRange(await db.Subjects.Where(s => s.SchoolId == schoolId).ToListAsync());
        db.Rooms.RemoveRange(await db.Rooms.Where(r => r.SchoolId == schoolId).ToListAsync());
        db.Teachers.RemoveRange(await db.Teachers.Where(t => t.SchoolId == schoolId).ToListAsync());
        db.Schools.Remove(school);
        await db.SaveChangesAsync();

        // إن كانت المساحة المحذوفة هي الحالية: امسح الكاش حتى لا تُكتب بيانات تحتها
        if (_school.Current is { } current && current.Value == id)
            _school.ClearCurrent();

        _logger.LogWarning("تم حذف مساحة العمل {Id} ({Name})", id, school.Name);
        return true;
    }

    /// <summary>نسخ بيانات مساحة عمل (المعلمون/المواد/الفصول/القاعات) إلى مساحة عمل جديدة.</summary>
    public async Task<Guid?> DuplicateAsync(Guid id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimetableDbContext>();

        var source = await db.Schools.FirstOrDefaultAsync(s => s.Id == id);
        if (source is null) return null;

        var sid = new SchoolId(id);
        var newSchool = new School($"{source.Name} (نسخة)", source.ShortName);
        db.Schools.Add(newSchool);
        await db.SaveChangesAsync();

        var nsid = (SchoolId)newSchool.Id;

        // المعلمون أولاً لبناء خريطة معرّفات تُستخدم في إعادة ربط القاعات والفصول
        var teacherMap = new Dictionary<Guid, Guid>();
        foreach (var teacher in await db.Teachers.Where(t => t.SchoolId == sid).ToListAsync())
        {
            var copy = new Teacher(nsid, teacher.FirstName, teacher.LastName, teacher.MiddleName);
            copy.UpdatePersonalInfo(teacher.FirstName, teacher.LastName, teacher.MiddleName, teacher.Email, teacher.Phone, teacher.Address, teacher.DateOfBirth, teacher.NationalId);
            copy.UpdateEmploymentInfo(teacher.EmployeeNumber, teacher.HireDate, teacher.Specialization, teacher.CanTeachMultipleGrades, teacher.MaxPeriodsPerDay, teacher.MaxPeriodsPerWeek);
            copy.SetAppearance(teacher.CustomShortName, teacher.CustomColorHex);
            db.Teachers.Add(copy);
            teacherMap[(Guid)teacher.Id] = (Guid)copy.Id;
        }

        var roomMap = new Dictionary<Guid, Guid>();
        foreach (var room in await db.Rooms.Where(r => r.SchoolId == sid).ToListAsync())
        {
            var copy = new Room(nsid, room.Name, room.ShortName, room.Capacity);
            copy.UpdateBasicInfo(room.Name, room.ShortName, room.Code, room.Description, room.Capacity);
            copy.UpdateEquipment(room.HasProjector, room.HasSmartBoard, room.HasComputers);
            copy.UpdateType(room.IsLab, room.IsGym, room.IsLibrary, room.IsMusicRoom, room.IsArtRoom);
            copy.UpdateLocation(room.Floor, room.Building);
            copy.SetShared(room.IsShared);
            if (room.AssignedTeacherId is { } oldTeacher && teacherMap.TryGetValue(oldTeacher, out var newTeacher))
                copy.SetTeacherAssignment(newTeacher);
            db.Rooms.Add(copy);
            roomMap[(Guid)room.Id] = (Guid)copy.Id;
        }

        foreach (var subject in await db.Subjects.Where(s => s.SchoolId == sid).ToListAsync())
        {
            var copy = new Subject(nsid, subject.Name, subject.ShortName);
            copy.UpdateBasicInfo(subject.Name, subject.ShortName, subject.Description, subject.GradeLevel);
            copy.UpdateSchedule(subject.DefaultPeriodsPerWeek, subject.MinPeriodsPerWeek, subject.MaxPeriodsPerWeek);
            copy.UpdateRequirements(subject.RequiresLab, subject.RequiresSpecialRoom, subject.MinStudentsPerClass, subject.MaxStudentsPerClass);
            db.Subjects.Add(copy);
        }

        foreach (var cls in await db.Classes.Where(c => c.SchoolId == sid).ToListAsync())
        {
            var copy = new Class(nsid, cls.Name, cls.GradeLevel, cls.Section);
            copy.UpdateBasicInfo(cls.Name, cls.ShortName, cls.Code, cls.StudentCount);

            if (cls.DefaultRoomId is { } oldRoom && roomMap.TryGetValue(oldRoom.Value, out var newRoom))
                copy.SetDefaultRoom((RoomId)newRoom);
            if (cls.HomeroomTeacherId is { } oldHomeroom && teacherMap.TryGetValue(oldHomeroom.Value, out var newHomeroom))
                copy.SetHomeroomTeacher((TeacherId)newHomeroom);

            db.Classes.Add(copy);
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("نُسخت مساحة العمل {Id} إلى {NewId}", id, newSchool.Id);
        return newSchool.Id;
    }

    private static void DeleteSettings(TimetableDbContext db, Guid timetableId)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM TimetableSettings WHERE upper(TimetableId) = upper($id)";
            var p = cmd.CreateParameter(); p.ParameterName = "$id"; p.Value = timetableId.ToString();
            cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // تنظيف اختياري
        }
    }
}

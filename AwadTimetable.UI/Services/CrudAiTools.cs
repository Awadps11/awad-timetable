using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using AwadTimetable.AI.Interfaces;
using AwadTimetable.AI.Tools;
using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Data.Repositories;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.UI.Services;

/// <summary>مساعدات مشتركة لأدوات الإدارة (CRUD) في المساعد الذكي.</summary>
internal static class CrudSupport
{
    public static bool TryGuid(string? s, out Guid g)
    {
        g = Guid.Empty;
        return !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out g) && g != Guid.Empty;
    }

    /// <summary>مساحة عمل/مدرسة الجدول الحالي — كل عمليات القراءة والكتابة تبقى داخلها.</summary>
    public static Task<SchoolId> GetSchoolIdAsync(IServiceProvider sp)
        => sp.GetRequiredService<CurrentSchoolService>().GetSchoolIdAsync();

    public static (string First, string Last, string? Middle) NameParts(
        string? firstName, string? lastName, string? middle, string? fullName)
    {
        firstName = firstName?.Trim();
        lastName = lastName?.Trim();
        if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
            return (firstName!, lastName!, string.IsNullOrWhiteSpace(middle) ? null : middle!.Trim());

        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (firstName ?? string.Empty, lastName ?? string.Empty, null);
        if (parts.Length == 1) return (parts[0], lastName ?? string.Empty, null);
        var mid = parts.Length > 2 ? string.Join(' ', parts[1..^1]) : null;
        return (parts[0], parts[^1], string.IsNullOrWhiteSpace(middle) ? mid : middle!.Trim());
    }

    public static async Task<Teacher?> FindTeacherAsync(IServiceProvider sp, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var repo = sp.GetRequiredService<ITeacherRepository>();
        var school = await GetSchoolIdAsync(sp);
        if (TryGuid(key, out var g)) { var e = await repo.GetByIdAsync(g, ct); if (e != null && e.SchoolId == school) return e; }
        return (await repo.GetBySchoolAsync(school, ct))
            .FirstOrDefault(x => AssistantHelpers.Matches(key, x.FullName, x.DisplayShortName, x.FirstName, x.LastName));
    }

    public static async Task<Subject?> FindSubjectAsync(IServiceProvider sp, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var repo = sp.GetRequiredService<ISubjectRepository>();
        var school = await GetSchoolIdAsync(sp);
        if (TryGuid(key, out var g)) { var e = await repo.GetByIdAsync(g, ct); if (e != null && e.SchoolId == school) return e; }
        return (await repo.GetBySchoolAsync(school, ct))
            .FirstOrDefault(x => AssistantHelpers.Matches(key, x.Name, x.ShortName));
    }

    public static async Task<Class?> FindClassAsync(IServiceProvider sp, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var repo = sp.GetRequiredService<IClassRepository>();
        var school = await GetSchoolIdAsync(sp);
        if (TryGuid(key, out var g)) { var e = await repo.GetByIdAsync(g, ct); if (e != null && e.SchoolId == school) return e; }
        return (await repo.GetBySchoolAsync(school, ct))
            .FirstOrDefault(x => AssistantHelpers.Matches(key, x.Name, x.ShortName, x.FullName));
    }

    public static async Task<Room?> FindRoomAsync(IServiceProvider sp, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var repo = sp.GetRequiredService<IRoomRepository>();
        var school = await GetSchoolIdAsync(sp);
        if (TryGuid(key, out var g)) { var e = await repo.GetByIdAsync(g, ct); if (e != null && e.SchoolId == school) return e; }
        return (await repo.GetBySchoolAsync(school, ct))
            .FirstOrDefault(x => AssistantHelpers.Matches(key, x.Name, x.ShortName, x.Code));
    }

    public static string ListLines(IEnumerable<string> items, int limit = 200)
    {
        var list = items.ToList();
        if (list.Count == 0) return "لا يوجد أي عنصر.";
        var sb = new StringBuilder();
        foreach (var i in list.Take(limit)) sb.AppendLine("• " + i);
        if (list.Count > limit) sb.AppendLine($"… و{list.Count - limit} عنصرًا آخر.");
        return sb.ToString().TrimEnd();
    }
}

// =======================================================================
//  المعلمون
// =======================================================================
public sealed class ManageTeachersAiTool : BaseTool
{
    private readonly AssistantToolContext _context;
    public override string Name => "manage_teachers";
    public override string Description =>
        "إدارة المعلمين: عرض القائمة، إضافة معلم، تعديل بياناته، أو حذفه. استخدمها لأي طلب يخص المعلمين (مثال: «أضف معلمًا اسمه…»).";
    public override JsonSchema Schema { get; }

    public ManageTeachersAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "العملية", Enum = new[] { "list", "create", "update", "delete" } },
                ["target"] = new JsonSchemaProperty { Type = "string", Description = "المعلم المستهدف (اسمه أو معرّفه) — مطلوب في update/delete" },
                ["name"] = new JsonSchemaProperty { Type = "string", Description = "الاسم الكامل (بديل عن firstName+lastName)" },
                ["firstName"] = new JsonSchemaProperty { Type = "string", Description = "الاسم الأول" },
                ["lastName"] = new JsonSchemaProperty { Type = "string", Description = "اسم العائلة" },
                ["middleName"] = new JsonSchemaProperty { Type = "string", Description = "اسم الأب (اختياري)" },
                ["specialization"] = new JsonSchemaProperty { Type = "string", Description = "التخصص" },
                ["employeeNumber"] = new JsonSchemaProperty { Type = "string", Description = "الرقم الوظيفي" },
                ["email"] = new JsonSchemaProperty { Type = "string", Description = "البريد الإلكتروني" },
                ["phone"] = new JsonSchemaProperty { Type = "string", Description = "الهاتف" },
                ["maxPeriodsPerDay"] = new JsonSchemaProperty { Type = "integer", Description = "أقصى حصص يوميًا" },
                ["maxPeriodsPerWeek"] = new JsonSchemaProperty { Type = "integer", Description = "أقصى حصص أسبوعيًا" },
                ["canTeachMultipleGrades"] = new JsonSchemaProperty { Type = "boolean", Description = "يستطيع تدريس أكثر من صف" }
            },
            Required = new[] { "action" }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var a = ParseParameters<Params>(parameters);
        var action = (a.Action ?? "list").Trim().ToLowerInvariant();

        using var scope = _context.ScopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<ITeacherRepository>();

        switch (action)
        {
            case "list":
            {
                var school = await CrudSupport.GetSchoolIdAsync(sp);
                var all = await repo.GetBySchoolAsync(school, cancellationToken);
                var lines = all.OrderBy(t => t.LastName).ThenBy(t => t.FirstName)
                    .Select(t => $"{t.FullName}" + (string.IsNullOrWhiteSpace(t.Specialization) ? "" : $" — {t.Specialization}"));
                return ToolResult.Ok($"المعلمون ({all.Count}):\n" + CrudSupport.ListLines(lines));
            }
            case "create":
            {
                var (first, last, mid) = CrudSupport.NameParts(a.FirstName, a.LastName, a.MiddleName, a.Name);
                if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
                    return ToolResult.Fail("حدّد اسم المعلم (الاسم الأول واسم العائلة).");

                var school = await _context.School.GetSchoolIdAsync();
                var teacher = new Teacher(school, first, last, mid);
                ApplyEmployment(teacher, a, isNew: true);
                ApplyContact(teacher, a);
                await repo.AddAsync(teacher, cancellationToken);
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تمت إضافة المعلم «{teacher.FullName}».");
            }
            case "update":
            {
                var teacher = await CrudSupport.FindTeacherAsync(sp, a.Target ?? a.Name, cancellationToken);
                if (teacher == null) return ToolResult.Fail($"لم يتم العثور على المعلم «{a.Target ?? a.Name}».");

                var (first, last, mid) = CrudSupport.NameParts(
                    a.FirstName ?? teacher.FirstName, a.LastName ?? teacher.LastName,
                    a.MiddleName ?? teacher.MiddleName, null);
                teacher.UpdatePersonalInfo(first, last, mid,
                    a.Email ?? teacher.Email, a.Phone ?? teacher.Phone,
                    teacher.Address, teacher.DateOfBirth, teacher.NationalId);
                ApplyEmployment(teacher, a, isNew: false);
                await repo.UpdateAsync(teacher, cancellationToken);
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تم تحديث بيانات المعلم «{teacher.FullName}».");
            }
            case "delete":
            {
                var teacher = await CrudSupport.FindTeacherAsync(sp, a.Target ?? a.Name, cancellationToken);
                if (teacher == null) return ToolResult.Fail($"لم يتم العثور على المعلم «{a.Target ?? a.Name}».");

                if (!_context.Confirm("تأكيد حذف معلم", $"هل تريد حذف المعلم «{teacher.FullName}»؟"))
                    return ToolResult.Ok("تم إلغاء الحذف بناءً على طلب المستخدم.");

                try
                {
                    await repo.DeleteAsync(teacher.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    return ToolResult.Fail($"تعذّر حذف المعلم (قد يكون مرتبطًا بحصص): {ex.Message}");
                }
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تم حذف المعلم «{teacher.FullName}».");
            }
            default:
                return ToolResult.Fail($"العملية «{a.Action}» غير معروفة. استخدم: list/create/update/delete.");
        }
    }

    private static void ApplyEmployment(Teacher t, Params a, bool isNew)
    {
        var hasChange = isNew || a.Specialization != null || a.EmployeeNumber != null
            || a.MaxPeriodsPerDay.HasValue || a.MaxPeriodsPerWeek.HasValue || a.CanTeachMultipleGrades.HasValue;
        if (!hasChange) return;

        t.UpdateEmploymentInfo(
            a.EmployeeNumber ?? t.EmployeeNumber,
            t.HireDate,
            a.Specialization ?? t.Specialization,
            a.CanTeachMultipleGrades ?? t.CanTeachMultipleGrades,
            a.MaxPeriodsPerDay ?? (isNew ? 6 : t.MaxPeriodsPerDay),
            a.MaxPeriodsPerWeek ?? (isNew ? 30 : t.MaxPeriodsPerWeek));
    }

    private static void ApplyContact(Teacher t, Params a)
    {
        if (a.Email == null && a.Phone == null) return;
        t.UpdatePersonalInfo(t.FirstName, t.LastName, t.MiddleName, a.Email, a.Phone,
            t.Address, t.DateOfBirth, t.NationalId);
    }

    private sealed record Params
    {
        public string? Action { get; init; }
        public string? Target { get; init; }
        public string? Name { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? MiddleName { get; init; }
        public string? Specialization { get; init; }
        public string? EmployeeNumber { get; init; }
        public string? Email { get; init; }
        public string? Phone { get; init; }
        public int? MaxPeriodsPerDay { get; init; }
        public int? MaxPeriodsPerWeek { get; init; }
        public bool? CanTeachMultipleGrades { get; init; }
    }
}

// =======================================================================
//  المواد
// =======================================================================
public sealed class ManageSubjectsAiTool : BaseTool
{
    private readonly AssistantToolContext _context;
    public override string Name => "manage_subjects";
    public override string Description =>
        "إدارة المواد: عرض القائمة، إضافة مادة، تعديلها، أو حذفها. استخدمها لأي طلب يخص المواد.";
    public override JsonSchema Schema { get; }

    public ManageSubjectsAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "العملية", Enum = new[] { "list", "create", "update", "delete" } },
                ["target"] = new JsonSchemaProperty { Type = "string", Description = "المادة المستهدفة (اسمها أو معرّفها) — مطلوب في update/delete" },
                ["name"] = new JsonSchemaProperty { Type = "string", Description = "اسم المادة" },
                ["shortName"] = new JsonSchemaProperty { Type = "string", Description = "الاسم المختصر" },
                ["description"] = new JsonSchemaProperty { Type = "string", Description = "وصف" },
                ["gradeLevel"] = new JsonSchemaProperty { Type = "integer", Description = "الصف (1-12)" },
                ["defaultPeriodsPerWeek"] = new JsonSchemaProperty { Type = "integer", Description = "عدد الحصص الأسبوعية" },
                ["minPeriodsPerWeek"] = new JsonSchemaProperty { Type = "integer", Description = "أدنى حصص أسبوعيًا" },
                ["maxPeriodsPerWeek"] = new JsonSchemaProperty { Type = "integer", Description = "أقصى حصص أسبوعيًا" },
                ["colorHex"] = new JsonSchemaProperty { Type = "string", Description = "اللون بصيغة #RRGGBB" }
            },
            Required = new[] { "action" }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var a = ParseParameters<Params>(parameters);
        var action = (a.Action ?? "list").Trim().ToLowerInvariant();

        using var scope = _context.ScopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<ISubjectRepository>();

        switch (action)
        {
            case "list":
            {
                var school = await CrudSupport.GetSchoolIdAsync(sp);
                var all = await repo.GetBySchoolAsync(school, cancellationToken);
                var lines = all.OrderBy(s => s.Name)
                    .Select(s => $"{s.Name}" + (s.DefaultPeriodsPerWeek > 0 ? $" ({s.DefaultPeriodsPerWeek} حصة/أسبوع)" : ""));
                return ToolResult.Ok($"المواد ({all.Count}):\n" + CrudSupport.ListLines(lines));
            }
            case "create":
            {
                if (string.IsNullOrWhiteSpace(a.Name))
                    return ToolResult.Fail("حدّد اسم المادة.");

                var school = await _context.School.GetSchoolIdAsync();
                var subject = new Subject(school, a.Name!.Trim(), a.ShortName);
                subject.UpdateBasicInfo(a.Name!.Trim(), a.ShortName, a.Description, a.GradeLevel);
                if (a.DefaultPeriodsPerWeek > 0 || a.MinPeriodsPerWeek > 0 || a.MaxPeriodsPerWeek > 0)
                    subject.UpdateSchedule(a.DefaultPeriodsPerWeek, a.MinPeriodsPerWeek, a.MaxPeriodsPerWeek);
                if (!string.IsNullOrWhiteSpace(a.ColorHex)) subject.SetColor(a.ColorHex);
                await repo.AddAsync(subject, cancellationToken);
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تمت إضافة المادة «{subject.Name}».");
            }
            case "update":
            {
                var subject = await CrudSupport.FindSubjectAsync(sp, a.Target ?? a.Name, cancellationToken);
                if (subject == null) return ToolResult.Fail($"لم يتم العثور على المادة «{a.Target ?? a.Name}».");

                subject.UpdateBasicInfo(a.Name ?? subject.Name, a.ShortName ?? subject.ShortName,
                    a.Description ?? subject.Description, a.GradeLevel > 0 ? a.GradeLevel : subject.GradeLevel);
                if (a.DefaultPeriodsPerWeek > 0 || a.MinPeriodsPerWeek > 0 || a.MaxPeriodsPerWeek > 0)
                    subject.UpdateSchedule(
                        a.DefaultPeriodsPerWeek > 0 ? a.DefaultPeriodsPerWeek : subject.DefaultPeriodsPerWeek,
                        a.MinPeriodsPerWeek > 0 ? a.MinPeriodsPerWeek : subject.MinPeriodsPerWeek,
                        a.MaxPeriodsPerWeek > 0 ? a.MaxPeriodsPerWeek : subject.MaxPeriodsPerWeek);
                if (!string.IsNullOrWhiteSpace(a.ColorHex)) subject.SetColor(a.ColorHex);
                await repo.UpdateAsync(subject, cancellationToken);
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تم تحديث المادة «{subject.Name}».");
            }
            case "delete":
            {
                var subject = await CrudSupport.FindSubjectAsync(sp, a.Target ?? a.Name, cancellationToken);
                if (subject == null) return ToolResult.Fail($"لم يتم العثور على المادة «{a.Target ?? a.Name}».");
                if (!_context.Confirm("تأكيد حذف مادة", $"هل تريد حذف المادة «{subject.Name}»؟"))
                    return ToolResult.Ok("تم إلغاء الحذف بناءً على طلب المستخدم.");
                try { await repo.DeleteAsync(subject.Id, cancellationToken); }
                catch (Exception ex) { return ToolResult.Fail($"تعذّر حذف المادة: {ex.Message}"); }
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تم حذف المادة «{subject.Name}».");
            }
            default:
                return ToolResult.Fail($"العملية «{a.Action}» غير معروفة. استخدم: list/create/update/delete.");
        }
    }

    private sealed record Params
    {
        public string? Action { get; init; }
        public string? Target { get; init; }
        public string? Name { get; init; }
        public string? ShortName { get; init; }
        public string? Description { get; init; }
        public int GradeLevel { get; init; }
        public int DefaultPeriodsPerWeek { get; init; }
        public int MinPeriodsPerWeek { get; init; }
        public int MaxPeriodsPerWeek { get; init; }
        public string? ColorHex { get; init; }
    }
}

// =======================================================================
//  الفصول
// =======================================================================
public sealed class ManageClassesAiTool : BaseTool
{
    private readonly AssistantToolContext _context;
    public override string Name => "manage_classes";
    public override string Description =>
        "إدارة الفصول (الشعب): عرض القائمة، إضافة فصل، تعديله، أو حذفه. استخدمها لأي طلب يخص الفصول.";
    public override JsonSchema Schema { get; }

    public ManageClassesAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "العملية", Enum = new[] { "list", "create", "update", "delete" } },
                ["target"] = new JsonSchemaProperty { Type = "string", Description = "الفصل المستهدف (اسمه أو معرّفه) — مطلوب في update/delete" },
                ["name"] = new JsonSchemaProperty { Type = "string", Description = "اسم الفصل" },
                ["gradeLevel"] = new JsonSchemaProperty { Type = "integer", Description = "الصف (1-12)" },
                ["section"] = new JsonSchemaProperty { Type = "string", Description = "الشعبة (حرف واحد، مثل أ/ب)" },
                ["shortName"] = new JsonSchemaProperty { Type = "string", Description = "الاسم المختصر" },
                ["code"] = new JsonSchemaProperty { Type = "string", Description = "الرمز" },
                ["studentCount"] = new JsonSchemaProperty { Type = "integer", Description = "عدد الطلاب" },
                ["homeroomTeacher"] = new JsonSchemaProperty { Type = "string", Description = "معلم الفصل (اسم أو معرّف)" },
                ["defaultRoom"] = new JsonSchemaProperty { Type = "string", Description = "القاعة الافتراضية (اسم أو معرّف)" }
            },
            Required = new[] { "action" }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var a = ParseParameters<Params>(parameters);
        var action = (a.Action ?? "list").Trim().ToLowerInvariant();

        using var scope = _context.ScopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<IClassRepository>();

        switch (action)
        {
            case "list":
            {
                var school = await CrudSupport.GetSchoolIdAsync(sp);
                var all = await repo.GetBySchoolAsync(school, cancellationToken);
                var lines = all.OrderBy(c => c.GradeLevel).ThenBy(c => c.Section).Select(c => c.FullName);
                return ToolResult.Ok($"الفصول ({all.Count}):\n" + CrudSupport.ListLines(lines));
            }
            case "create":
            {
                if (string.IsNullOrWhiteSpace(a.Name))
                    return ToolResult.Fail("حدّد اسم الفصل.");
                var section = string.IsNullOrWhiteSpace(a.Section) ? 'أ' : a.Section!.Trim()[0];
                var school = await _context.School.GetSchoolIdAsync();
                var cls = new Class(school, a.Name!.Trim(), a.GradeLevel <= 0 ? 1 : a.GradeLevel, section);
                cls.UpdateBasicInfo(a.Name!.Trim(), a.ShortName, a.Code, a.StudentCount);
                await ApplyClassLinksAsync(sp, cls, a, cancellationToken);
                await repo.AddAsync(cls, cancellationToken);
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تمت إضافة الفصل «{cls.FullName}».");
            }
            case "update":
            {
                var cls = await CrudSupport.FindClassAsync(sp, a.Target ?? a.Name, cancellationToken);
                if (cls == null) return ToolResult.Fail($"لم يتم العثور على الفصل «{a.Target ?? a.Name}».");
                cls.UpdateBasicInfo(a.Name ?? cls.Name, a.ShortName ?? cls.ShortName, a.Code ?? cls.Code,
                    a.StudentCount > 0 ? a.StudentCount : cls.StudentCount);
                if (a.StudentCount > 0) cls.SetStudentCount(a.StudentCount);
                await ApplyClassLinksAsync(sp, cls, a, cancellationToken);
                await repo.UpdateAsync(cls, cancellationToken);
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تم تحديث الفصل «{cls.FullName}».");
            }
            case "delete":
            {
                var cls = await CrudSupport.FindClassAsync(sp, a.Target ?? a.Name, cancellationToken);
                if (cls == null) return ToolResult.Fail($"لم يتم العثور على الفصل «{a.Target ?? a.Name}».");
                if (!_context.Confirm("تأكيد حذف فصل", $"هل تريد حذف الفصل «{cls.FullName}»؟"))
                    return ToolResult.Ok("تم إلغاء الحذف بناءً على طلب المستخدم.");
                try { await repo.DeleteAsync(cls.Id, cancellationToken); }
                catch (Exception ex) { return ToolResult.Fail($"تعذّر حذف الفصل: {ex.Message}"); }
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تم حذف الفصل «{cls.FullName}».");
            }
            default:
                return ToolResult.Fail($"العملية «{a.Action}» غير معروفة. استخدم: list/create/update/delete.");
        }
    }

    private static async Task ApplyClassLinksAsync(IServiceProvider sp, Class cls, Params a, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(a.HomeroomTeacher))
        {
            var t = await CrudSupport.FindTeacherAsync(sp, a.HomeroomTeacher, ct);
            if (t != null) cls.SetHomeroomTeacher((TeacherId)t.Id);
        }
        if (!string.IsNullOrWhiteSpace(a.DefaultRoom))
        {
            var r = await CrudSupport.FindRoomAsync(sp, a.DefaultRoom, ct);
            if (r != null) cls.SetDefaultRoom((RoomId)r.Id);
        }
    }

    private sealed record Params
    {
        public string? Action { get; init; }
        public string? Target { get; init; }
        public string? Name { get; init; }
        public int GradeLevel { get; init; }
        public string? Section { get; init; }
        public string? ShortName { get; init; }
        public string? Code { get; init; }
        public int StudentCount { get; init; }
        public string? HomeroomTeacher { get; init; }
        public string? DefaultRoom { get; init; }
    }
}

// =======================================================================
//  القاعات
// =======================================================================
public sealed class ManageRoomsAiTool : BaseTool
{
    private readonly AssistantToolContext _context;
    public override string Name => "manage_rooms";
    public override string Description =>
        "إدارة القاعات: عرض القائمة، إضافة قاعة، تعديلها، أو حذفها. استخدمها لأي طلب يخص القاعات.";
    public override JsonSchema Schema { get; }

    public ManageRoomsAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "العملية", Enum = new[] { "list", "create", "update", "delete" } },
                ["target"] = new JsonSchemaProperty { Type = "string", Description = "القاعة المستهدفة (اسمها أو معرّفها) — مطلوب في update/delete" },
                ["name"] = new JsonSchemaProperty { Type = "string", Description = "اسم القاعة" },
                ["shortName"] = new JsonSchemaProperty { Type = "string", Description = "الاسم المختصر" },
                ["code"] = new JsonSchemaProperty { Type = "string", Description = "الرمز" },
                ["description"] = new JsonSchemaProperty { Type = "string", Description = "وصف" },
                ["capacity"] = new JsonSchemaProperty { Type = "integer", Description = "السعة" },
                ["building"] = new JsonSchemaProperty { Type = "string", Description = "المبنى" },
                ["floor"] = new JsonSchemaProperty { Type = "integer", Description = "الطابق" },
                ["isLab"] = new JsonSchemaProperty { Type = "boolean", Description = "مختبر" }
            },
            Required = new[] { "action" }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var a = ParseParameters<Params>(parameters);
        var action = (a.Action ?? "list").Trim().ToLowerInvariant();

        using var scope = _context.ScopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<IRoomRepository>();

        switch (action)
        {
            case "list":
            {
                var school = await CrudSupport.GetSchoolIdAsync(sp);
                var all = await repo.GetBySchoolAsync(school, cancellationToken);
                var lines = all.OrderBy(r => r.Name)
                    .Select(r => $"{r.Name}" + (r.Capacity > 0 ? $" (سعة {r.Capacity})" : "") + (r.IsLab ? " [مختبر]" : ""));
                return ToolResult.Ok($"القاعات ({all.Count}):\n" + CrudSupport.ListLines(lines));
            }
            case "create":
            {
                if (string.IsNullOrWhiteSpace(a.Name))
                    return ToolResult.Fail("حدّد اسم القاعة.");
                var school = await _context.School.GetSchoolIdAsync();
                var room = new Room(school, a.Name!.Trim(), a.ShortName, a.Capacity > 0 ? a.Capacity : 30);
                room.UpdateBasicInfo(a.Name!.Trim(), a.ShortName, a.Code, a.Description, a.Capacity > 0 ? a.Capacity : room.Capacity);
                if (a.Building != null || a.Floor > 0) room.UpdateLocation(a.Floor, a.Building);
                if (a.IsLab.HasValue) room.UpdateType(a.IsLab.Value, false, false, false, false);
                await repo.AddAsync(room, cancellationToken);
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تمت إضافة القاعة «{room.Name}».");
            }
            case "update":
            {
                var room = await CrudSupport.FindRoomAsync(sp, a.Target ?? a.Name, cancellationToken);
                if (room == null) return ToolResult.Fail($"لم يتم العثور على القاعة «{a.Target ?? a.Name}».");
                room.UpdateBasicInfo(a.Name ?? room.Name, a.ShortName ?? room.ShortName, a.Code ?? room.Code,
                    a.Description ?? room.Description, a.Capacity > 0 ? a.Capacity : room.Capacity);
                if (a.Building != null || a.Floor > 0) room.UpdateLocation(a.Floor > 0 ? a.Floor : room.Floor, a.Building ?? room.Building);
                await repo.UpdateAsync(room, cancellationToken);
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تم تحديث القاعة «{room.Name}».");
            }
            case "delete":
            {
                var room = await CrudSupport.FindRoomAsync(sp, a.Target ?? a.Name, cancellationToken);
                if (room == null) return ToolResult.Fail($"لم يتم العثور على القاعة «{a.Target ?? a.Name}».");
                if (!_context.Confirm("تأكيد حذف قاعة", $"هل تريد حذف القاعة «{room.Name}»؟"))
                    return ToolResult.Ok("تم إلغاء الحذف بناءً على طلب المستخدم.");
                try { await repo.DeleteAsync(room.Id, cancellationToken); }
                catch (Exception ex) { return ToolResult.Fail($"تعذّر حذف القاعة: {ex.Message}"); }
                _context.RaiseDataChanged();
                return ToolResult.Ok($"تم حذف القاعة «{room.Name}».");
            }
            default:
                return ToolResult.Fail($"العملية «{a.Action}» غير معروفة. استخدم: list/create/update/delete.");
        }
    }

    private sealed record Params
    {
        public string? Action { get; init; }
        public string? Target { get; init; }
        public string? Name { get; init; }
        public string? ShortName { get; init; }
        public string? Code { get; init; }
        public string? Description { get; init; }
        public int Capacity { get; init; }
        public string? Building { get; init; }
        public int Floor { get; init; }
        public bool? IsLab { get; init; }
    }
}

// =======================================================================
//  الحصص (على الجدول المفتوح)
// =======================================================================
public sealed class ManageLessonsAiTool : BaseTool
{
    private readonly AssistantToolContext _context;
    public override string Name => "manage_lessons";
    public override string Description =>
        "إدارة حصص الجدول المفتوح: عرض، إضافة، تحريك (تغيير اليوم/الفترة)، أو حذف حصة. استخدمها لطلبات جدولة الحصص.";
    public override JsonSchema Schema { get; }

    public ManageLessonsAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "العملية", Enum = new[] { "list", "add", "move", "delete" } },
                ["subject"] = new JsonSchemaProperty { Type = "string", Description = "المادة (اسم أو معرّف)" },
                ["teacher"] = new JsonSchemaProperty { Type = "string", Description = "المعلم (اسم أو معرّف)" },
                ["class"] = new JsonSchemaProperty { Type = "string", Description = "الفصل (اسم أو معرّف)" },
                ["room"] = new JsonSchemaProperty { Type = "string", Description = "القاعة (اسم أو معرّف، اختياري)" },
                ["day"] = new JsonSchemaProperty { Type = "integer", Description = "اليوم (0=السبت .. 6=الجمعة)" },
                ["period"] = new JsonSchemaProperty { Type = "integer", Description = "الفترة (1-12)" },
                ["newDay"] = new JsonSchemaProperty { Type = "integer", Description = "اليوم الجديد عند التحريك" },
                ["newPeriod"] = new JsonSchemaProperty { Type = "integer", Description = "الفترة الجديدة عند التحريك" }
            },
            Required = new[] { "action" }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable == null)
            return ToolResult.Fail("لا يوجد جدول مفتوح حاليًا. افتح جدولًا أولًا.");

        var a = ParseParameters<Params>(parameters);
        var action = (a.Action ?? "list").Trim().ToLowerInvariant();

        using var scope = _context.ScopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var subject = await CrudSupport.FindSubjectAsync(sp, a.Subject, cancellationToken);
        var teacher = await CrudSupport.FindTeacherAsync(sp, a.Teacher, cancellationToken);
        var cls = await CrudSupport.FindClassAsync(sp, a.Class, cancellationToken);
        var room = await CrudSupport.FindRoomAsync(sp, a.Room, cancellationToken);

        switch (action)
        {
            case "list":
            {
                IEnumerable<Lesson> q = timetable.Lessons;
                if (cls != null) q = q.Where(l => (Guid)l.ClassId == (Guid)cls.Id);
                if (teacher != null) q = q.Where(l => (Guid)l.TeacherId == (Guid)teacher.Id);
                if (subject != null) q = q.Where(l => (Guid)l.SubjectId == (Guid)subject.Id);
                var list = q.ToList();
                var lines = list.Select(l =>
                    $"{l.Subject?.Name ?? "?"} — {l.Teacher?.DisplayShortName ?? "?"} — {l.Class?.Name ?? "?"} @ " +
                    (l.Period.Value == 0 ? "غير مُسندة" : $"{AssistantHelpers.DayName(l.Day)} ف{l.Period.Value}"));
                return ToolResult.Ok($"الحصص ({list.Count}):\n" + CrudSupport.ListLines(lines));
            }
            case "add":
            {
                if (subject == null) return ToolResult.Fail("حدّد المادة بشكل صحيح.");
                if (teacher == null) return ToolResult.Fail("حدّد المعلم بشكل صحيح.");
                if (cls == null) return ToolResult.Fail("حدّد الفصل بشكل صحيح.");
                if (a.Day is < 0 or > 6) return ToolResult.Fail("رقم اليوم بين 0 (السبت) و6 (الجمعة).");

                var period = a.Period is >= 1 and <= 12 ? PeriodNumber.Create((byte)a.Period) : PeriodNumber.Unscheduled;
                var lesson = new Lesson(new TimetableId(timetable.Id), (SubjectId)subject.Id, (TeacherId)teacher.Id,
                    (ClassId)cls.Id, (DomainDayOfWeek)a.Day, period, Duration.FromMinutes(45));
                if (room != null) lesson.AssignRoom((RoomId)room.Id);
                lesson.AttachReferences(subject, teacher, cls, room);
                timetable.AddLesson(lesson);
                _context.RaiseChanged();
                return ToolResult.Ok($"تمت إضافة حصة {subject.Name} لـ{teacher.DisplayShortName} في {cls.Name}.");
            }
            case "move":
            {
                var lesson = FindLesson(timetable, subject, teacher, cls, a);
                if (lesson == null) return ToolResult.Fail("لم يتم العثور على الحصة المطلوبة.");
                if (a.NewDay is < 0 or > 6) return ToolResult.Fail("حدّد اليوم الجديد (newDay) بين 0 و6.");
                if (a.NewPeriod is < 1 or > 12) return ToolResult.Fail("حدّد الفترة الجديدة (newPeriod) بين 1 و12.");
                lesson.Reschedule((DomainDayOfWeek)a.NewDay, PeriodNumber.Create((byte)a.NewPeriod));
                _context.RaiseChanged();
                return ToolResult.Ok($"تم تحريك الحصة إلى {AssistantHelpers.DayName((DomainDayOfWeek)a.NewDay)} — الفترة {a.NewPeriod}.");
            }
            case "delete":
            {
                var lesson = FindLesson(timetable, subject, teacher, cls, a);
                if (lesson == null) return ToolResult.Fail("لم يتم العثور على الحصة المطلوبة.");
                if (!_context.Confirm("تأكيد حذف حصة",
                        $"حذف حصة {lesson.Subject?.Name ?? "?"} لـ{lesson.Teacher?.DisplayShortName ?? "?"} في {lesson.Class?.Name ?? "?"}؟"))
                    return ToolResult.Ok("تم إلغاء الحذف بناءً على طلب المستخدم.");
                timetable.RemoveLesson(lesson);
                _context.RaiseChanged();
                return ToolResult.Ok("تم حذف الحصة.");
            }
            default:
                return ToolResult.Fail($"العملية «{a.Action}» غير معروفة. استخدم: list/add/move/delete.");
        }
    }

    private static Lesson? FindLesson(Timetable tt, Subject? subject, Teacher? teacher, Class? cls, Params a)
    {
        IEnumerable<Lesson> q = tt.Lessons;
        if (subject != null) q = q.Where(l => (Guid)l.SubjectId == (Guid)subject.Id);
        if (teacher != null) q = q.Where(l => (Guid)l.TeacherId == (Guid)teacher.Id);
        if (cls != null) q = q.Where(l => (Guid)l.ClassId == (Guid)cls.Id);
        if (a.Day is >= 0 and <= 6) q = q.Where(l => (int)l.Day == a.Day);
        if (a.Period is >= 0 and <= 12) q = q.Where(l => l.Period.Value == a.Period);
        return q.FirstOrDefault();
    }

    private sealed record Params
    {
        public string? Action { get; init; }
        public string? Subject { get; init; }
        public string? Teacher { get; init; }
        public string? Class { get; init; }
        public string? Room { get; init; }
        public int Day { get; init; } = -1;
        public int Period { get; init; } = -1;
        public int NewDay { get; init; } = -1;
        public int NewPeriod { get; init; } = -1;
    }
}

// =======================================================================
//  الإسناد (على الجدول المفتوح)
// =======================================================================
public sealed class ManageAssignmentsAiTool : BaseTool
{
    private readonly AssistantToolContext _context;
    public override string Name => "manage_assignments";
    public override string Description =>
        "إدارة الإسناد (معلم+مادة+فصل+عدد الحصص الأسبوعية): عرض، إضافة، حذف، تفعيل/تعطيل، أو توليد الحصص غير المُسندة. استخدمها لطلبات الإسناد.";
    public override JsonSchema Schema { get; }

    public ManageAssignmentsAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "العملية", Enum = new[] { "list", "add", "remove", "toggle", "set_periods", "generate" } },
                ["teacher"] = new JsonSchemaProperty { Type = "string", Description = "المعلم (اسم أو معرّف)" },
                ["subject"] = new JsonSchemaProperty { Type = "string", Description = "المادة (اسم أو معرّف)" },
                ["class"] = new JsonSchemaProperty { Type = "string", Description = "الفصل (اسم أو معرّف)" },
                ["weeklyPeriods"] = new JsonSchemaProperty { Type = "integer", Description = "عدد الحصص الأسبوعية" },
                ["enabled"] = new JsonSchemaProperty { Type = "boolean", Description = "مفعّل؟ (في toggle)" }
            },
            Required = new[] { "action" }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable == null)
            return ToolResult.Fail("لا يوجد جدول مفتوح حاليًا. افتح جدولًا أولًا.");

        var a = ParseParameters<Params>(parameters);
        var action = (a.Action ?? "list").Trim().ToLowerInvariant();
        timetable.Assignments ??= new List<TeachingAssignment>();

        using var scope = _context.ScopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var teacher = await CrudSupport.FindTeacherAsync(sp, a.Teacher, cancellationToken);
        var subject = await CrudSupport.FindSubjectAsync(sp, a.Subject, cancellationToken);
        var cls = await CrudSupport.FindClassAsync(sp, a.Class, cancellationToken);

        switch (action)
        {
            case "list":
            {
                var lines = timetable.Assignments.Select(x =>
                {
                    var t = Find(timetable, x.TeacherId); var s = Find2(timetable, x.SubjectId); var c = Find3(timetable, x.ClassId);
                    return $"{t} / {s} / {c} — {x.WeeklyPeriods} حصة" + (x.IsEnabled ? "" : " [معطَّل]");
                });
                return ToolResult.Ok($"الإسنادات ({timetable.Assignments.Count}):\n" + CrudSupport.ListLines(lines));
            }
            case "add":
            {
                if (teacher == null) return ToolResult.Fail("حدّد المعلم.");
                if (subject == null) return ToolResult.Fail("حدّد المادة.");
                if (cls == null) return ToolResult.Fail("حدّد الفصل.");
                var assignment = new TeachingAssignment
                {
                    TeacherId = (Guid)teacher.Id,
                    SubjectId = (Guid)subject.Id,
                    ClassId = (Guid)cls.Id,
                    WeeklyPeriods = Math.Max(1, a.WeeklyPeriods > 0 ? a.WeeklyPeriods : 1)
                };
                timetable.Assignments.Add(assignment);
                _context.RaiseChanged();
                return ToolResult.Ok($"تمت إضافة إسناد {subject.Name} لـ{teacher.DisplayShortName} في {cls.Name} ({assignment.WeeklyPeriods} حصة).");
            }
            case "remove":
            {
                var match = MatchAssignment(timetable, teacher, subject, cls);
                if (match == null) return ToolResult.Fail("لم يتم العثور على الإسناد.");
                timetable.Assignments.Remove(match);
                _context.RaiseChanged();
                return ToolResult.Ok("تم حذف الإسناد.");
            }
            case "toggle":
            {
                var match = MatchAssignment(timetable, teacher, subject, cls);
                if (match == null) return ToolResult.Fail("لم يتم العثور على الإسناد.");
                match.IsEnabled = a.Enabled ?? !match.IsEnabled;
                _context.RaiseChanged();
                return ToolResult.Ok($"الإسناد الآن {(match.IsEnabled ? "مفعّل" : "معطَّل")}.");
            }
            case "set_periods":
            {
                var match = MatchAssignment(timetable, teacher, subject, cls);
                if (match == null) return ToolResult.Fail("لم يتم العثور على الإسناد.");
                if (a.WeeklyPeriods < 1) return ToolResult.Fail("حدّد عدد الحصص الأسبوعية (1 على الأقل).");
                match.WeeklyPeriods = a.WeeklyPeriods;
                _context.RaiseChanged();
                return ToolResult.Ok($"تم ضبط عدد الحصص الأسبوعية إلى {a.WeeklyPeriods}.");
            }
            case "generate":
            {
                int created = 0, removed = 0;
                foreach (var asg in timetable.Assignments.ToList())
                {
                    var stale = timetable.Lessons.Where(l => l.Period.Value < 1
                        && (Guid)l.TeacherId == asg.TeacherId
                        && (Guid)l.SubjectId == asg.SubjectId
                        && (Guid)l.ClassId == asg.ClassId).ToList();
                    foreach (var old in stale) { timetable.RemoveLesson(old); removed++; }

                    if (!asg.IsEnabled) continue;

                    var t = await CrudSupport.FindTeacherAsync(sp, asg.TeacherId.ToString(), cancellationToken);
                    var s = await CrudSupport.FindSubjectAsync(sp, asg.SubjectId.ToString(), cancellationToken);
                    var c = await CrudSupport.FindClassAsync(sp, asg.ClassId.ToString(), cancellationToken);

                    for (int i = 0; i < Math.Max(0, asg.WeeklyPeriods); i++)
                    {
                        var lesson = new Lesson(new TimetableId(timetable.Id), new SubjectId(asg.SubjectId),
                            new TeacherId(asg.TeacherId), new ClassId(asg.ClassId),
                            DomainDayOfWeek.Saturday, PeriodNumber.Unscheduled, Duration.FromMinutes(45));
                        lesson.AttachReferences(s, t, c);
                        timetable.AddLesson(lesson);
                        created++;
                    }
                }
                _context.RaiseChanged();
                return ToolResult.Ok($"تم توليد {created} حصة غير مُسندة" + (removed > 0 ? $"، واستُبدلت {removed} سابقة." : "."));
            }
            default:
                return ToolResult.Fail($"العملية «{a.Action}» غير معروفة. استخدم: list/add/remove/toggle/set_periods/generate.");
        }
    }

    private static TeachingAssignment? MatchAssignment(Timetable tt, Teacher? t, Subject? s, Class? c)
        => tt.Assignments.FirstOrDefault(x =>
            (t == null || x.TeacherId == (Guid)t.Id) &&
            (s == null || x.SubjectId == (Guid)s.Id) &&
            (c == null || x.ClassId == (Guid)c.Id));

    private static string Find(Timetable tt, Guid id) =>
        tt.Lessons.FirstOrDefault(l => (Guid)l.TeacherId == id)?.Teacher?.DisplayShortName ?? id.ToString().Substring(0, 8);
    private static string Find2(Timetable tt, Guid id) =>
        tt.Lessons.FirstOrDefault(l => (Guid)l.SubjectId == id)?.Subject?.Name ?? id.ToString().Substring(0, 8);
    private static string Find3(Timetable tt, Guid id) =>
        tt.Lessons.FirstOrDefault(l => (Guid)l.ClassId == id)?.Class?.Name ?? id.ToString().Substring(0, 8);

    private sealed record Params
    {
        public string? Action { get; init; }
        public string? Teacher { get; init; }
        public string? Subject { get; init; }
        public string? Class { get; init; }
        public int WeeklyPeriods { get; init; }
        public bool? Enabled { get; init; }
    }
}

// =======================================================================
//  القيود (عرض)
// =======================================================================
public sealed class ManageConstraintsAiTool : BaseTool
{
    public override string Name => "manage_constraints";
    public override string Description =>
        "عرض القيود المتاحة في النظام وشرحها. استخدمها عندما يسأل المستخدم عن القيود أو القواعد.";
    public override JsonSchema Schema { get; }

    public ManageConstraintsAiTool()
    {
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "العملية", Enum = new[] { "list" } }
            }
        };
    }

    public override Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var all = ConstraintRegistry.CreateAll().ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"القواعد المتاحة ({all.Count}):");
        foreach (var c in all)
            sb.AppendLine($"• {c.Name} — {(string.IsNullOrWhiteSpace(c.Description) ? c.Type.ToString() : c.Description)}" +
                          (c.IsHard ? " [صلبة]" : ""));
        sb.AppendLine("يمكن تفعيل/تعطيل القيود وتعديل أولويتها من تبويب «القيود» في التطبيق.");
        return Task.FromResult(ToolResult.Ok(sb.ToString().TrimEnd()));
    }
}

// =======================================================================
//  ���� ������� (��� ������� ��� ������ �) � ������� ����� �ai� ������� ������ ���������
// =======================================================================
public sealed class ManageRulesAiTool : BaseTool
{
    private readonly AssistantToolContext _context;
    public override string Name => "manage_rules";
    public override string Description =>
        "����� ���� ������� �� ������ ������� (��� ������� ��� ������ ���ѡ ����� ���ɡ ���): ��֡ ����ɡ ����� ��������/����ݡ ����� �� ���. �� ��� ���� �� ����� �������ϻ. �������� ���������� ������.";
    public override JsonSchema Schema { get; }

    public ManageRulesAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "�������", Enum = new[] { "list", "add", "set_priority", "set_target", "toggle", "delete" } },
                ["title"] = new JsonSchemaProperty { Type = "string", Description = "��� ����� (�� add) �� ������� �� �����/�������" },
                ["kind"] = new JsonSchemaProperty { Type = "string", Description = "�����", Enum = new[] { "EndTime", "TeacherLink", "Availability", "SubjectDistribution", "MaxConsecutive", "MaxGapsPerDay", "DailyLoad", "MaxDaysPerWeek", "MaxGapsPerWeek", "MaxSpanPerDay", "BeginsEarly", "SubjectPair", "SubjectTwicePerDay", "BlockLessons", "SameStartingTime", "NotOverlapping", "MinDaysBetweenActivities", "PreferredRoom", "RoomChangeLimit", "MinDaysPerWeek", "General" } },
                ["scope"] = new JsonSchemaProperty { Type = "string", Description = "������", Enum = new[] { "Teacher", "Class", "Subject", "Room", "School" } },
                ["target"] = new JsonSchemaProperty { Type = "string", Description = "���/����� ����� (����/���/����/����)� �� set_target �� ����� ������" },
                ["priority"] = new JsonSchemaProperty { Type = "string", Description = "��������", Enum = new[] { "Hard", "High", "Medium", "Low", "Ignore" } },
                ["enabled"] = new JsonSchemaProperty { Type = "boolean", Description = "����� (�� toggle)" },
                                ["id"] = new JsonSchemaProperty { Type = "string", Description = "id" },
                ["parameters"] = new JsonSchemaProperty { Type = "string", Description = "JSON params: block; end-time; maxPerDay; maxConsecutive; maxGapsPerDay; maxDaysPerWeek; maxGapsPerWeek; maxSpanPerDay; maxBeginningsSecondHour; minDaysBetween; maxRoomChangesPerDay; maxRoomChangesPerWeek; minDaysPerWeek; secondTargetId; noSameDay; noConsecutive; mustBeConsecutive" }
            },
            Required = new[] { "action" }
        };
    }

    public override Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable == null)
            return Task.FromResult(ToolResult.Fail("�� ���� ���� ����� ������. ���� ������ �����."));

        var a = ParseParameters<Params>(parameters);
        var action = (a.Action ?? "list").Trim().ToLowerInvariant();
        timetable.Rules ??= new List<RuleItem>();

        switch (action)
        {
            case "list":
            {
                var lines = timetable.Rules.Select(r =>
                    $"[{r.Id.ToString()[..8]}] {AwadTimetable.UI.ViewModels.RuleTexts.Kind(r.Kind)} � {r.Title} � {AwadTimetable.UI.ViewModels.RuleTexts.Priority(r.Priority)}" +
                    (r.IsEnabled ? "" : " [������]") + (r.Source == "ai" ? " (�������)" : ""));
                return Task.FromResult(ToolResult.Ok($"���� ������� ({timetable.Rules.Count}):\n" + CrudSupport.ListLines(lines)));
            }
            case "add":
            {
                if (string.IsNullOrWhiteSpace(a.Title))
                    return Task.FromResult(ToolResult.Fail("���� ��� ����� (title)."));
                var scope = ParseEnum(a.Scope, RuleScope.School);
                var kind = ParseEnum(a.Kind, RuleKind.General);
                var priority = ParseEnum(a.Priority, RulePriority.Medium);
                var rule = new RuleItem
                {
                    Kind = kind,
                    Scope = scope,
                    Title = a.Title!.Trim(),
                    Priority = priority,
                    Parameters = string.IsNullOrWhiteSpace(a.Parameters) ? null : a.Parameters!.Trim(),
                    Source = "ai"
                };
                rule.TargetId = ResolveTarget(scope, a.Target);
                if (scope != RuleScope.School && rule.TargetId == null && !string.IsNullOrWhiteSpace(a.Target))
                    return Task.FromResult(ToolResult.Fail($"�� ��� ������ ��� �{a.Target}� ��� {AwadTimetable.UI.ViewModels.RuleTexts.Scope(scope)}."));

                timetable.Rules.Add(rule);
                _context.RaiseDataChanged();
                _context.RaiseChanged();
                return Task.FromResult(ToolResult.Ok($"����� ����� �{rule.Title}� ������� {AwadTimetable.UI.ViewModels.RuleTexts.Priority(priority)} � ���� �� ����� �������ϻ."));
            }
            default:
            {
                var rule = FindRule(timetable, a);
                if (rule == null) return Task.FromResult(ToolResult.Fail("�� ��� ������ ��� �����. ������ list ������ ���������."));

                switch (action)
                {
                    case "set_priority":
                        rule.Priority = ParseEnum(a.Priority, rule.Priority);
                        break;
                    case "set_target":
                        var newTarget = ResolveTarget(rule.Scope, a.Target);
                        if (newTarget == null) return Task.FromResult(ToolResult.Fail($"�� ��� ������ ��� �{a.Target}�."));
                        rule.TargetId = newTarget;
                        break;
                    case "toggle":
                        rule.IsEnabled = a.Enabled ?? !rule.IsEnabled;
                        break;
                    case "delete":
                        if (!_context.Confirm("����� ��� ���", $"��� ����� �{rule.Title}��"))
                            return Task.FromResult(ToolResult.Ok("�� ����� ����� ����� ��� ��� ��������."));
                        timetable.Rules.Remove(rule);
                        _context.RaiseDataChanged();
                        return Task.FromResult(ToolResult.Ok("�� ��� �����."));
                    default:
                        return Task.FromResult(ToolResult.Fail($"������� �{a.Action}� ��� ������. ������: list/add/set_priority/set_target/toggle/delete."));
                }
                rule.UpdatedAt = DateTime.Now;
                _context.RaiseDataChanged();
                return Task.FromResult(ToolResult.Ok($"�� ����� ����� �{rule.Title}�."));
            }
        }
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, true, out var v) ? v : fallback;

    private Guid? ResolveTarget(RuleScope scope, string? key)
    {
        if (scope == RuleScope.School || string.IsNullOrWhiteSpace(key)) return null;
        using var scopeSp = _context.ScopeFactory.CreateScope();
        var sp = scopeSp.ServiceProvider;
        var ct = CancellationToken.None;
        return scope switch
        {
            RuleScope.Teacher => CrudSupport.FindTeacherAsync(sp, key, ct).GetAwaiter().GetResult()?.Id,
            RuleScope.Class => CrudSupport.FindClassAsync(sp, key, ct).GetAwaiter().GetResult()?.Id,
            RuleScope.Subject => CrudSupport.FindSubjectAsync(sp, key, ct).GetAwaiter().GetResult()?.Id,
            RuleScope.Room => CrudSupport.FindRoomAsync(sp, key, ct).GetAwaiter().GetResult()?.Id,
            _ => null
        };
    }

    private static RuleItem? FindRule(Timetable tt, Params a)
    {
        if (CrudSupport.TryGuid(a.Id, out var g))
            return tt.Rules.FirstOrDefault(r => r.Id == g);
        return tt.Rules.FirstOrDefault(r => !string.IsNullOrWhiteSpace(a.Title) &&
            r.Title.Contains(a.Title!, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record Params
    {
        public string? Action { get; init; }
        public string? Title { get; init; }
        public string? Kind { get; init; }
        public string? Scope { get; init; }
        public string? Target { get; init; }
        public string? Priority { get; init; }
        public string? Parameters { get; init; }
        public bool? Enabled { get; init; }
        public string? Id { get; init; }
    }
}

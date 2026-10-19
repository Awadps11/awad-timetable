using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using AwadTimetable.AI.Interfaces;
using AwadTimetable.AI.Tools;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Data.Repositories;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.UI.Services;

/// <summary>مساعدات مشتركة لأدوات المساعد الذكي.</summary>
internal static class AssistantHelpers
{
    public static string DayName(DomainDayOfWeek day) => day switch
    {
        DomainDayOfWeek.Saturday => "السبت",
        DomainDayOfWeek.Sunday => "الأحد",
        DomainDayOfWeek.Monday => "الاثنين",
        DomainDayOfWeek.Tuesday => "الثلاثاء",
        DomainDayOfWeek.Wednesday => "الأربعاء",
        DomainDayOfWeek.Thursday => "الخميس",
        DomainDayOfWeek.Friday => "الجمعة",
        _ => day.ToString()
    };

    /// <summary>مطابقة اسم (أو احتوائه) مع أي من المرشّحين، بلا حساسية لحالة الأحرف.</summary>
    public static bool Matches(string? query, params string?[] candidates)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var q = query.Trim();

        foreach (var c in candidates)
            if (!string.IsNullOrWhiteSpace(c) && string.Equals(c.Trim(), q, StringComparison.OrdinalIgnoreCase))
                return true;

        foreach (var c in candidates)
            if (!string.IsNullOrWhiteSpace(c) && c.Trim().Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}

/// <summary>فحص التضاربات في الجدول المفتوح فعليًا.</summary>
public sealed class CheckConflictsAiTool : BaseTool
{
    private readonly AssistantToolContext _context;

    public override string Name => "check_conflicts";
    public override string Description =>
        "فحص التضاربات في الجدول المفتوح حاليًا (تعارض المعلم/القاعة/السعة). استخدمها عندما يسأل المستخدم عن وجود تضاربات أو مشاكل في الجدول.";
    public override JsonSchema Schema { get; }

    public CheckConflictsAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["severity"] = new JsonSchemaProperty
                {
                    Type = "string",
                    Description = "تصفية حسب الشدة (اختياري)",
                    Enum = new[] { "all", "high", "critical" }
                }
            }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable == null)
            return ToolResult.Fail("لا يوجد جدول مفتوح حاليًا. افتح جدولًا أولًا.");

        var args = ParseParameters<Params>(parameters);
        var all = await _context.Detector.DetectConflictsAsync(timetable, cancellationToken);

        var filtered = (args.Severity ?? "all").ToLowerInvariant() switch
        {
            "high" => all.Where(c => c.Severity >= ConflictSeverity.High).ToList(),
            "critical" => all.Where(c => c.Severity == ConflictSeverity.Critical).ToList(),
            _ => all.ToList()
        };

        if (filtered.Count == 0)
            return ToolResult.Ok($"لا توجد تضاربات في الجدول «{timetable.Name}» ✓");

        var sb = new StringBuilder();
        sb.AppendLine($"عدد التضاربات في «{timetable.Name}»: {filtered.Count}.");
        foreach (var c in filtered.Take(15))
            sb.AppendLine($"• [{c.Severity}] {c.Description}");
        if (filtered.Count > 15)
            sb.AppendLine($"… و{filtered.Count - 15} تضاربًا آخر.");

        return ToolResult.Ok(sb.ToString().TrimEnd());
    }

    private sealed record Params
    {
        public string? Severity { get; init; }
    }
}

/// <summary>شرح/تلخيص واقعي للجدول المفتوح (توزيع الحصص على الأيام والفصول).</summary>
public sealed class ExplainScheduleAiTool : BaseTool
{
    private readonly AssistantToolContext _context;

    public override string Name => "explain_schedule";
    public override string Description =>
        "تلخيص الجدول المفتوح حاليًا: عدد الحصص، توزيعها على الأيام، والفصول الأكثر/الأقل ازدحامًا. استخدمها لشرح الجدول.";
    public override JsonSchema Schema { get; }

    public ExplainScheduleAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["focus"] = new JsonSchemaProperty
                {
                    Type = "string",
                    Description = "ما الذي يُشرح (اختياري)",
                    Enum = new[] { "overview", "by_day", "by_class", "by_teacher" }
                }
            }
        };
    }

    public override Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable == null)
            return Task.FromResult(ToolResult.Fail("لا يوجد جدول مفتوح حاليًا. افتح جدولًا أولًا."));

        var args = ParseParameters<Params>(parameters);
        var lessons = timetable.Lessons;
        var sb = new StringBuilder();
        sb.AppendLine($"شرح الجدول «{timetable.Name}»:");
        sb.AppendLine($"- إجمالي الحصص المجدولة: {lessons.Count}.");

        switch ((args.Focus ?? "overview").ToLowerInvariant())
        {
            case "by_day":
            {
                sb.AppendLine("التوزيع على الأيام:");
                foreach (var g in lessons.GroupBy(l => l.Day).OrderBy(g => g.Key))
                    sb.AppendLine($"  • {AssistantHelpers.DayName(g.Key)}: {g.Count()} حصة.");
                break;
            }
            case "by_class":
            {
                sb.AppendLine("ازدحام الفصول:");
                foreach (var g in lessons.GroupBy(l => l.ClassId).OrderByDescending(g => g.Count()).Take(15))
                {
                    var name = g.First().Class?.Name ?? g.Key.ToString();
                    sb.AppendLine($"  • {name}: {g.Count()} حصة.");
                }
                break;
            }
            case "by_teacher":
            {
                sb.AppendLine("أكثر المعلمين حصصًا:");
                foreach (var g in lessons.GroupBy(l => l.TeacherId).OrderByDescending(g => g.Count()).Take(15))
                {
                    var name = g.First().Teacher?.FullName ?? g.Key.ToString();
                    sb.AppendLine($"  • {name}: {g.Count()} حصة.");
                }
                break;
            }
            default:
            {
                var busy = lessons.GroupBy(l => l.Day).OrderByDescending(g => g.Count()).FirstOrDefault();
                var quiet = lessons.GroupBy(l => l.Day).OrderBy(g => g.Count()).FirstOrDefault();
                if (busy != null)
                    sb.AppendLine($"- أكثر يوم ازدحامًا: {AssistantHelpers.DayName(busy.Key)} ({busy.Count()} حصة).");
                if (quiet != null)
                    sb.AppendLine($"- أقل يوم ازدحامًا: {AssistantHelpers.DayName(quiet.Key)} ({quiet.Count()} حصة).");
                var classes = lessons.Select(l => l.ClassId).Distinct().Count();
                var teachers = lessons.Select(l => l.TeacherId).Distinct().Count();
                sb.AppendLine($"- عدد الفصول المشغولة: {classes}، وعدد المعلمين المشاركين: {teachers}.");
                break;
            }
        }

        return Task.FromResult(ToolResult.Ok(sb.ToString().TrimEnd()));
    }

    private sealed record Params
    {
        public string? Focus { get; init; }
    }
}

/// <summary>إنشاء حصة حقيقية في الجدول المفتوح مع تأكيد المستخدم.</summary>
public sealed class CreateLessonAiTool : BaseTool
{
    private readonly AssistantToolContext _context;

    public override string Name => "create_lesson";
    public override string Description =>
        "إضافة حصة جديدة إلى الجدول المفتوح حاليًا. يمكن تحديد المادة/المعلم/الفصل بالاسم أو بالمعرّف. تتطلّب تأكيد المستخدم قبل التنفيذ.";
    public override JsonSchema Schema { get; }

    public CreateLessonAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["subject"] = new JsonSchemaProperty { Type = "string", Description = "اسم المادة أو معرّفها" },
                ["teacher"] = new JsonSchemaProperty { Type = "string", Description = "اسم المعلم أو معرّفه" },
                ["class"] = new JsonSchemaProperty { Type = "string", Description = "اسم الفصل أو معرّفه" },
                ["room"] = new JsonSchemaProperty { Type = "string", Description = "اسم القاعة أو معرّفها (اختياري)" },
                ["day"] = new JsonSchemaProperty { Type = "integer", Description = "اليوم (0=السبت .. 6=الجمعة)" },
                ["period"] = new JsonSchemaProperty { Type = "integer", Description = "رقم الفترة (1-12)" }
            },
            Required = new[] { "subject", "teacher", "class", "day", "period" }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable == null)
            return ToolResult.Fail("لا يوجد جدول مفتوح حاليًا. افتح جدولًا أولًا.");

        var args = ParseParameters<Params>(parameters);

        if (string.IsNullOrWhiteSpace(args.Subject) ||
            string.IsNullOrWhiteSpace(args.Teacher) ||
            string.IsNullOrWhiteSpace(args.Class))
            return ToolResult.Fail("يجب تحديد المادة والمعلم والفصل (بالاسم أو المعرف).");

        if (args.Day is < 0 or > 6)
            return ToolResult.Fail("رقم اليوم يجب أن يكون بين 0 (السبت) و 6 (الجمعة).");
        if (args.Period is < 1 or > 12)
            return ToolResult.Fail("رقم الفترة يجب أن يكون بين 1 و 12.");

        var day = (DomainDayOfWeek)args.Day;
        var period = PeriodNumber.Create((byte)args.Period);

        Subject? subject;
        Teacher? teacher;
        Class? classRoom;
        Room? room = null;

        using (var scope = _context.ScopeFactory.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var school = await _context.School.GetSchoolIdAsync();

            var subjectRepo = sp.GetRequiredService<ISubjectRepository>();
            subject = Guid.TryParse(args.Subject, out var sg)
                ? await subjectRepo.GetByIdAsync(new SubjectId(sg), cancellationToken)
                : null;
            if (subject is not null && subject.SchoolId != school) subject = null;
            subject ??= (await subjectRepo.GetBySchoolAsync(school, cancellationToken))
                .FirstOrDefault(s => AssistantHelpers.Matches(args.Subject, s.Name, s.ShortName));

            var teacherRepo = sp.GetRequiredService<ITeacherRepository>();
            teacher = Guid.TryParse(args.Teacher, out var tg)
                ? await teacherRepo.GetByIdAsync(new TeacherId(tg), cancellationToken)
                : null;
            if (teacher is not null && teacher.SchoolId != school) teacher = null;
            teacher ??= (await teacherRepo.GetBySchoolAsync(school, cancellationToken))
                .FirstOrDefault(t => AssistantHelpers.Matches(args.Teacher, t.FullName, t.DisplayShortName));

            var classRepo = sp.GetRequiredService<IClassRepository>();
            classRoom = Guid.TryParse(args.Class, out var cg)
                ? await classRepo.GetByIdAsync(new ClassId(cg), cancellationToken)
                : null;
            if (classRoom is not null && classRoom.SchoolId != school) classRoom = null;
            classRoom ??= (await classRepo.GetBySchoolAsync(school, cancellationToken))
                .FirstOrDefault(c => AssistantHelpers.Matches(args.Class, c.Name, c.ShortName, c.FullName));

            if (!string.IsNullOrWhiteSpace(args.Room))
            {
                var roomRepo = sp.GetRequiredService<IRoomRepository>();
                room = Guid.TryParse(args.Room, out var rg)
                    ? await roomRepo.GetByIdAsync(new RoomId(rg), cancellationToken)
                    : null;
                if (room is not null && room.SchoolId != school) room = null;
                room ??= (await roomRepo.GetBySchoolAsync(school, cancellationToken))
                    .FirstOrDefault(r => AssistantHelpers.Matches(args.Room, r.Name, r.ShortName));
            }
        }

        if (subject == null) return ToolResult.Fail($"لم يتم العثور على المادة «{args.Subject}».");
        if (teacher == null) return ToolResult.Fail($"لم يتم العثور على المعلم «{args.Teacher}».");
        if (classRoom == null) return ToolResult.Fail($"لم يتم العثور على الفصل «{args.Class}».");
        if (!string.IsNullOrWhiteSpace(args.Room) && room == null) return ToolResult.Fail($"لم يتم العثور على القاعة «{args.Room}».");

        var subjectGuidV = subject.Id;
        var teacherGuidV = teacher.Id;
        var classGuidV = classRoom.Id;

        // فحص سريع للتضاربات على نفس (اليوم، الفترة)
        var warnings = new List<string>();
        var sameSlot = timetable.Lessons.Where(l => l.Day == day && (byte)l.Period == (byte)period).ToList();
        if (sameSlot.Any(l => (Guid)l.TeacherId == teacherGuidV))
            warnings.Add($"المعلم «{teacher.DisplayShortName}» مشغول في نفس الوقت.");
        if (sameSlot.Any(l => (Guid)l.ClassId == classGuidV))
            warnings.Add($"الفصل «{classRoom.Name}» مشغول في نفس الوقت.");
        if (room != null && sameSlot.Any(l => l.RoomId != null && (Guid)l.RoomId == (Guid)room.Id))
            warnings.Add($"القاعة «{room.Name}» مشغولة في نفس الوقت.");

        var summary =
            $"مادة: {subject.Name}\n" +
            $"معلم: {teacher.FullName}\n" +
            $"فصل: {classRoom.Name}\n" +
            $"القاعة: {(room?.Name ?? "—")}\n" +
            $"الوقت: {AssistantHelpers.DayName(day)} — الفترة {args.Period}";

        var confirmMessage = "هل تريد إضافة هذه الحصة إلى الجدول؟\n\n" + summary;
        if (warnings.Count > 0)
            confirmMessage += "\n\nتنبيه — تعارضات محتملة:\n- " + string.Join("\n- ", warnings);

        if (!_context.Confirm("تأكيد إضافة حصة", confirmMessage))
            return ToolResult.Ok("تم إلغاء إضافة الحصة بناءً على طلب المستخدم.");

        var lesson = new Lesson(
            new TimetableId(timetable.Id),
            new SubjectId(subjectGuidV),
            new TeacherId(teacherGuidV),
            new ClassId(classGuidV),
            day,
            period,
            Duration.FromMinutes(45));

        if (room != null) lesson.AssignRoom(new RoomId(room.Id));

        timetable.AddLesson(lesson);
        _context.RaiseChanged();

        var result = $"تمت إضافة الحصة بنجاح.\n{summary}" +
                     (warnings.Count > 0 ? "\nملاحظة: قد توجد تعارضات — راجع الجدول." : "");

        return ToolResult.Ok(result, new { lessonId = lesson.Id });
    }

    private sealed record Params
    {
        public string? Subject { get; init; }
        public string? Teacher { get; init; }
        public string? Class { get; init; }
        public string? Room { get; init; }
        public int Day { get; init; }
        public int Period { get; init; }
    }
}

/// <summary>تحسين الجدول المفتوح فعليًا مع تأكيد المستخدم.</summary>
public sealed class OptimizeTimetableAiTool : BaseTool
{
    private readonly AssistantToolContext _context;

    public override string Name => "optimize_timetable";
    public override string Description =>
        "تحسين الجدول المفتوح حاليًا (رفع الجودة وتقليل التضاربات). تتطلّب تأكيد المستخدم.";
    public override JsonSchema Schema { get; }

    public OptimizeTimetableAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["timeLimitMinutes"] = new JsonSchemaProperty { Type = "integer", Description = "الحد الزمني بالدقائق (افتراضي 2)" }
            }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable == null)
            return ToolResult.Fail("لا يوجد جدول مفتوح حاليًا. افتح جدولًا أولًا.");

        var args = ParseParameters<Params>(parameters);

        if (!_context.Confirm("تأكيد تحسين الجدول",
                $"هل تريد تحسين الجدول «{timetable.Name}»؟ قد تستغرق العملية بعض الوقت."))
            return ToolResult.Ok("تم إلغاء التحسين بناءً على طلب المستخدم.");

        var options = new OptimizationOptions
        {
            TimeLimit = TimeSpan.FromMinutes(Math.Clamp(args.TimeLimitMinutes, 1, 30))
        };

        var result = await _context.Generator.OptimizeAsync(timetable, options, cancellationToken);
        _context.RaiseChanged();

        if (!result.Improved)
            return ToolResult.Ok($"لم يُعثر على تحسين إضافي. الجودة الحالية: {result.OldScore.Value:F0}%.");

        return ToolResult.Ok(
            $"تم تحسين الجدول.\nالجودة: {result.OldScore.Value:F0}% ← {result.NewScore.Value:F0}%\n" +
            $"نسبة التحسين: {result.ImprovementPercentage:0.#}%\n" +
            $"التضاربات المتبقية: {result.RemainingViolations.Count}.");
    }

    private sealed record Params
    {
        public int TimeLimitMinutes { get; init; } = 2;
    }
}

/// <summary>إضافة قيد — القيود مُدارة عبر سجل القيود وبواجهة القيود، فلا ندّعي إضافتها من المحادثة.</summary>
public sealed class AddConstraintAiTool : BaseTool
{
    public override string Name => "add_constraint";
    public override string Description => "توضيح كيفية إدارة القيود (القيود مُدارة عبر سجل القيود وبواجهة القيود).";
    public override JsonSchema Schema { get; }

    public AddConstraintAiTool()
    {
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["constraintType"] = new JsonSchemaProperty
                {
                    Type = "string",
                    Description = "نوع القيد",
                    Enum = AwadTimetable.Core.Constraints.ConstraintRegistry.GetAllKeys()
                }
            }
        };
    }

    public override Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var args = ParseParameters<Params>(parameters);
        var known = AwadTimetable.Core.Constraints.ConstraintRegistry.GetAllKeys()
            .Contains(args.ConstraintType ?? "");

        if (!string.IsNullOrWhiteSpace(args.ConstraintType) && !known)
            return Task.FromResult(ToolResult.Fail($"نوع القيد «{args.ConstraintType}» غير معروف."));

        return Task.FromResult(ToolResult.Ok(
            "القيود في هذا النظام تُطبَّق عبر «محرر القيود» داخل التطبيق (تبويب القيود)، وليس من المحادثة. " +
            "افتح تبويب القيود لتفعيل/تعطيل القيود المطلوبة."));
    }

    private sealed record Params
    {
        public string? ConstraintType { get; init; }
    }
}

/// <summary>توليد الجدول (أو إعادة توليده) بالمحرك المختار.</summary>
public sealed class GenerateTimetableAiTool : BaseTool
{
    private readonly AssistantToolContext _context;

    public override string Name => "generate_timetable";
    public override string Description =>
        "يولّد/يعيد توليد الجدول بالمحرك المختار ويطبّق النتيجة. استخدمه بعد التأكد من الإعدادات والإسنادات.";
    public override JsonSchema Schema { get; }

    public GenerateTimetableAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["time_limit_seconds"] = new JsonSchemaProperty
                {
                    Type = "integer",
                    Description = "مهلة التوليد بالثواني (افتراضي 60، بين 5 و600)."
                }
            }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable is null) return ToolResult.Fail("لا يوجد جدول مفتوح.");

        var args = ParseParameters<Params>(parameters);
        var seconds = Math.Clamp(args.TimeLimitSeconds ?? 60, 5, 600);

        var result = await _context.Generator.GenerateAsync(
            timetable,
            new GenerationOptions { TimeLimit = TimeSpan.FromSeconds(seconds), UseParallel = true },
            cancellationToken);

        _context.RaiseChanged();

        if (!result.Success)
            return ToolResult.Fail(result.ErrorMessage ?? "فشل توليد الجدول.");

        return ToolResult.Ok(
            $"تم التوليد: {result.LessonsScheduled} حصة، {result.ConflictsCount} تضارب، " +
            $"الجودة {result.QualityScore.Value:F0}%، المدة {result.Duration.TotalSeconds:F1} ث.");
    }

    private sealed record Params
    {
        public int? TimeLimitSeconds { get; init; }
    }
}

/// <summary>تصميم قالب طباعة من وصف المستخدم وفتح معاينة مباشرة.</summary>
public sealed class DesignPrintTemplateAiTool : BaseTool
{
    private readonly AssistantToolContext _context;

    public override string Name => "design_print_template";
    public override string Description =>
        "يصمّم قالب طباعة من طلب المستخدم (ألوان/خط/حجم/خيارات) ويعرض معاينة الطباعة فوراً.";
    public override JsonSchema Schema { get; }

    public DesignPrintTemplateAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        var props = new Dictionary<string, JsonSchemaProperty>
        {
            ["template_name"] = new JsonSchemaProperty { Type = "string", Description = "اسم القالب." },
            ["header_color"] = new JsonSchemaProperty { Type = "string", Description = "لون الترويسة hex مثل #4B6BAC." },
            ["day_color"] = new JsonSchemaProperty { Type = "string", Description = "لون رأس اليوم hex." },
            ["row_color1"] = new JsonSchemaProperty { Type = "string", Description = "لون الصف الأول hex." },
            ["row_color2"] = new JsonSchemaProperty { Type = "string", Description = "لون الصف الثاني hex." },
            ["text_color"] = new JsonSchemaProperty { Type = "string", Description = "لون النص hex." },
            ["accent_color"] = new JsonSchemaProperty { Type = "string", Description = "اللون المميز hex." },
            ["font_family"] = new JsonSchemaProperty { Type = "string", Description = "اسم الخط مثل Segoe UI." },
            ["font_size"] = new JsonSchemaProperty { Type = "integer", Description = "حجم الخط (8..16)." },
            ["page_size"] = new JsonSchemaProperty { Type = "string", Description = "A4 أو A3 أو Letter أو Legal." },
            ["orientation"] = new JsonSchemaProperty { Type = "string", Description = "portrait أو landscape." },
            ["cell_style"] = new JsonSchemaProperty { Type = "string", Description = "filled أو outlined أو minimal." },
            ["show_period_times"] = new JsonSchemaProperty { Type = "boolean", Description = "إظهار أوقات الفترات." },
            ["show_teacher"] = new JsonSchemaProperty { Type = "boolean", Description = "إظهار أسماء المعلمين." },
            ["show_room"] = new JsonSchemaProperty { Type = "boolean", Description = "إظهار القاعات." },
            ["show_summary"] = new JsonSchemaProperty { Type = "boolean", Description = "إظهار تقرير الملخص." },
            ["show_legend"] = new JsonSchemaProperty { Type = "boolean", Description = "إظهار شريط التنقل." },
            ["zebra"] = new JsonSchemaProperty { Type = "boolean", Description = "تلوين الصفوف المتبادل." },
        };

        Schema = new JsonSchema { Type = "object", Properties = props };
    }

    public override Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable is null) return Task.FromResult(ToolResult.Fail("لا يوجد جدول مفتوح."));

        var p = ParseParameters<Params>(parameters);

        var template = new AwadTimetable.Printing.Templates.PrintTemplate
        {
            Name = string.IsNullOrWhiteSpace(p.TemplateName) ? "قالب المساعد" : p.TemplateName!.Trim(),
            HeaderColor = Norm(p.HeaderColor, "#4B6BAC"),
            DayColor = Norm(p.DayColor, "#3D5990"),
            RowColor1 = Norm(p.RowColor1, "#FFFFFF"),
            RowColor2 = Norm(p.RowColor2, "#F3F7FF"),
            TextColor = Norm(p.TextColor, "#112233"),
            AccentColor = Norm(p.AccentColor, "#4B6BAC"),
            FontFamily = string.IsNullOrWhiteSpace(p.FontFamily) ? "Segoe UI" : p.FontFamily!.Trim(),
            FontSize = Math.Clamp(p.FontSize ?? 10, 8, 16),
            PageSize = (p.PageSize ?? "A4").ToLowerInvariant() switch
            {
                "a3" => AwadTimetable.Printing.Services.PrintPageSize.A3,
                "letter" => AwadTimetable.Printing.Services.PrintPageSize.Letter,
                "legal" => AwadTimetable.Printing.Services.PrintPageSize.Legal,
                _ => AwadTimetable.Printing.Services.PrintPageSize.A4,
            },
            Orientation = string.Equals(p.Orientation, "portrait", StringComparison.OrdinalIgnoreCase)
                ? AwadTimetable.Printing.Services.PrintOrientation.Portrait
                : AwadTimetable.Printing.Services.PrintOrientation.Landscape,
            CellStyle = (p.CellStyle ?? "filled").ToLowerInvariant() switch
            {
                "outlined" => AwadTimetable.Printing.Templates.PrintCellStyle.Outlined,
                "minimal" => AwadTimetable.Printing.Templates.PrintCellStyle.Minimal,
                _ => AwadTimetable.Printing.Templates.PrintCellStyle.Filled,
            },
            ShowPeriodTimes = p.ShowPeriodTimes ?? true,
            ShowTeacher = p.ShowTeacher ?? true,
            ShowRoom = p.ShowRoom ?? true,
            ShowSummary = p.ShowSummary ?? true,
            ShowLegend = p.ShowLegend ?? true,
            Zebra = p.Zebra ?? true,
        };

        var html = _context.WebReport.GenerateHtml(
            timetable,
            new AwadTimetable.Printing.Services.WebReportOptions
            {
                SchoolName = string.Empty,
                ProductName = "نظام عوض للجداول المدرسية",
            },
            lesson => (
                lesson.Subject?.Name ?? "",
                lesson.Teacher?.DisplayShortName ?? "",
                lesson.Class?.DisplayShortName ?? "",
                lesson.Room?.Name ?? ""),
            template);

        var app = System.Windows.Application.Current;
        app?.Dispatcher.Invoke(() =>
        {
            var window = new AwadTimetable.UI.Views.PrintPreviewWindow(html)
            {
                Owner = app.MainWindow,
                Title = $"معاينة الطباعة — {template.Name}",
            };
            window.ShowDialog();
        });

        return Task.FromResult(ToolResult.Ok(
            $"تم تصميم القالب «{template.Name}» وفتح المعاينة. " +
            "يمكنك حفظه كقالب من نافذة «قوالب الطباعة والمعاينة»."));
    }

    private static string Norm(string? value, string fallback)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0) return fallback;
        if (!v.StartsWith('#')) v = "#" + v;
        return v.Length == 7 ? v.ToUpperInvariant() : fallback;
    }

    private sealed record Params
    {
        public string? TemplateName { get; init; }
        public string? HeaderColor { get; init; }
        public string? DayColor { get; init; }
        public string? RowColor1 { get; init; }
        public string? RowColor2 { get; init; }
        public string? TextColor { get; init; }
        public string? AccentColor { get; init; }
        public string? FontFamily { get; init; }
        public int? FontSize { get; init; }
        public string? PageSize { get; init; }
        public string? Orientation { get; init; }
        public string? CellStyle { get; init; }
        public bool? ShowPeriodTimes { get; init; }
        public bool? ShowTeacher { get; init; }
        public bool? ShowRoom { get; init; }
        public bool? ShowSummary { get; init; }
        public bool? ShowLegend { get; init; }
        public bool? Zebra { get; init; }
    }
}

/// <summary>تقسيم حصة (Split) إلى عدة حصص موزّعة على أيام مختلفة مع ربط مجموعة وقاعدة أدنى أيام.</summary>
public sealed class SplitLessonAiTool : BaseTool
{
    private readonly AssistantToolContext _context;

    public override string Name => "split_lesson";
    public override string Description =>
        "تقسيم حصة إلى عدة حصص (2..7) تُوزَّع على أيام مختلفة وتُربط في مجموعة واحدة، مع إمكانية إنشاء قاعدة " +
        "«أدنى أيام بين الحصص» تلقائياً (على نمط FET). تُحدَّد الحصة بمعرّفها أو بالمادة/الفصل. تتطلّب تأكيد المستخدم.";
    public override JsonSchema Schema { get; }

    public SplitLessonAiTool(AssistantToolContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["lessonId"] = new JsonSchemaProperty { Type = "string", Description = "معرّف الحصة (اختياري إن حُدّدت المادة والفصل)." },
                ["subject"] = new JsonSchemaProperty { Type = "string", Description = "اسم المادة أو معرّفها (لتحديد الحصة)." },
                ["class"] = new JsonSchemaProperty { Type = "string", Description = "اسم الفصل أو معرّفه (لتحديد الحصة)." },
                ["day"] = new JsonSchemaProperty { Type = "integer", Description = "اليوم (0=السبت .. 6=الجمعة) لتصفية الحصة (اختياري)." },
                ["parts"] = new JsonSchemaProperty { Type = "integer", Description = "عدد الأجزاء (2..7)." },
                ["minDaysBetween"] = new JsonSchemaProperty { Type = "integer", Description = "أدنى عدد أيام بين الأجزاء (1..7) — ينشئ قاعدة صلبة." },
                ["splitDuration"] = new JsonSchemaProperty { Type = "boolean", Description = "تقسيم المدة على الأجزاء (افتراضي true)." }
            },
            Required = new[] { "parts" }
        };
    }

    public override Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var timetable = _context.CurrentTimetable;
        if (timetable is null)
            return Task.FromResult(ToolResult.Fail("لا يوجد جدول مفتوح حاليًا. افتح جدولًا أولًا."));

        var args = ParseParameters<Params>(parameters);

        if (args.Parts is < LessonSplitter.MinParts or > LessonSplitter.MaxParts)
            return Task.FromResult(ToolResult.Fail(
                $"عدد الأجزاء يجب أن يكون بين {LessonSplitter.MinParts} و {LessonSplitter.MaxParts}."));

        Lesson? lesson = null;

        if (!string.IsNullOrWhiteSpace(args.LessonId) && Guid.TryParse(args.LessonId, out var lid))
            lesson = timetable.Lessons.FirstOrDefault(l => l.Id == lid);

        if (lesson is null)
        {
            var candidates = timetable.Lessons.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(args.Subject))
                candidates = candidates.Where(l => AssistantHelpers.Matches(args.Subject, l.Subject?.Name, l.Subject?.ShortName));
            if (!string.IsNullOrWhiteSpace(args.Class))
                candidates = candidates.Where(l => AssistantHelpers.Matches(args.Class, l.Class?.Name, l.Class?.ShortName, l.Class?.FullName));
            if (args.Day is >= 0 and <= 6)
                candidates = candidates.Where(l => l.Day == (DomainDayOfWeek)args.Day);

            lesson = candidates.FirstOrDefault();
        }

        if (lesson is null)
            return Task.FromResult(ToolResult.Fail("لم يتم العثور على حصة مطابقة. حدّد معرّف الحصة أو المادة والفصل."));

        var subjectName = lesson.Subject?.Name ?? "مادة";
        var className = lesson.Class?.Name ?? "فصل";
        var perPart = (int)lesson.Duration / args.Parts;
        var willSplitDuration = (args.SplitDuration ?? true) && perPart >= 30 && perPart % 5 == 0;

        var summary =
            $"المادة: {subjectName}\n" +
            $"الفصل: {className}\n" +
            $"المدة الحالية: {(int)lesson.Duration} دقيقة\n" +
            $"عدد الأجزاء: {args.Parts}\n" +
            $"مدة كل جزء: {(willSplitDuration ? perPart : (int)lesson.Duration)} دقيقة" +
            (args.MinDaysBetween is int m ? $"\nأدنى أيام بين الأجزاء: {m}" : "");

        if (!_context.Confirm("تأكيد تقسيم الحصة", "هل تريد تقسيم هذه الحصة؟\n\n" + summary))
            return Task.FromResult(ToolResult.Ok("تم إلغاء التقسيم بناءً على طلب المستخدم."));

        LessonSplitResult result;
        try
        {
            result = LessonSplitter.Split(
                timetable,
                lesson,
                args.Parts,
                minDaysBetween: args.MinDaysBetween,
                splitDuration: args.SplitDuration ?? true);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"تعذّر التقسيم: {ex.Message}"));
        }

        _context.RaiseChanged();

        var message =
            $"تم تقسيم الحصة إلى {result.Parts.Count} أجزاء موزّعة على أيام مختلفة ضمن مجموعة واحدة.\n{summary}" +
            (result.MinDaysRule is not null ? "\nأُضيفت قاعدة «أدنى أيام بين الحصص» تلقائياً." : "");

        return Task.FromResult(ToolResult.Ok(message, new
        {
            groupId = result.GroupId,
            lessonIds = result.Parts.Select(p => p.Id).ToArray()
        }));
    }

    private sealed record Params
    {
        public string? LessonId { get; init; }
        public string? Subject { get; init; }
        public string? Class { get; init; }
        public int? Day { get; init; }
        public int Parts { get; init; }
        public int? MinDaysBetween { get; init; }
        public bool? SplitDuration { get; init; }
    }
}

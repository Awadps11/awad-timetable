using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using AwadTimetable.AI.Interfaces;
using AwadTimetable.AI.Tools;
using AwadTimetable.Data.Repositories;

namespace AwadTimetable.UI.Services;

/// <summary>
/// أداة حقيقية لقراءة بيانات المدرسة من قاعدة البيانات (عدد/قوائم المعلمين والفصول والمواد والقاعات والحصص).
/// تُستخدم من قِبل المساعد الذكي للإجابة على الأسئلة الواقعية.
/// تُنشئ نطاق خدمة (scope) خاصًا لكل نداء لأن المستودعات مسجّلة كـ Scoped والسجل Singleton.
/// </summary>
public sealed class SchoolQueryTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSchoolService _school;
    private readonly AssistantToolContext _context;

    public override string Name => "query_school_data";
    public override string Description =>
        "قراءة بيانات مساحة العمل الحالية (المدرسة المفتوحة) من قاعدة البيانات: إجمالي الأعداد والقوائم (المعلمون، الفصول، المواد، القاعات، الحصص، الجداول). استخدم هذه الأداة للإجابة عن أسئلة مثل 'كم عدد المعلمين؟' أو 'اذكر الفصول'.";
    public override JsonSchema Schema { get; }

    public SchoolQueryTool(IServiceScopeFactory scopeFactory, CurrentSchoolService school, AssistantToolContext context)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _school = school ?? throw new ArgumentNullException(nameof(school));
        _context = context ?? throw new ArgumentNullException(nameof(context));

        Schema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["query"] = new JsonSchemaProperty
                {
                    Type = "string",
                    Description = "نوع الاستعلام المطلوب",
                    Enum = new[]
                    {
                        "overview", "teachers_count", "classes_count", "subjects_count",
                        "rooms_count", "lessons_count", "timetables_count",
                        "list_teachers", "list_classes", "list_subjects", "list_rooms", "list_timetables"
                    }
                },
                ["limit"] = new JsonSchemaProperty
                {
                    Type = "integer",
                    Description = "الحد الأقصى لعدد العناصر في القائمة (افتراضي 100)"
                }
            },
            Required = new[] { "query" }
        };
    }

    public override async Task<ToolResult> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        var args = ParseParameters<QueryParams>(parameters);
        var query = (args.Query ?? "overview").Trim().ToLowerInvariant();
        var limit = args.Limit is > 0 ? args.Limit.Value : 100;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var teachers = sp.GetRequiredService<ITeacherRepository>();
        var classes = sp.GetRequiredService<IClassRepository>();
        var subjects = sp.GetRequiredService<ISubjectRepository>();
        var rooms = sp.GetRequiredService<IRoomRepository>();
        var timetables = sp.GetRequiredService<ITimetableRepository>();

        try
        {
            var schoolId = await _school.GetSchoolIdAsync();

            switch (query)
            {
                case "teachers_count":
                    return ToolResult.Ok($"عدد المعلمين في المدرسة: {(await teachers.GetBySchoolAsync(schoolId, cancellationToken)).Count}.");

                case "classes_count":
                    return ToolResult.Ok($"عدد الفصول في المدرسة: {(await classes.GetBySchoolAsync(schoolId, cancellationToken)).Count}.");

                case "subjects_count":
                    return ToolResult.Ok($"عدد المواد الدراسية: {(await subjects.GetBySchoolAsync(schoolId, cancellationToken)).Count}.");

                case "rooms_count":
                    return ToolResult.Ok($"عدد القاعات: {(await rooms.GetBySchoolAsync(schoolId, cancellationToken)).Count}.");

                case "lessons_count":
                    return ToolResult.Ok($"عدد الحصص في الجدول المفتوح: {_context.CurrentTimetable?.Lessons.Count ?? 0}.");

                case "timetables_count":
                    return ToolResult.Ok($"عدد الجداول المحفوظة لهذه المدرسة: {(await timetables.GetBySchoolAsync(schoolId, cancellationToken)).Count}.");

                case "list_teachers":
                {
                    var items = await teachers.GetBySchoolAsync(schoolId, cancellationToken);
                    return ToolResult.Ok(BuildList("المعلمون", items.Select(t => t.FullName), limit));
                }
                case "list_classes":
                {
                    var items = await classes.GetBySchoolAsync(schoolId, cancellationToken);
                    return ToolResult.Ok(BuildList("الفصول", items.Select(c => c.Name), limit));
                }
                case "list_subjects":
                {
                    var items = await subjects.GetBySchoolAsync(schoolId, cancellationToken);
                    return ToolResult.Ok(BuildList("المواد", items.Select(s => s.Name), limit));
                }
                case "list_rooms":
                {
                    var items = await rooms.GetBySchoolAsync(schoolId, cancellationToken);
                    return ToolResult.Ok(BuildList("القاعات", items.Select(r => r.Name), limit));
                }
                case "list_timetables":
                {
                    var items = await timetables.GetBySchoolAsync(schoolId, cancellationToken);
                    return ToolResult.Ok(BuildList("الجداول", items.Select(t => t.Name), limit));
                }

                case "overview":
                default:
                {
                    var tCount = (await teachers.GetBySchoolAsync(schoolId, cancellationToken)).Count;
                    var cCount = (await classes.GetBySchoolAsync(schoolId, cancellationToken)).Count;
                    var sCount = (await subjects.GetBySchoolAsync(schoolId, cancellationToken)).Count;
                    var rCount = (await rooms.GetBySchoolAsync(schoolId, cancellationToken)).Count;
                    var lCount = _context.CurrentTimetable?.Lessons.Count ?? 0;
                    var ttCount = (await timetables.GetBySchoolAsync(schoolId, cancellationToken)).Count;

                    var sb = new StringBuilder();
                    sb.AppendLine("ملخّص بيانات مساحة العمل الحالية:");
                    sb.AppendLine($"- المعلمون: {tCount}");
                    sb.AppendLine($"- الفصول: {cCount}");
                    sb.AppendLine($"- المواد: {sCount}");
                    sb.AppendLine($"- القاعات: {rCount}");
                    sb.AppendLine($"- الحصص في الجدول المفتوح: {lCount}");
                    sb.AppendLine($"- الجداول المحفوظة: {ttCount}");
                    return ToolResult.Ok(sb.ToString().TrimEnd());
                }
            }
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"تعذّر قراءة بيانات المدرسة: {ex.Message}");
        }
    }

    private static string BuildList(string title, IEnumerable<string> names, int limit)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (list.Count == 0) return $"لا يوجد أي عنصر في: {title}.";

        var shown = list.Take(limit).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"{title} ({list.Count}):");
        for (var i = 0; i < shown.Count; i++)
            sb.AppendLine($"{i + 1}. {shown[i]}");
        if (list.Count > shown.Count)
            sb.AppendLine($"… و{list.Count - shown.Count} عنصرًا إضافيًا.");
        return sb.ToString().TrimEnd();
    }

    private sealed record QueryParams
    {
        public string? Query { get; init; }
        public int? Limit { get; init; }
    }
}

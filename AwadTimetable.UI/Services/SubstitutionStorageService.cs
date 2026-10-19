using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using AwadTimetable.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AwadTimetable.UI.Services;

/// <summary>صف تعيين بديل محفوظ في قاعدة البيانات</summary>
public sealed record StoredSubstitution(
    DateOnly Date,
    Guid TimetableId,
    Guid LessonId,
    int Day,
    byte Period,
    Guid ClassId,
    Guid SubjectId,
    Guid AbsentTeacherId,
    Guid? SubstituteTeacherId,
    int? Tier,
    string Status);

/// <summary>
/// تخزين تعيينات المعلمين البديلين (نظام الانتظار) — جدول خام بجانب إعدادات الجدول
/// </summary>
public class SubstitutionStorageService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubstitutionStorageService> _logger;

    public SubstitutionStorageService(
        IServiceScopeFactory scopeFactory,
        ILogger<SubstitutionStorageService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>حفظ تعيينات يوم كامل (يستبدل أي تعيينات سابقة لنفس اليوم)</summary>
    public Task SaveDayAsync(DateOnly date, Guid timetableId, IReadOnlyList<SubstitutionAssignmentRow> rows)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.TimetableDbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        EnsureTable(conn);
        var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM SubstitutionAssignments WHERE Date=$d";
            AddParam(del, "$d", dateText);
            del.ExecuteNonQuery();
        }

        foreach (var row in rows)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText =
                "INSERT INTO SubstitutionAssignments " +
                "(Id, Date, TimetableId, LessonId, Day, Period, ClassId, SubjectId, AbsentTeacherId, SubstituteTeacherId, Tier, Status) " +
                "VALUES ($id, $d, $tt, $les, $day, $per, $cls, $sub, $abs, $rep, $tier, $st)";
            AddParam(ins, "$id", Guid.NewGuid().ToString("D"));
            AddParam(ins, "$d", dateText);
            AddParam(ins, "$tt", timetableId.ToString("D"));
            AddParam(ins, "$les", row.LessonId.ToString("D"));
            AddParam(ins, "$day", row.Day);
            AddParam(ins, "$per", row.Period);
            AddParam(ins, "$cls", row.ClassId.ToString("D"));
            AddParam(ins, "$sub", row.SubjectId.ToString("D"));
            AddParam(ins, "$abs", row.AbsentTeacherId.ToString("D"));
            AddParam(ins, "$rep", row.SubstituteTeacherId?.ToString("D"));
            AddParam(ins, "$tier", (object?)row.Tier ?? DBNull.Value);
            AddParam(ins, "$st", row.Status);
            ins.ExecuteNonQuery();
        }

        _logger.LogInformation("حُفظت {Count} تعيين بديل لتاريخ {Date}", rows.Count, dateText);
        return Task.CompletedTask;
    }

    /// <summary>تحميل تعيينات يوم</summary>
    public async Task<IReadOnlyList<StoredSubstitution>> LoadDayAsync(DateOnly date)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.TimetableDbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        EnsureTable(conn);

        var list = new List<StoredSubstitution>();
        var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT Date, TimetableId, LessonId, Day, Period, ClassId, SubjectId, AbsentTeacherId, SubstituteTeacherId, Tier, Status " +
            "FROM SubstitutionAssignments WHERE Date=$d ORDER BY Period, ClassId";
        AddParam(cmd, "$d", dateText);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new StoredSubstitution(
                DateOnly.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetByte(4),
                Guid.Parse(reader.GetString(5)),
                Guid.Parse(reader.GetString(6)),
                Guid.Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.GetString(10)));
        }

        return list;
    }

    /// <summary>حذف تعيينات يوم كامل</summary>
    public async Task ClearDayAsync(DateOnly date)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.TimetableDbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        EnsureTable(conn);

        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM SubstitutionAssignments WHERE Date=$d";
        AddParam(del, "$d", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var affected = await del.ExecuteNonQueryAsync();
        _logger.LogInformation("حُذفت {Count} تعيين بديل لتاريخ {Date}", affected, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static void EnsureTable(System.Data.Common.DbConnection conn)
    {
        using var create = conn.CreateCommand();
        create.CommandText =
            "CREATE TABLE IF NOT EXISTS SubstitutionAssignments (" +
            "Id TEXT PRIMARY KEY NOT NULL, " +
            "Date TEXT NOT NULL, " +
            "TimetableId TEXT, " +
            "LessonId TEXT, " +
            "Day INTEGER, " +
            "Period INTEGER, " +
            "ClassId TEXT, " +
            "SubjectId TEXT, " +
            "AbsentTeacherId TEXT, " +
            "SubstituteTeacherId TEXT, " +
            "Tier INTEGER, " +
            "Status TEXT)";
        create.ExecuteNonQuery();
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}

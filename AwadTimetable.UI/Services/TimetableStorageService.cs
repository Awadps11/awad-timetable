using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Data;
using AwadTimetable.Data.Repositories;

namespace AwadTimetable.UI.Services;

/// <summary>سطر عرض لقائمة الجداول المحفوظة في قاعدة البيانات</summary>
public sealed record DbTimetableInfo(
    Guid Id,
    Guid SchoolId,
    string Name,
    string SchoolName,
    int YearStart,
    int YearEnd,
    int Term,
    string Status,
    int LessonsCount,
    DateTime? UpdatedAt);

/// <summary>
/// خدمة حفظ/فتح/حذف الجداول في قاعدة البيانات المحلية (SQLite)
/// </summary>
public sealed class TimetableStorageService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSchoolService _currentSchool;
    private readonly ILogger<TimetableStorageService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public TimetableStorageService(
        IServiceScopeFactory scopeFactory,
        CurrentSchoolService currentSchool,
        ILogger<TimetableStorageService> logger)
    {
        _scopeFactory = scopeFactory;
        _currentSchool = currentSchool;
        _logger = logger;
    }

    /// <summary>ضمان وجود مخطط قاعدة البيانات (+ جدول إعدادات الجدول للتوقيتات + أعمدة بديل المعلم)</summary>
    private void EnsureSchema(TimetableDbContext db)
    {
        EnsureDatabaseSchema(db);
    }

    /// <summary>إنشاء/ترقية المخطط عند بدء التشغيل (يتصل بالسياق مباشرة)</summary>
    public static void EnsureDatabaseSchema(TimetableDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Timetables'";
            if (check.ExecuteScalar() == null)
            {
                db.Database.Migrate();
            }
        }

        using (var create = conn.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE IF NOT EXISTS TimetableSettings (" +
                "TimetableId TEXT PRIMARY KEY NOT NULL, " +
                "PeriodTimesJson TEXT, " +
                "StageConfigsJson TEXT, " +
                "AssignmentsJson TEXT, " +
                "RulesJson TEXT)";
            create.ExecuteNonQuery();
        }

        // قواعد بيانات قديمة: عمود StageConfigsJson غير موجود — أضفه بهدوء إن لم يكن موجوداً
        TryAddColumn(conn, "ALTER TABLE TimetableSettings ADD COLUMN StageConfigsJson TEXT");

        // قواعد بيانات قديمة: عمود الإسناد المركزي غير موجود — أضفه بهدوء
        TryAddColumn(conn, "ALTER TABLE TimetableSettings ADD COLUMN AssignmentsJson TEXT");

        // قواعد بيانات قديمة: عمود بنود القواعد غير موجود — أضفه بهدوء
        TryAddColumn(conn, "ALTER TABLE TimetableSettings ADD COLUMN RulesJson TEXT");

        // محرك التوليد المختار لكل جدول (CP-SAT افتراضياً للجداول القديمة)
        TryAddColumn(conn, "ALTER TABLE TimetableSettings ADD COLUMN GeneratorEngine INTEGER NOT NULL DEFAULT 0");

        // نوع المدرسة (متوسط افتراضياً للجداول القديمة)
        TryAddColumn(conn, "ALTER TABLE TimetableSettings ADD COLUMN SchoolType INTEGER NOT NULL DEFAULT 1");

        // درجة الجودة (كانت تُفقد عند إعادة فتح الجدول)
        TryAddColumn(conn, "ALTER TABLE TimetableSettings ADD COLUMN QualityScore REAL NOT NULL DEFAULT 0");

        // خيارات القاعات: قاعة مخصّصة لمعلم، وقاعة مشتركة
        TryAddColumn(conn, "ALTER TABLE Rooms ADD COLUMN AssignedTeacherId TEXT");
        TryAddColumn(conn, "ALTER TABLE Rooms ADD COLUMN IsShared INTEGER NOT NULL DEFAULT 0");

        // محرك التوليد المختار لهذا الجدول — العمود الحقيقي على جدول Timetables (تقرأه EF مباشرة)
        TryAddColumn(conn, "ALTER TABLE Timetables ADD COLUMN Engine INTEGER NOT NULL DEFAULT 0");

        // نوع المدرسة لهذا الجدول — العمود الحقيقي على جدول Timetables
        TryAddColumn(conn, "ALTER TABLE Timetables ADD COLUMN SchoolType INTEGER NOT NULL DEFAULT 1");

        // أولوية إغلاق وقت المعلم (التفريغ/الندب): 1 = عالية (ملزم) وهو الافتراضي للصفوف القديمة
        TryAddColumn(conn, "ALTER TABLE TeacherAvailabilities ADD COLUMN Priority INTEGER NOT NULL DEFAULT 1");

        // أعمدة البديل الثلاثة للمعلمين (قواعد قديمة أُنشئت قبل نظام الانتظار)
        TryAddColumn(conn, "ALTER TABLE Teachers ADD COLUMN Substitute1TeacherId TEXT");
        TryAddColumn(conn, "ALTER TABLE Teachers ADD COLUMN Substitute2TeacherId TEXT");
        TryAddColumn(conn, "ALTER TABLE Teachers ADD COLUMN Substitute3TeacherId TEXT");

        // السماح بأكثر من مساحة عمل بنفس اسم المدرسة (جدول جديد = بيانات فارغة تماماً)
        TryExec(conn, "DROP INDEX IF EXISTS IX_Schools_Name");

        // تنظيف بقايا الإعدادات لجداول محذوفة (مقارنة غير حسّاسة لحالة الأحرف لأن المعرّفات تُخزَّن بصيغ مختلفة)
        TryExec(conn,
            "DELETE FROM TimetableSettings WHERE upper(TimetableId) NOT IN (SELECT upper(Id) FROM Timetables)");
        if (TableExists(conn, "SubstitutionAssignments"))
            TryExec(conn,
                "DELETE FROM SubstitutionAssignments WHERE upper(TimetableId) NOT IN (SELECT upper(Id) FROM Timetables)");

        // السماح بأكثر من جدول في نفس السنة/الفصل الدراسي (المفتاح الفريد السابق كان يمسح الجدول السابق عند الحفظ)
        TryExec(conn, "DROP INDEX IF EXISTS IX_Timetables_SchoolId_AcademicYearStart_AcademicYearEnd_TermNumber");
        TryExec(conn,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Timetables_SchoolId_Name_AcademicYearStart_AcademicYearEnd_TermNumber " +
            "ON Timetables (SchoolId, Name, AcademicYearStart, AcademicYearEnd, TermNumber)");

        // السماح بأكثر من شعبة بنفس (الصف، الشعبة) — يمنع فشل إنشاء شعب الانتظار وتكرار الأقسام
        TryExec(conn, "DROP INDEX IF EXISTS IX_Classes_SchoolId_GradeLevel_Section");
        TryExec(conn,
            "CREATE INDEX IF NOT EXISTS IX_Classes_SchoolId_GradeLevel_Section " +
            "ON Classes (SchoolId, GradeLevel, Section)");
    }

    private static void TryExec(System.Data.Common.DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void TryAddColumn(System.Data.Common.DbConnection conn, string alterSql)
    {
        try
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = alterSql;
            alter.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (
            ex.SqliteErrorCode == 1
            && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // هذا هو التعارض المتوقع عند إعادة تشغيل ترقية على قاعدة سبق تحديثها.
        }
    }

    private static bool TableExists(
        System.Data.Common.DbConnection conn,
        string tableName,
        System.Data.Common.DbTransaction? transaction = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        cmd.Parameters.Add(parameter);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>قائمة كل الجداول المحفوظة (جميع مساحات العمل/المدارس)</summary>
    public async Task<IReadOnlyList<DbTimetableInfo>> ListAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimetableDbContext>();
        EnsureSchema(db);

        var schoolNames = await db.Schools
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        var rows = await db.Timetables
            .AsNoTracking()
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.SchoolId,
                t.AcademicYearStart,
                t.AcademicYearEnd,
                t.TermNumber,
                t.Status,
                LessonsCount = t.Lessons.Count,
                t.UpdatedAt,
            })
            .ToListAsync();

        var items = rows.Select(t => new DbTimetableInfo(
            t.Id,
            t.SchoolId.Value,
            t.Name,
            schoolNames.GetValueOrDefault(t.SchoolId.Value, string.Empty),
            t.AcademicYearStart,
            t.AcademicYearEnd,
            t.TermNumber,
            t.Status.ToString(),
            t.LessonsCount,
            t.UpdatedAt)).ToList();

        _logger.LogInformation("قائمة الجداول المحفوظة: {Count} جدول", items.Count);
        return items;
    }

    /// <summary>
    /// حفظ الجدول الحالي. يُستبدل أي صف بنفس (المدرسة، السنة، الفصل) لتجنّب تكرار المفتاح الفريد.
    /// </summary>
    public async Task SaveAsync(Timetable timetable)
    {
        if (timetable.SchoolId.Value == Guid.Empty)
            throw new InvalidOperationException("الجدول لا يتبع أي مدرسة (SchoolId فارغ) — أنشئه من نافذة «جدول جديد» أولاً");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimetableDbContext>();
        EnsureSchema(db);

        // ملف مستورد قد يحمل مدرسة غير موجودة محلياً: أنشئ مساحة عمل لها
        var schoolExists = await db.Schools.AnyAsync(s => s.Id == timetable.SchoolId.Value);
        if (!schoolExists)
        {
            var created = new School(timetable.Name, timetable.Name);
            db.Schools.Add(created);
            await db.SaveChangesAsync();
            timetable.ReassignSchool((SchoolId)created.Id);
            _currentSchool.SetCurrent((SchoolId)created.Id);
            _logger.LogInformation("أُنشئت مساحة عمل للجدول المستورد: {Id}", created.Id);
        }

        // حفظ مستقر: ابحث عن صف بنفس المعرّف أولاً (يُبقي معرّف الجدول ثابتاً)، وإلا بنفس المفتاح الفريد
        var row = await db.Timetables.FirstOrDefaultAsync(t => t.Id == timetable.Id);
        if (row is null)
        {
            var sameKey = await db.Timetables
                .Where(t => t.SchoolId == timetable.SchoolId
                    && t.Name == timetable.Name
                    && t.AcademicYearStart == timetable.AcademicYearStart
                    && t.AcademicYearEnd == timetable.AcademicYearEnd
                    && t.TermNumber == timetable.TermNumber)
                .ToListAsync();

            foreach (var old in sameKey)
            {
                db.Timetables.Remove(old);
                DeleteSettings(db, old.Id);
            }

            if (sameKey.Count > 0)
                await db.SaveChangesAsync();

            row = new Timetable(
                timetable.Id,
                timetable.SchoolId,
                timetable.Name,
                timetable.AcademicYearStart,
                timetable.AcademicYearEnd,
                timetable.TermNumber);
            db.Timetables.Add(row);
        }
        else
        {
            row.UpdateBasicInfo(timetable.Name, timetable.Description, timetable.TermNumber);
        }

        row.SetStatus(timetable.Status);
        row.SetEngine(timetable.Engine);
        row.SetSchoolType(timetable.SchoolType);
        row.UpdateStatistics(
            timetable.TotalLessons > 0 ? timetable.TotalLessons : timetable.Lessons.Count,
            timetable.TotalConflicts,
            timetable.QualityScore);

        // خصائص ظلية (shadow) — أعمدة FK في المخطط لا تُملأ تلقائياً بدون nav
        SetShadow(db.Entry(row), "SchoolId1", row.SchoolId.Value);

        // المرحلة 1: صف الجدول وحده حتى يتحقق مفتاحه الأجنبي قبل الحصص
        await db.SaveChangesAsync();

        // احذف حصص هذا الجدول القديمة ضمن نفس عملية الحفظ (إعادة بناء كاملة، ذرّية مع الإدراج)
        var oldLessons = await db.Lessons
            .Where(l => l.TimetableId == (TimetableId)row.Id)
            .ToListAsync();
        if (oldLessons.Count > 0)
            db.Lessons.RemoveRange(oldLessons);

        // حفظ توقيت الفترات وإعدادات المراحل في جدول الإعدادات المرافق (عبارة واحدة حتى لا يحل REPLACE عمود الآخر)
        SaveSettings(db, row.Id, timetable.PeriodTimes, timetable.StageConfigs, timetable.Assignments, timetable.Rules, timetable.QualityScore.Value);
        SaveEngine(db, row.Id, timetable.Engine);
        SaveSchoolType(db, row.Id, timetable.SchoolType);

        // المرحلة 2: بناء الحصص وإضافتها
        foreach (var lesson in timetable.Lessons)
        {
            var fresh = new Lesson(
                (Guid)lesson.Id,
                (TimetableId)row.Id,
                lesson.SubjectId,
                lesson.TeacherId,
                lesson.ClassId,
                lesson.Day,
                lesson.Period,
                lesson.Duration);
            fresh.AssignRoom(lesson.RoomId);
            fresh.SetType(lesson.Type);
            fresh.WeekNumber = lesson.WeekNumber;
            fresh.SetSortOrder(lesson.SortOrder);
            fresh.Fix(lesson.IsFixed);
            row.Lessons.Add(fresh);
            // تتبّع صريح كصف جديد — بدونه يبدأ Entry() التتبع بحالة Unchanged فيُصدر UPDATE بدل INSERT ويفشل
            db.Lessons.Add(fresh);
        }

        var trackedLessons = row.Lessons.ToList();
        foreach (var fresh in trackedLessons)
        {
            SetShadow(db.Entry(fresh), "ClassId1", fresh.ClassId.Value);
            SetShadow(db.Entry(fresh), "SubjectId1", fresh.SubjectId.Value);
            SetShadow(db.Entry(fresh), "TeacherId1", fresh.TeacherId.Value);
            SetShadow(db.Entry(fresh), "TimetableId1", fresh.TimetableId.Value);
            if (fresh.RoomId is { } roomId) SetShadow(db.Entry(fresh), "RoomId1", roomId.Value);
        }

        // ملاحظات الحصة لا تملك setters عامة → تُضبط بعد التتبع
        foreach (var pair in trackedLessons.Zip(timetable.Lessons))
        {
            if (!string.IsNullOrEmpty(pair.Second.Notes))
            {
                db.Entry(pair.First).Property("Notes").CurrentValue = pair.Second.Notes;
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dex)
        {
            // تشخيص مفصّل: قيم الظل المقروءة + وجود الصفوف المرجعية في قاعدة البيانات
            var l0 = trackedLessons.FirstOrDefault();
            if (l0 != null)
            {
                var subIds = trackedLessons.Select(l => l.SubjectId.Value).Distinct().ToList();
                var teaIds = trackedLessons.Select(l => l.TeacherId.Value).Distinct().ToList();
                var clsIds = trackedLessons.Select(l => l.ClassId.Value).Distinct().ToList();
                int subOk = await db.Subjects.CountAsync(s => subIds.Contains(s.Id));
                int teaOk = await db.Teachers.CountAsync(t => teaIds.Contains(t.Id));
                int clsOk = await db.Classes.CountAsync(c => clsIds.Contains(c.Id));
                int schoolOk = await db.Schools.CountAsync(s => s.Id == row.SchoolId.Value);
                var e0 = db.Entry(l0);
                _logger.LogError(dex,
                    "DBFK: school={SchoolOk} subj {SubOk}/{SubN} tea {TeaOk}/{TeaN} cls {ClsOk}/{ClsN} | shadows: Tim={Tim} Sub={Sub} Tea={Tea} Cls={Cls} Room={Room} | lesson: {Lsubj}/{Ltea}/{Lcls}/{Lroom} notes=[{Notes}]",
                    schoolOk, subOk, subIds.Count, teaOk, teaIds.Count, clsOk, clsIds.Count,
                    ReadShadow(e0, "TimetableId1"), ReadShadow(e0, "SubjectId1"), ReadShadow(e0, "TeacherId1"), ReadShadow(e0, "ClassId1"), ReadShadow(e0, "RoomId1"),
                    l0.SubjectId.Value, l0.TeacherId.Value, l0.ClassId.Value, l0.RoomId?.ToString() ?? "null", l0.Notes);
            }
            throw;
        }

        // حفظ تفريغ/ندب المعلمين (كان يُحفظ في الذاكرة فقط ويُفقد عند إعادة الفتح)
        await SaveAvailabilitiesAsync(db, row.Id, timetable);

        _logger.LogInformation(
            "تم حفظ الجدول في قاعدة البيانات: {Name} ({Lessons} حصة) ID={Id}",
            row.Name, row.Lessons.Count, row.Id);
    }

    /// <summary>يستبدل صفوف توفر معلمي مساحة العمل بما هو موجود في الجدول الحالي.</summary>
    private async Task SaveAvailabilitiesAsync(TimetableDbContext db, Guid timetableId, Timetable timetable)
    {
        try
        {
            var teacherIds = await db.Teachers
                .Where(t => t.SchoolId == timetable.SchoolId)
                .Select(t => t.Id)
                .ToListAsync();
            var keySet = teacherIds.ToHashSet();

            // احذف صفوف هذا الجدول (وأي صفوف قديمة بلا معرّف جدول لنفس المعلمين) ثم أدرج الجديد — ذرّياً
            var stale = (await db.TeacherAvailabilities.ToListAsync())
                .Where(a =>
                {
                    var owner = Microsoft.EntityFrameworkCore.EF.Property<Guid?>(a, "TimetableId");
                    return owner == timetableId || (owner == null && keySet.Contains(a.TeacherId.Value));
                })
                .ToList();
            if (stale.Count > 0)
                db.TeacherAvailabilities.RemoveRange(stale);

            foreach (var a in timetable.TeacherAvailabilities)
            {
                if (!keySet.Contains(a.TeacherId.Value)) continue;

                var fresh = new TeacherAvailability(a.TeacherId, a.Day, a.Period, a.IsAvailable, a.Priority);
                db.TeacherAvailabilities.Add(fresh);
                SetShadow(db.Entry(fresh), "TeacherId1", a.TeacherId.Value);
                SetShadow(db.Entry(fresh), "TimetableId", timetableId);
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // لا نُفشل حفظ الجدول بسبب التوفر، لكن نسجّل الخطأ
            _logger.LogError(ex, "تعذر حفظ تفريغ/ندب المعلمين للجدول {Id}", timetableId);
        }
    }

    /// <summary>تعيين عمود FK ظل (shadow) إن وُجد في المخطط — يتجاهل الاسم غير المعروف</summary>
    private static void SetShadow(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string name, Guid value)
    {
        try
        {
            entry.Property(name).CurrentValue = value;
        }
        catch (ArgumentException)
        {
            // العمود غير موجود في المخطط الحالي — تجاهل
        }
    }

    /// <summary>قراءة قيمة عمود ظل لغرض التشخيص — ترجع "missing" إن لم يكن العمود موجوداً</summary>
    private static string ReadShadow(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string name)
    {
        try
        {
            return entry.Property(name).CurrentValue?.ToString() ?? "null";
        }
        catch (ArgumentException)
        {
            return "missing";
        }
    }

    /// <summary>حذف صف إعدادات جدول (توقيت/مراحل/إسنادات/قواعد) — يُستدعى عند استبدال جدول أو حذفه</summary>
    private static void DeleteSettings(
        TimetableDbContext db,
        Guid timetableId,
        System.Data.Common.DbTransaction? transaction = null)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM TimetableSettings WHERE TimetableId = $id";
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
        cmd.Parameters.Add(pId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>حفظ توقيت الفترات وإعدادات المراحل معاً (UPSERT بعمود مفاتيح واحد — استبدال الصف كاملاً يمسح العمود الآخر)</summary>
    private static void SaveSettings(TimetableDbContext db, Guid timetableId, List<PeriodTime> times, List<StageScheduleConfig> configs, List<TeachingAssignment>? assignments = null, List<RuleItem>? rules = null, double qualityScore = 0)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO TimetableSettings (TimetableId, PeriodTimesJson, StageConfigsJson, AssignmentsJson, RulesJson, QualityScore) VALUES ($id, $pt, $sc, $asg, $rl, $q) " +
            "ON CONFLICT(TimetableId) DO UPDATE SET PeriodTimesJson = $pt, StageConfigsJson = $sc, AssignmentsJson = $asg, RulesJson = $rl, QualityScore = $q";
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
        var pQ = cmd.CreateParameter(); pQ.ParameterName = "$q"; pQ.Value = qualityScore;
        var pPt = cmd.CreateParameter(); pPt.ParameterName = "$pt";
        pPt.Value = (times == null || times.Count == 0) ? "" : JsonSerializer.Serialize(times, JsonOpts);
        var pSc = cmd.CreateParameter(); pSc.ParameterName = "$sc";
        pSc.Value = (configs == null || configs.Count == 0) ? "" : JsonSerializer.Serialize(configs, JsonOpts);
        var pAsg = cmd.CreateParameter(); pAsg.ParameterName = "$asg";
        pAsg.Value = (assignments == null || assignments.Count == 0) ? "" : JsonSerializer.Serialize(assignments, JsonOpts);
        var pRl = cmd.CreateParameter(); pRl.ParameterName = "$rl";
        pRl.Value = (rules == null || rules.Count == 0) ? "" : JsonSerializer.Serialize(rules, JsonOpts);
        cmd.Parameters.Add(pId); cmd.Parameters.Add(pPt); cmd.Parameters.Add(pSc); cmd.Parameters.Add(pAsg); cmd.Parameters.Add(pRl); cmd.Parameters.Add(pQ);
        cmd.ExecuteNonQuery();
    }

    private static List<PeriodTime> LoadPeriodTimes(TimetableDbContext db, Guid timetableId)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT PeriodTimesJson FROM TimetableSettings WHERE TimetableId = $id";
            var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
            cmd.Parameters.Add(pId);
            var result = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(result)) return new List<PeriodTime>();
            return JsonSerializer.Deserialize<List<PeriodTime>>(result, JsonOpts) ?? new List<PeriodTime>();
        }
        catch
        {
            return new List<PeriodTime>();
        }
    }

    /// <summary>حفظ محرك التوليد المختار لهذا الجدول</summary>
    private static void SaveEngine(TimetableDbContext db, Guid timetableId, GeneratorEngine engine)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO TimetableSettings (TimetableId, GeneratorEngine) VALUES ($id, $eng) " +
            "ON CONFLICT(TimetableId) DO UPDATE SET GeneratorEngine = $eng";
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
        var pEng = cmd.CreateParameter(); pEng.ParameterName = "$eng"; pEng.Value = (int)engine;
        cmd.Parameters.Add(pId); cmd.Parameters.Add(pEng);
        cmd.ExecuteNonQuery();
    }

    /// <summary>حفظ نوع المدرسة لهذا الجدول</summary>
    private static void SaveSchoolType(TimetableDbContext db, Guid timetableId, SchoolType type)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO TimetableSettings (TimetableId, SchoolType) VALUES ($id, $t) " +
            "ON CONFLICT(TimetableId) DO UPDATE SET SchoolType = $t";
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
        var pT = cmd.CreateParameter(); pT.ParameterName = "$t"; pT.Value = (int)type;
        cmd.Parameters.Add(pId); cmd.Parameters.Add(pT);
        cmd.ExecuteNonQuery();
    }

    private static SchoolType LoadSchoolType(TimetableDbContext db, Guid timetableId)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SchoolType FROM TimetableSettings WHERE TimetableId = $id";
            var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
            cmd.Parameters.Add(pId);
            var value = cmd.ExecuteScalar();
            return value is null or DBNull ? SchoolType.Middle : (SchoolType)Convert.ToInt32(value);
        }
        catch
        {
            return SchoolType.Middle;
        }
    }

    private static GeneratorEngine LoadEngine(TimetableDbContext db, Guid timetableId)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT GeneratorEngine FROM TimetableSettings WHERE TimetableId = $id";
            var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
            cmd.Parameters.Add(pId);
            var value = cmd.ExecuteScalar();
            return value is null or DBNull ? GeneratorEngine.CpSat : (GeneratorEngine)Convert.ToInt32(value);
        }
        catch
        {
            return GeneratorEngine.CpSat;
        }
    }

    /// <summary>درجة الجودة المحفوظة (0 = غير معروفة/مسودة)</summary>
    private static double LoadQualityScore(TimetableDbContext db, Guid timetableId)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT QualityScore FROM TimetableSettings WHERE TimetableId = $id";
            var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
            cmd.Parameters.Add(pId);
            var value = cmd.ExecuteScalar();
            return value is null or DBNull ? 0 : Convert.ToDouble(value);
        }
        catch
        {
            return 0;
        }
    }

    private static List<StageScheduleConfig> LoadStageConfigs(TimetableDbContext db, Guid timetableId)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT StageConfigsJson FROM TimetableSettings WHERE TimetableId = $id";
            var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
            cmd.Parameters.Add(pId);
            var result = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(result)) return new List<StageScheduleConfig>();
            return JsonSerializer.Deserialize<List<StageScheduleConfig>>(result, JsonOpts) ?? new List<StageScheduleConfig>();
        }
        catch
        {
            return new List<StageScheduleConfig>();
        }
    }

    private static List<TeachingAssignment> LoadAssignments(TimetableDbContext db, Guid timetableId)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT AssignmentsJson FROM TimetableSettings WHERE TimetableId = $id";
            var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
            cmd.Parameters.Add(pId);
            var result = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(result)) return new List<TeachingAssignment>();
            return JsonSerializer.Deserialize<List<TeachingAssignment>>(result, JsonOpts) ?? new List<TeachingAssignment>();
        }
        catch
        {
            return new List<TeachingAssignment>();
        }
    }

    private static List<RuleItem> LoadRules(TimetableDbContext db, Guid timetableId)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT RulesJson FROM TimetableSettings WHERE TimetableId = $id";
            var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = timetableId.ToString();
            cmd.Parameters.Add(pId);
            var result = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(result)) return new List<RuleItem>();
            return JsonSerializer.Deserialize<List<RuleItem>>(result, JsonOpts) ?? new List<RuleItem>();
        }
        catch
        {
            return new List<RuleItem>();
        }
    }

    /// <summary>فتح جدول محفوظ (مع الحصص والتوقيتات)</summary>
    public async Task<Timetable?> LoadAsync(Guid id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimetableDbContext>();
        EnsureSchema(db);

        async Task<Timetable?> QueryAsync()
            => await db.Timetables
                .AsNoTracking()
                .Include(t => t.Lessons).ThenInclude(l => l.Subject)
                .Include(t => t.Lessons).ThenInclude(l => l.Teacher)
                .Include(t => t.Lessons).ThenInclude(l => l.Class)
                .Include(t => t.Lessons).ThenInclude(l => l.Room)
                .FirstOrDefaultAsync(t => t.Id == id);

        Timetable? timetable;
        try
        {
            timetable = await QueryAsync();
        }
        catch (Exception ex) when (
            ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            // قاعدة بيانات قديمة: أعد ترقية المخطط ثم أعد المحاولة مرة واحدة
            _logger.LogWarning(ex, "مخطط قاعدة البيانات قديم — إعادة الترقية والمحاولة");
            EnsureSchema(db);
            timetable = await QueryAsync();
        }

        if (timetable == null)
        {
            _logger.LogWarning("لم يُعثر على الجدول {Id} في قاعدة البيانات", id);
            return null;
        }

        timetable.PeriodTimes = LoadPeriodTimes(db, id);
        timetable.StageConfigs = LoadStageConfigs(db, id);
        timetable.Assignments = LoadAssignments(db, id);
        timetable.Rules = LoadRules(db, id);
        timetable.SetEngine(LoadEngine(db, id));
        timetable.SetSchoolType(LoadSchoolType(db, id));

        // تفريغ/ندب المعلمين: تُحمَّل لمعلمي مساحة عمل الجدول (كانت تُفقد عند إعادة الفتح)
        var teacherKeys = await db.Teachers
            .AsNoTracking()
            .Where(t => t.SchoolId == timetable.SchoolId)
            .Select(t => t.Id)
            .ToListAsync();
        var keySet = teacherKeys.ToHashSet();
        var allAvailabilities = await db.TeacherAvailabilities.AsNoTracking().ToListAsync();
        timetable.TeacherAvailabilities.Clear();
        foreach (var availability in allAvailabilities.Where(a =>
                 {
                     var owner = Microsoft.EntityFrameworkCore.EF.Property<Guid?>(a, "TimetableId");
                     return owner == id || (owner == null && keySet.Contains(a.TeacherId.Value));
                 }))
            timetable.TeacherAvailabilities.Add(availability);

        var quality = LoadQualityScore(db, id);
        if (quality > 0)
            timetable.UpdateStatistics(timetable.TotalLessons, timetable.TotalConflicts, new Score(quality));
        _logger.LogInformation("تم فتح الجدول من قاعدة البيانات: {Name} ({Lessons} حصة)",
            timetable.Name, timetable.Lessons.Count);
        return timetable;
    }

    /// <summary>
    /// تصفير كامل لبيانات المدرسة (معلمين/مواد/فصول/قاعات/جداول/إسنادات) مع نسخة احتياطية لقاعدة البيانات أولاً.
    /// يُستخدم لبداية جديدة فعلية عند إنشاء جدول جديد.
    /// </summary>
    public Task ResetSchoolDataAsync()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AwadTimetable", "awad_timetable.db");

        if (File.Exists(dbPath))
        {
            var backup = $"{dbPath}.bak_{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(dbPath, backup, overwrite: true);
            _logger.LogWarning("نسخة احتياطية قبل التصفير: {Backup}", backup);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimetableDbContext>();
        EnsureSchema(db);

        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) conn.Open();

        using var tx = conn.BeginTransaction();
        void Exec(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        try
        {
            Exec("DELETE FROM SubjectTeacher");
            Exec("DELETE FROM Lessons");
            Exec("DELETE FROM TeacherAvailabilities");
            Exec("DELETE FROM TimetableSettings");
            Exec("DELETE FROM Timetables");
            Exec("DELETE FROM Classes");
            Exec("DELETE FROM Subjects");
            Exec("DELETE FROM Rooms");
            Exec("DELETE FROM Teachers");

            // جدول خام لا يضمن وجوده في كل قاعدة بيانات
            try { Exec("DELETE FROM SubstitutionAssignments"); } catch { /* غير موجود */ }

            tx.Commit();
            _logger.LogWarning("تم تصفير بيانات المدرسة بالكامل (بداية جديدة)");
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>حذف جدول محفوظ وبيانات مساحة عمله غير المستخدمة ضمن معاملة واحدة.</summary>
    public async Task<bool> DeleteAsync(Guid id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimetableDbContext>();
        EnsureSchema(db);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var workspaceDeleted = false;
        try
        {
            var row = await db.Timetables.FirstOrDefaultAsync(t => t.Id == id);
            if (row == null)
            {
                await transaction.CommitAsync();
                return false;
            }

            var schoolId = row.SchoolId;
            DeleteSubstitutionAssignments(db, id, transaction.GetDbTransaction());
            db.Timetables.Remove(row);
            await db.SaveChangesAsync();
            DeleteSettings(db, id, transaction.GetDbTransaction());

            // إن لم يبقَ جدول آخر في مساحة العمل، احذف بياناتها ضمن المعاملة نفسها.
            var stillUsed = await db.Timetables.AnyAsync(t => t.SchoolId == schoolId);
            if (!stillUsed)
            {
                DeleteWorkspaceData(db, schoolId, transaction.GetDbTransaction());
                workspaceDeleted = true;
            }

            await transaction.CommitAsync();
            if (workspaceDeleted && _currentSchool.Current == schoolId)
                _currentSchool.ClearCurrent();
            _logger.LogInformation("تم حذف الجدول {Id} من قاعدة البيانات", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل حذف الجدول {Id} أو تنظيف مساحة العمل؛ تم التراجع عن العملية", id);
            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception rollbackException)
            {
                _logger.LogError(rollbackException, "تعذر التراجع عن معاملة حذف الجدول {Id}", id);
            }
            throw;
        }
    }

    private static void DeleteSubstitutionAssignments(
        TimetableDbContext db,
        Guid timetableId,
        System.Data.Common.DbTransaction transaction)
    {
        var conn = db.Database.GetDbConnection();
        if (!TableExists(conn, "SubstitutionAssignments", transaction)) return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM SubstitutionAssignments WHERE upper(TimetableId) = upper($id)";
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = timetableId.ToString("D");
        cmd.Parameters.Add(parameter);
        cmd.ExecuteNonQuery();
    }

    /// <summary>حذف بيانات مساحة عمل لم تعد مستخدمة داخل معاملة الحذف الحالية.</summary>
    private static void DeleteWorkspaceData(
        TimetableDbContext db,
        SchoolId schoolId,
        System.Data.Common.DbTransaction transaction)
    {
        var conn = db.Database.GetDbConnection();
        var sid = schoolId.Value.ToString("D").ToUpperInvariant();

        void Exec(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            var p = cmd.CreateParameter();
            p.ParameterName = "$sid";
            p.Value = sid;
            cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }

        Exec("DELETE FROM TeacherAvailabilities WHERE upper(TeacherId) IN " +
             "(SELECT upper(Id) FROM Teachers WHERE upper(SchoolId) = upper($sid))");
        if (TableExists(conn, "SubjectTeacher", transaction))
            Exec("DELETE FROM SubjectTeacher WHERE upper(SubjectId) IN " +
                 "(SELECT upper(Id) FROM Subjects WHERE upper(SchoolId) = upper($sid)) " +
                 "OR upper(TeacherId) IN (SELECT upper(Id) FROM Teachers WHERE upper(SchoolId) = upper($sid))");
        if (TableExists(conn, "SubstitutionAssignments", transaction))
            Exec("DELETE FROM SubstitutionAssignments WHERE upper(TimetableId) IN " +
                 "(SELECT upper(Id) FROM Timetables WHERE upper(SchoolId) = upper($sid))");
        Exec("DELETE FROM Classes WHERE upper(SchoolId) = upper($sid)");
        Exec("DELETE FROM Subjects WHERE upper(SchoolId) = upper($sid)");
        Exec("DELETE FROM Rooms WHERE upper(SchoolId) = upper($sid)");
        Exec("DELETE FROM Teachers WHERE upper(SchoolId) = upper($sid)");
        Exec("DELETE FROM Schools WHERE upper(Id) = upper($sid)");
    }
}

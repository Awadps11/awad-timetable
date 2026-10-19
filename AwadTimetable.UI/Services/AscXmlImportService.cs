using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Data;
using AwadTimetable.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using DayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.UI.Services;

/// <summary>حصة مستوردة من ملف aSc XML (جاهزة للإضافة للجدول الحالي)</summary>
public sealed record AscPlacement(
    DayOfWeek Day,
    byte Period,
    string SubjectName,
    string TeacherName,
    string ClassName,
    string? RoomName);

/// <summary>نتيجة استيراد aSc XML</summary>
public sealed record AscImportResult(
    IReadOnlyList<AscPlacement> Placements,
    int CreatedSubjects,
    int CreatedTeachers,
    int CreatedClasses,
    int CreatedRooms,
    int Skipped);

/// <summary>
/// استيراد جدول من صيغة aSc Timetables XML (‎.xml):
/// periods / subjects / teachers / classes / lessons + cards (الحصص المجدولة).
/// يدعم ترميز windows-1256 (العربية) وUTF-8.
/// </summary>
public sealed class AscXmlImportService
{
    /// <summary>حروف الشُعب العربية بترتيبها (لا تتتالى نقاط Unicode، لذلك نستخدم جدولاً صريحاً) 1→أ، 2→ب ...</summary>
    private const string ArabicSections = "أبجدهوزحطيكلمنسعفصقرشتثخذضظغ";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSchoolService _school;

    static AscXmlImportService()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* مسجل مسبقاً */ }
    }

    public AscXmlImportService(IServiceScopeFactory scopeFactory, CurrentSchoolService school)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _school = school ?? throw new ArgumentNullException(nameof(school));
    }

    public async Task<AscImportResult> ImportAsync(string filePath, CancellationToken ct = default)
    {
        // التحليل نُقل إلى Core (AscXmlReader) ليصبح قابلاً للاختبار على ملفات aSc الحقيقية
        var parsed = AscXmlReader.ParseFile(filePath);

        var periodCount = parsed.PeriodsCount;
        var subjectsById = parsed.Subjects;
        var teachersById = parsed.Teachers;
        var classesById = parsed.Classes;
        var roomsById = parsed.Rooms;
        var lessonsById = parsed.Lessons;
        var rawCards = parsed.Cards;

        if (rawCards.Count == 0)
            throw new InvalidDataException("لا توجد بطاقات حصص مجدولة (cards) في الملف — استخدم ملف aSc يحتوي على جدول مُحل");

        // ===== حل الكيانات داخل نطاق قاعدة البيانات =====
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<Data.TimetableDbContext>();
        var subjectRepo = sp.GetRequiredService<ISubjectRepository>();
        var teacherRepo = sp.GetRequiredService<ITeacherRepository>();
        var classRepo = sp.GetRequiredService<IClassRepository>();
        var roomRepo = sp.GetRequiredService<IRoomRepository>();
        var schoolId = await _school.GetSchoolIdAsync();
        var schoolGuid = schoolId.Value;

        var subjectsByDbName = new Dictionary<string, Subject>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in await subjectRepo.GetBySchoolAsync(schoolId, ct)) subjectsByDbName[s.Name] = s;
        var teachersByDbName = new Dictionary<string, Teacher>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in await teacherRepo.GetBySchoolAsync(schoolId, ct)) teachersByDbName[t.FullName] = t;
        var classesByDbName = new Dictionary<string, Class>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in await classRepo.GetBySchoolAsync(schoolId, ct)) classesByDbName[c.Name] = c;
        var roomsByDbName = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in await roomRepo.GetBySchoolAsync(schoolId, ct)) roomsByDbName[r.Name] = r;

        var createdSubjects = 0; var createdTeachers = 0; var createdClasses = 0; var createdRooms = 0;

        // ضبط عمود الظل SchoolId1 (مفتاح أجنبي NOT NULL في المخطط) قبل الحفظ
        void SetShadowSchool(object entity) => db.Entry(entity).Property("SchoolId1").CurrentValue = schoolGuid;

        async Task<Subject> GetOrCreateSubjectAsync(string name)
        {
            if (subjectsByDbName.TryGetValue(name, out var ex)) return ex;
            var created = new Subject(schoolId, name);
            db.Subjects.Add(created);
            SetShadowSchool(created);
            await db.SaveChangesAsync(ct);
            subjectsByDbName[name] = created; createdSubjects++;
            return created;
        }
        async Task<Teacher> GetOrCreateTeacherAsync(string full, string first, string last)
        {
            // المفتاح المخزَّن هو FullName (= first + " " + last عند MiddleName فارغ) — نبحث به أولًا
            // حتى لا يتكرر المعلم عند إعادة الاستيراد عندما يختلف اسم الملف الأصلي عن FullName
            var key = $"{first} {last}".Trim();
            if (teachersByDbName.TryGetValue(key, out var ex)) return ex;
            if (full.Length > 0 && full != key && teachersByDbName.TryGetValue(full, out ex)) return ex;
            var created = new Teacher(schoolId, first, last);
            db.Teachers.Add(created);
            SetShadowSchool(created);
            await db.SaveChangesAsync(ct);
            teachersByDbName[created.FullName] = created;
            if (full.Length > 0) teachersByDbName[full] = created;
            createdTeachers++;
            return created;
        }
        async Task<Class> GetOrCreateClassAsync(string name, string? gradeAttr)
        {
            if (classesByDbName.TryGetValue(name, out var ex)) return ex;
            var (grade, section) = ParseGradeSection(name, gradeAttr);
            // إن تعارضت (المرحلة/الشعبة) مع فصل موجود نجرّب الحروف التالية حتى لا يفشل الاستيراد
            for (int i = SectionIndex(section); i <= ArabicSections.Length; i++)
            {
                Class? created = null;
                try
                {
                    created = new Class(schoolId, name, grade, SectionLetter(i));
                    db.Classes.Add(created);
                    SetShadowSchool(created);
                    await db.SaveChangesAsync(ct);
                    classesByDbName[name] = created; createdClasses++;
                    return created;
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException)
                {
                    if (created != null) db.Entry(created).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                }
            }
            throw new InvalidDataException($"تعذر إنشاء الفصل \"{name}\" — تعارض مع فصل موجود بنفس المرحلة/القسم");
        }
        async Task<Room?> GetOrCreateRoomAsync(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (roomsByDbName.TryGetValue(name, out var ex)) return ex;
            var created = new Room(schoolId, name);
            db.Rooms.Add(created);
            SetShadowSchool(created);
            await db.SaveChangesAsync(ct);
            roomsByDbName[name] = created; createdRooms++;
            return created;
        }

        var placements = new List<AscPlacement>();
        var skipped = 0;

        foreach (var (lessonId, period0, days) in rawCards)
        {
            if (!lessonsById.TryGetValue(lessonId, out var ldef)) { skipped++; continue; }
            if (subjectsById.TryGetValue(ldef.SubjectId, out var sdef) == false || string.IsNullOrEmpty(sdef.Name)) { skipped++; continue; }
            var teacherIds = SplitIds(ldef.TeacherIds);
            var classIds = SplitIds(ldef.ClassIds);
            if (teacherIds.Count == 0 || classIds.Count == 0) { skipped++; continue; }
            if (!subjectsById.ContainsKey(ldef.SubjectId)) { skipped++; continue; }

            var subject = await GetOrCreateSubjectAsync(sdef.Name);
            var teacherId0 = teacherIds[0];
            teachersById.TryGetValue(teacherId0, out var tdef);
            var tFull = tdef?.Full ?? string.Empty;
            var tFirst = tdef?.First ?? string.Empty;
            var tLast = tdef?.Last ?? string.Empty;
            var teacher = await GetOrCreateTeacherAsync(
                tFull.Length > 0 ? tFull : sdef.Name,
                tFirst.Length > 0 ? tFirst : "معلم",
                tLast.Length > 0 ? tLast : "بدون اسم");

            string? roomName = null;
            if (ldef.RoomIds != null)
            {
                var rid = SplitIds(ldef.RoomIds).FirstOrDefault();
                if (rid != null && roomsById.TryGetValue(rid, out var rn)) roomName = rn;
            }
            var room = await GetOrCreateRoomAsync(roomName);

            foreach (var classId in classIds)
            {
                if (!classesById.TryGetValue(classId, out var cdef) || string.IsNullOrEmpty(cdef.Name)) { skipped++; continue; }
                var classEnt = await GetOrCreateClassAsync(cdef.Name, cdef.Grade);

                foreach (var day in ParseDayBits(days))
                {
                    // aSc: period صفري الأساس؛ PeriodNumber يقبل 1..12 فقط
                    if (period0 < 0 || period0 > 11) { skipped++; continue; }
                    if (periodCount > 0 && period0 >= periodCount) { skipped++; continue; }
                    var period = (byte)(period0 + 1);
                    placements.Add(new AscPlacement(day, period, subject.Name, teacher.FullName, classEnt.Name, room?.Name));
                }
            }
        }

        return new AscImportResult(placements, createdSubjects, createdTeachers, createdClasses, createdRooms, skipped);
    }

    // ---------- أدوات مساعدة ----------

    private static string ReadXmlText(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        Encoding enc = Encoding.UTF8;
        if (bytes.Length >= 4 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            enc = new UTF8Encoding(true);
        }
        else
        {
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 300));
            var m = System.Text.RegularExpressions.Regex.Match(head, "encoding\\s*=\\s*[\"']([\\w-]+)[\"']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
            {
                try { enc = Encoding.GetEncoding(m.Groups[1].Value); } catch { enc = Encoding.UTF8; }
            }
        }
        return enc.GetString(bytes);
    }

    private static string? Attr(XElement el, string name) => el.Attribute(name)?.Value;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static List<string> SplitIds(string? ids) =>
        string.IsNullOrWhiteSpace(ids)
            ? new List<string>()
            : ids!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// aSc: قناع أيام بخمس خانات "10000" حيث الخانة 0 = الأحد ... 4 = الخميس.
    /// يدعم أيضاً الصيغة المفصولة بفواصل (أقنعة أسابيع): "10000,01000,..." باتحاد الأيام بلا تكرار.
    /// enums.DayOfWeek: السبت=0 الأحد=1 ... الخميس=5.
    /// </summary>
    private static IEnumerable<DayOfWeek> ParseDayBits(string daysMask)
    {
        if (string.IsNullOrWhiteSpace(daysMask)) yield break;

        var days = new List<DayOfWeek>();
        var parts = daysMask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var clean = new string(part.Where(c => c == '0' || c == '1').ToArray());
            for (int i = 0; i < clean.Length && i < 5; i++)
            {
                if (clean[i] != '1') continue;
                var day = (DayOfWeek)(i + 1);
                if (!days.Contains(day)) days.Add(day);
            }
        }

        foreach (var d in days) yield return d;
    }

    /// <summary>
    /// اشتقاق (المرحلة، القسم) من اسم الفصل مع مراعاة المرحلة قبل الرتبة،
    /// حتى لا يُلتبس "أول/ثاني/ثالث" بين الابتدائي والمتوسط والثانوي:
    /// "أول متوسط 1" → (7، أ)، "أول ثانوي1" → (10، أ)، "ثاني ثانوي 1" → (11، أ)، "ثالث ثانوي 1" → (12، أ).
    /// </summary>
    private static (int Grade, char Section) ParseGradeSection(string className, string? gradeAttr)
    {
        var name = className ?? string.Empty;
        int grade = 0;

        // 1) سمة الصف الصريحة (aSc grade) إن كانت رقماً صحيحاً 1..12
        var attr = NullIfEmpty(gradeAttr);
        if (attr != null && int.TryParse(attr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gAttr) && gAttr is >= 1 and <= 12)
            grade = gAttr;

        // 2) الاشتقاق من الاسم
        if (grade == 0)
        {
            // الصفات المطلقة (عاشر/حادي عشر/ثاني عشر) تُفحص أولاً لتفادي مطابقة "ثاني" داخل "ثاني عشر"
            (string Word, int G)[] absolute = { ("ثاني عشر", 12), ("حادي عشر", 11), ("عاشر", 10) };
            foreach (var (w, g) in absolute)
            {
                if (name.Contains(w, StringComparison.OrdinalIgnoreCase)) { grade = g; break; }
            }

            if (grade == 0)
            {
                // أساس المرحلة: ابتدائي=1، متوسط=7، ثانوي=10
                var stageBase = name.Contains("ثانوي", StringComparison.OrdinalIgnoreCase) ? 10
                    : name.Contains("متوسط", StringComparison.OrdinalIgnoreCase) ? 7
                    : 1;

                (string Word, int Rank)[] ranks =
                {
                    ("أول", 1), ("ثاني", 2), ("ثالث", 3),
                    ("رابع", 4), ("خامس", 5), ("سادس", 6),
                    ("سابع", 7), ("ثامن", 8), ("تاسع", 9)
                };
                foreach (var (w, r) in ranks)
                {
                    if (!name.Contains(w, StringComparison.OrdinalIgnoreCase)) continue;
                    grade = stageBase == 1 ? r : stageBase + r - 1;
                    break;
                }

                // مرحلة معروفة بلا رتبة صريحة (مثل "انتظار ثانوي") → أساس المرحلة
                if (grade == 0 && stageBase > 1) grade = stageBase;
            }
        }

        // 3) احتياطي أخير: أول رقم داخل الاسم
        if (grade == 0)
        {
            var digitsOnly = new string(name.Where(char.IsAsciiDigit).ToArray());
            if (int.TryParse(digitsOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gDig) && gDig is >= 1 and <= 12)
                grade = gDig;
        }

        if (grade is < 1 or > 12) grade = 1;

        // القسم: رقم في نهاية الاسم (1→أ، 2→ب) أو حرف شعبة منفرد في آخر الاسم
        var section = 'أ';
        var j = name.Length - 1;
        while (j >= 0 && char.IsAsciiDigit(name[j])) j--;
        var tailDigits = name.Substring(j + 1);
        if (int.TryParse(tailDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var trailing) && trailing is >= 1 and <= 26)
        {
            section = SectionLetter(trailing);
        }
        else
        {
            var lastToken = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (lastToken is { Length: 1 } && ArabicSections.Contains(lastToken[0]))
                section = lastToken[0];
        }
        return (grade, section);
    }

    /// <summary>حرف الشعبة المقابل لرقم 1..26 (1→أ، 2→ب ...)</summary>
    private static char SectionLetter(int index)
    {
        if (index < 1) index = 1;
        if (index > ArabicSections.Length) index = ArabicSections.Length;
        return ArabicSections[index - 1];
    }

    /// <summary>رقم الشعبة المقابل لحرف (أ→1، ب→2 ...) وإلا 1</summary>
    private static int SectionIndex(char section)
    {
        var idx = ArabicSections.IndexOf(section);
        return idx >= 0 ? idx + 1 : 1;
    }
}

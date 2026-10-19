using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Globalization;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Solver;

/// <summary>
/// مُصدِّر صيغة aSc Timetables XML (الإصدار 2027.7.1)، بنفس بنية الملف الذي صدّره البرنامج فعلياً:
/// ترميز windows-1256، معرّفات سداسية، periods، daysdefs، lessons، cards.
/// </summary>
public static class AscXmlFormat
{
    private const string EncodingName = "windows-1256";

    /// <summary>ترتيب أيام aSc في قناع daysdef (الموضع 0 = الأحد)</summary>
    private static readonly DomainDayOfWeek[] MaskOrder =
    {
        DomainDayOfWeek.Sunday,
        DomainDayOfWeek.Monday,
        DomainDayOfWeek.Tuesday,
        DomainDayOfWeek.Wednesday,
        DomainDayOfWeek.Thursday,
    };

    public static void Export(Timetable timetable, string path)
    {
        ArgumentNullException.ThrowIfNull(timetable);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var doc = Build(timetable);

        // .NET لا يحمل صفحات الترميز الويندوزية افتراضياً؛ نسجّل مزوّدها قبل طلب windows-1256
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(EncodingName);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, encoding);
        writer.Write(doc.Declaration?.ToString() ?? $"<?xml version=\"1.0\" encoding=\"{EncodingName}\"?>");
        writer.WriteLine();
        writer.Write(doc.Root!.ToString(SaveOptions.None));
    }

    public static XDocument Build(Timetable timetable)
    {
        var root = new XElement("timetable",
            new XAttribute("ascttversion", "2027.7.1"),
            new XAttribute("importtype", "database"),
            new XAttribute("options", "export:idprefix:%CHRID,import:idprefix:%TEMPID"),
            new XAttribute("defaultexport", "1"));

        var lessons = timetable.Lessons.ToList();

        // المعرّفات: aSc يستخدم نصاً سداسياً؛ نشتقه من GUID ثابت لكل كيان
        string Hex(Guid id) => id.ToString("N").Substring(0, 16).ToUpperInvariant();

        var classes = lessons.Where(l => l.Class is not null).Select(l => l.Class!).DistinctBy(c => c.Id).ToList();
        var teachers = lessons.Where(l => l.Teacher is not null).Select(l => l.Teacher!).DistinctBy(t => t.Id).ToList();
        var subjects = lessons.Where(l => l.Subject is not null).Select(l => l.Subject!).DistinctBy(s => s.Id).ToList();
        var rooms = lessons.Where(l => l.Room is not null).Select(l => l.Room!).DistinctBy(r => r.Id).ToList();

        // الفترات: فهرس aSc يبدأ من 0 (الحصة الأولى = 0)
        var periods = new XElement("periods",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "period,name,short,starttime,endtime"));
        var maxPeriod = Math.Max(1, lessons.Count == 0 ? 1 : lessons.Max(l => (int)l.Period.Value));
        for (var p = 1; p <= maxPeriod; p++)
        {
            periods.Add(new XElement("period",
                new XAttribute("name", p.ToString()),
                new XAttribute("short", p.ToString()),
                new XAttribute("period", (p - 1).ToString()),
                new XAttribute("starttime", "08:00"),
                new XAttribute("endtime", "08:45")));
        }
        root.Add(periods);

        // الاستراحات: كل فترة مُعلَّمة IsBreak تُكتب كاستراحة
        var breaksEl = new XElement("breaks",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "break,name,short,starttime,endtime"));
        var breakTimes = timetable.PeriodTimes.Where(p => p.IsBreak).OrderBy(p => p.Period).ToList();
        foreach (var b in breakTimes)
        {
            breaksEl.Add(new XElement("break",
                new XAttribute("name", $"استراحة {b.Period}"),
                new XAttribute("short", $"B{b.Period}"),
                new XAttribute("starttime", PeriodTime.FormatMinutes(b.StartMinutes)),
                new XAttribute("endtime", PeriodTime.FormatMinutes(b.EndMinutes))));
        }
        root.Add(breaksEl);

        // أيام العمل: تعريف واحد لكل يوم مستخدم + تعريف للكل
        var usedDays = lessons.Select(l => l.Day).Distinct().ToHashSet();
        var activeMasks = MaskOrder.Where(d => usedDays.Contains(d) || usedDays.Count == 0).ToList();
        if (activeMasks.Count == 0) activeMasks = MaskOrder.ToList();

        var daysdefs = new XElement("daysdefs",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "id,days,name,short"));
        var allMask = string.Join(",", activeMasks.Select(d => MaskFor(d)));
        var allId = Hex(Guid.Parse("00000000-0000-0000-0000-00000000A11D"));
        daysdefs.Add(new XElement("daysdef",
            new XAttribute("id", allId),
            new XAttribute("name", "أيام العمل"),
            new XAttribute("short", "X"),
            new XAttribute("days", allMask)));
        foreach (var d in activeMasks)
        {
            daysdefs.Add(new XElement("daysdef",
                new XAttribute("id", Hex(Guid.Parse("00000000-0000-0000-0000-" + ((int)d).ToString("D12")))),
                new XAttribute("name", DayName(d)),
                new XAttribute("short", DayName(d)),
                new XAttribute("days", MaskFor(d))));
        }
        root.Add(daysdefs);

        root.Add(new XElement("weeksdefs",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "id,weeks,name,short"),
            new XElement("weeksdef",
                new XAttribute("id", "5070C25AC1777FEB"),
                new XAttribute("name", "كل الأسابيع"),
                new XAttribute("short", "كل"),
                new XAttribute("weeks", "1"))));

        root.Add(new XElement("termsdefs",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "id,terms,name,short"),
            new XElement("termsdef",
                new XAttribute("id", "DBB971A2C8E6A5A9"),
                new XAttribute("name", "الفصل الدراسي"),
                new XAttribute("short", "الفصل"),
                new XAttribute("terms", "1"))));

        var subjectsEl = new XElement("subjects",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "id,name,short,partner_id"));
        foreach (var s in subjects)
        {
            subjectsEl.Add(new XElement("subject",
                new XAttribute("id", Hex(s.Id)),
                new XAttribute("name", s.Name),
                new XAttribute("short", s.ShortName ?? s.Name),
                new XAttribute("partner_id", "")));
        }
        root.Add(subjectsEl);

        var teachersEl = new XElement("teachers",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "id,name,short,gender,color,email,mobile,partner_id,firstname,lastname"));
        foreach (var t in teachers)
        {
            teachersEl.Add(new XElement("teacher",
                new XAttribute("id", Hex(t.Id)),
                new XAttribute("firstname", t.FirstName),
                new XAttribute("lastname", t.LastName),
                new XAttribute("name", t.FullName),
                new XAttribute("short", t.ShortName),
                new XAttribute("gender", "M"),
                new XAttribute("color", "#00FF00"),
                new XAttribute("email", ""),
                new XAttribute("mobile", ""),
                new XAttribute("partner_id", "")));
        }
        root.Add(teachersEl);

        var classesEl = new XElement("classes",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "id,name,short,classroomids,teacherid,grade,partner_id"));
        foreach (var c in classes)
        {
            classesEl.Add(new XElement("class",
                new XAttribute("id", Hex(c.Id)),
                new XAttribute("name", c.Name),
                new XAttribute("short", c.ShortName ?? c.Name),
                new XAttribute("teacherid", ""),
                new XAttribute("classroomids", ""),
                new XAttribute("grade", ""),
                new XAttribute("partner_id", "")));
        }
        root.Add(classesEl);

        // المباني: قائمة مستقلة، والقاعات تُشير إليها
        var buildings = rooms
            .Select(r => r.Building)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b, StringComparer.Ordinal)
            .ToList();
        var buildingIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var buildingsEl = new XElement("buildings",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "id,name,partner_id"));
        var buildingOrdinal = 0;
        foreach (var b in buildings)
        {
            buildingOrdinal++;
            var id = Hex(Guid.Parse("00000000-0000-0000-0000-" + buildingOrdinal.ToString("D12")));
            buildingIdByName[b] = id;
            buildingsEl.Add(new XElement("building",
                new XAttribute("id", id),
                new XAttribute("name", b),
                new XAttribute("partner_id", "")));
        }
        root.Add(buildingsEl);

        var roomsEl = new XElement("classrooms",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "id,name,short,capacity,buildingid,partner_id"));
        foreach (var r in rooms)
        {
            var buildingId = !string.IsNullOrWhiteSpace(r.Building) && buildingIdByName.TryGetValue(r.Building!.Trim(), out var bid)
                ? bid
                : "";
            roomsEl.Add(new XElement("classroom",
                new XAttribute("id", Hex(r.Id)),
                new XAttribute("name", r.Name),
                new XAttribute("short", r.ShortName ?? r.Name),
                new XAttribute("capacity", r.Capacity.ToString()),
                new XAttribute("buildingid", buildingId),
                new XAttribute("partner_id", "")));
        }
        root.Add(roomsEl);

        // الحصص (lessons) = طلب الحصص لكل (فصل، مادة، معلم)، والبطاقات (cards) = التوزيع الفعلي
        var lessonsEl = new XElement("lessons",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "id,subjectid,classids,groupids,teacherids,classroomids,periodspercard,periodsperweek,daysdefid,weeksdefid,termsdefid,seminargroup,capacity,partner_id"));
        var cardsEl = new XElement("cards",
            new XAttribute("options", "canadd,export:silent"),
            new XAttribute("columns", "lessonid,period,days,weeks,terms,classroomids"));

        var groups = lessons.GroupBy(l => (l.ClassId, l.SubjectId, l.TeacherId)).ToList();
        foreach (var g in groups)
        {
            var first = g.First();
            var lessonId = Hex(DeriveId(first.ClassId.Value, first.SubjectId.Value, first.TeacherId.Value));

            lessonsEl.Add(new XElement("lesson",
                new XAttribute("id", lessonId),
                new XAttribute("subjectid", Hex(first.SubjectId.Value)),
                new XAttribute("classids", Hex(first.ClassId.Value)),
                new XAttribute("groupids", ""),
                new XAttribute("teacherids", Hex(first.TeacherId.Value)),
                new XAttribute("classroomids", ""),
                new XAttribute("periodspercard", "1"),
                new XAttribute("periodsperweek", g.Count().ToString(CultureInfo.InvariantCulture) + ".0"),
                new XAttribute("daysdefid", allId),
                new XAttribute("weeksdefid", "5070C25AC1777FEB"),
                new XAttribute("termsdefid", "DBB971A2C8E6A5A9"),
                new XAttribute("seminargroup", ""),
                new XAttribute("capacity", "*"),
                new XAttribute("partner_id", "")));

            foreach (var lesson in g)
            {
                cardsEl.Add(new XElement("card",
                    new XAttribute("lessonid", lessonId),
                    new XAttribute("classroomids", lesson.RoomId is { } rid && rid != RoomId.Empty ? Hex(rid.Value) : ""),
                    new XAttribute("period", ((int)lesson.Period.Value - 1).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("weeks", "1"),
                    new XAttribute("terms", "1"),
                    new XAttribute("days", MaskFor(lesson.Day))));
            }
        }

        root.Add(lessonsEl);
        root.Add(cardsEl);

        return new XDocument(new XDeclaration("1.0", EncodingName, null), root);
    }

    /// <summary>معرّف حتمي لحصة مجمّعة من (فصل، مادة، معلم) لا يتغير بين التصديرات</summary>
    private static Guid DeriveId(Guid classId, Guid subjectId, Guid teacherId)
    {
        var bytes = new byte[16];
        var a = classId.ToByteArray();
        var b = subjectId.ToByteArray();
        var c = teacherId.ToByteArray();
        for (var i = 0; i < 16; i++) bytes[i] = (byte)(a[i] ^ b[i] ^ c[i]);
        return new Guid(bytes);
    }

    /// <summary>قناع يوم واحد: 1 في موضع اليوم وصفر في البقية (الموضع 0 = الأحد)</summary>
    public static string MaskFor(DomainDayOfWeek day)
    {
        var chars = new char[MaskOrder.Length];
        for (var i = 0; i < chars.Length; i++) chars[i] = '0';

        var index = Array.IndexOf(MaskOrder, day);
        if (index >= 0) chars[index] = '1';

        return new string(chars);
    }

    private static string DayName(DomainDayOfWeek day) => day switch
    {
        DomainDayOfWeek.Sunday => "الأحد",
        DomainDayOfWeek.Monday => "الإثنين",
        DomainDayOfWeek.Tuesday => "الثلاثاء",
        DomainDayOfWeek.Wednesday => "الأربعاء",
        DomainDayOfWeek.Thursday => "الخميس",
        _ => day.ToString(),
    };
}

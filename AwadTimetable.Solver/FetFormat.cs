using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Solver;

/// <summary>
/// بناء ملف .fet (XML) من الجدول. البنية مأخوذة من أمثلة FET الرسمية (الإصدار 7.12.0):
/// Days_List, Hours_List, Teachers_List, Subjects_List, Students_List, Activities_List، والقيود الأساسية.
/// </summary>
public static class FetExporter
{
    private const string FetVersion = "7.12.0";

    /// <summary>
    /// ترتيب أيام FET من أيام العمل الفعلية للجدول. أسماء الأيام فريدة وتُستخدم كمعرّفات.
    /// </summary>
    public static XDocument Build(Timetable timetable)
    {
        var days = ActiveDays(timetable);
        var hours = ActiveHours(timetable);

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null));
        var root = new XElement("fet", new XAttribute("version", FetVersion));

        root.Add(new XElement("Institution_Name", timetable.Name));
        root.Add(new XElement("Comments", timetable.Description ?? string.Empty));

        root.Add(new XElement("Days_List",
            new XElement("Number_of_Days", days.Count),
            days.Select(d => new XElement("Day", new XElement("Name", DayName(d))))));

        root.Add(new XElement("Hours_List",
            new XElement("Number_of_Hours", hours.Count),
            hours.Select(h => new XElement("Hour", new XElement("Name", HourName(h))))));

        // الأسماء هنا تُكتب كما هي؛ FET يقبل النصوص العربية في الوسوم
        var teachers = timetable.Lessons.Select(l => l.Teacher?.FullName ?? l.TeacherId.ToString())
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        root.Add(new XElement("Teachers_List",
            teachers.Select(t => new XElement("Teacher",
                new XElement("Name", t),
                new XElement("Comments", string.Empty)))));

        var subjects = timetable.Lessons.Select(l => l.Subject?.Name ?? l.SubjectId.ToString())
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        root.Add(new XElement("Subjects_List",
            subjects.Select(s => new XElement("Subject",
                new XElement("Name", s),
                new XElement("Comments", string.Empty)))));

        // كل فصل = Year واحد + Group واحد + Subgroup واحد.
        // FET يشترط أن تكون أسماء المجموعات فريدة عبر كل الأعوام، فنضيف بادئة للمجموعة والمجموعة الفرعية
        // (السبب: خطأ FET «Trying to add group ..., which is already added as another year»).
        var classes = timetable.Lessons.Select(l => l.Class?.Name ?? l.ClassId.ToString())
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        root.Add(new XElement("Students_List",
            classes.Select(c => new XElement("Year",
                new XElement("Name", c),
                new XElement("Number_of_Students", 0),
                new XElement("Comments", string.Empty),
                new XElement("Group",
                    new XElement("Name", GroupName(c)),
                    new XElement("Number_of_Students", 0),
                    new XElement("Comments", string.Empty),
                    new XElement("Subgroup",
                        new XElement("Name", SubgroupName(c)),
                        new XElement("Number_of_Students", 0),
                        new XElement("Comments", string.Empty)))))));

        // نشاط لكل حصة: Id = فهرس الحصة (1..n) وهو مفتاح الربط عند القراءة
        var activities = new XElement("Activities_List");
        var spaceConstraints = new XElement("Space_Constraints_List",
            new XElement("ConstraintBasicCompulsorySpace",
                new XElement("Weight_Percentage", 100),
                new XElement("Active", "true"),
                new XElement("Comments", string.Empty)));
        var index = 0;
        foreach (var lesson in timetable.Lessons)
        {
            index++;
            activities.Add(new XElement("Activity",
                new XElement("Teacher", lesson.Teacher?.FullName ?? lesson.TeacherId.ToString()),
                new XElement("Subject", lesson.Subject?.Name ?? lesson.SubjectId.ToString()),
                new XElement("Students", GroupName(lesson.Class?.Name ?? lesson.ClassId.ToString())),
                new XElement("Duration", 1),
                new XElement("Total_Duration", 1),
                new XElement("Id", index),
                new XElement("Activity_Group_Id", 0),
                new XElement("Active", "true"),
                new XElement("Comments", string.Empty)));

            // قاعة الحصة (إن حُدّدت) تُصدَّر كقيد «قاعة مفضّلة» بنفس معرّف النشاط
            var roomName = lesson.Room?.Name;
            if (!string.IsNullOrWhiteSpace(roomName))
            {
                spaceConstraints.Add(new XElement("ConstraintActivityPreferredRoom",
                    new XElement("Weight_Percentage", 100),
                    new XElement("Activity_Id", index),
                    new XElement("Room", roomName),
                    new XElement("Permanently_Locked", "false"),
                    new XElement("Active", "true"),
                    new XElement("Comments", string.Empty)));
            }
        }
        root.Add(activities);

        // قائمة القاعات المستخدمة فعلاً (FET يشترط تعريفها قبل القيود)
        var rooms = timetable.Lessons
            .Where(l => l.Room is not null && !string.IsNullOrWhiteSpace(l.Room!.Name))
            .Select(l => l.Room!)
            .DistinctBy(r => (Guid)r.Id)
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        root.Add(new XElement("Buildings_List"));
        root.Add(new XElement("Rooms_List", rooms.Select(r => new XElement("Room",
            new XElement("Name", r.Name),
            new XElement("Building", string.Empty),
            new XElement("Capacity", Math.Max(1, r.Capacity)),
            new XElement("Virtual", "false"),
            new XElement("Comments", string.Empty)))));
        root.Add(new XElement("Activity_Tags_List"));

        root.Add(new XElement("Time_Constraints_List",
            new XElement("ConstraintBasicCompulsoryTime",
                new XElement("Weight_Percentage", 100),
                new XElement("Active", "true"),
                new XElement("Comments", string.Empty))));

        root.Add(spaceConstraints);

        doc.Add(root);
        return doc;
    }

    public static IReadOnlyList<DomainDayOfWeek> ActiveDays(Timetable timetable)
    {
        var all = Enum.GetValues<DomainDayOfWeek>();
        var used = timetable.Lessons.Select(l => l.Day).Distinct().ToHashSet();
        var days = all.Where(d => d != DomainDayOfWeek.Friday || used.Contains(d)).ToList();
        return days.Count == 0 ? all.ToList() : days;
    }

    /// <summary>الفترات المستخدمة فعلاً في الحصص (فترات الاستراحة لا تظهر لأنها بلا حصص)</summary>
    public static IReadOnlyList<int> ActiveHours(Timetable timetable)
    {
        var used = timetable.Lessons.Select(l => (int)l.Period.Value).Distinct().OrderBy(p => p).ToList();
        return used.Count > 0 ? used : new List<int> { 1 };
    }

    public static string DayName(DomainDayOfWeek day) => day.ToString();

    /// <summary>اسم المجموعة الطلابية للفصل (فريد عن اسم العام)</summary>
    public static string GroupName(string className) => "G-" + className;

    /// <summary>اسم المجموعة الفرعية للفصل (فريد عن كل الأسماء الأخرى)</summary>
    public static string SubgroupName(string className) => "S-" + className;

    public static string HourName(int period) => $"P{period}";
}

/// <summary>
/// يقرأ نتيجة FET (ملف *_activities.xml) ويحوّلها إلى (يوم، فترة) لكل معرّف حصة.
/// </summary>
public static class FetResultReader
{
    /// <summary>يعيد: معرّف النشاط → (اليوم، رقم الفترة). الفارغ يعني أن FET لم يجدول النشاط.</summary>
    public static IReadOnlyDictionary<int, (DomainDayOfWeek day, int period)> ReadActivities(string outputDir, string timetableName)
    {
        var result = new Dictionary<int, (DomainDayOfWeek day, int period)>();

        var file = Directory.EnumerateFiles(outputDir, "*_activities.xml", SearchOption.AllDirectories).FirstOrDefault();
        if (file is null) return result;

        var doc = XDocument.Load(file);
        foreach (var activity in doc.Descendants("Activity"))
        {
            var idText = activity.Element("Id")?.Value;
            var day = activity.Element("Day")?.Value;
            var hour = activity.Element("Hour")?.Value;
            if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            if (string.IsNullOrEmpty(day) || string.IsNullOrEmpty(hour)) continue;

            if (!Enum.TryParse<DomainDayOfWeek>(day, ignoreCase: true, out var dayValue)) continue;
            if (!TryParsePeriod(hour, out var period)) continue;

            result[id] = (dayValue, period);
        }

        return result;
    }

    private static bool TryParsePeriod(string hour, out int period)
    {
        // أسماء الساعات نكتبها P<n> في التصدير؛ FET يعيدها كما كُتبت
        var digits = new string(hour.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out period) && period > 0;
    }
}

using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Tests;

/// <summary>
/// بناء جداول صغيرة واقعية للاختبار:
/// كل الصفوف تدرس نفس المواد بنفس المعلمين (مثل المدرسة الحقيقية)，
/// لذلك تنشأ تضاربات معلمين حقيقية يجب على المحلّال حلّها.
/// </summary>
internal static class SampleTimetable
{
    public static Timetable Create(int classCount, int subjectCount, int gradeLevel = 1)
    {
        var school = SchoolId.New();
        var timetable = new Timetable(school, "جدول اختباري", 2026, 2027, 1);

        var subjects = new List<Subject>();
        var teachers = new List<Teacher>();

        for (var j = 0; j < subjectCount; j++)
        {
            subjects.Add(new Subject(school, $"مادة{j + 1}"));
            teachers.Add(new Teacher(school, $"معلم{j + 1}", "اختباري"));
        }

        for (var i = 0; i < classCount; i++)
        {
            var cls = new Class(school, $"ك{i + 1}", gradeLevel, 'أ');
            timetable.ClassGradeLevels[cls.Id] = gradeLevel;

            for (var j = 0; j < subjectCount; j++)
            {
                var lesson = new Lesson(
                    (TimetableId)timetable.Id,
                    (SubjectId)subjects[j].Id,
                    (TeacherId)teachers[j].Id,
                    (ClassId)cls.Id,
                    DomainDayOfWeek.Saturday,
                    PeriodNumber.Create(1));

                // نفس ما تفعله الواجهة: ربط الكيانات حتى تعمل القيود وأسماء العرض
                lesson.AttachReferences(subjects[j], teachers[j], cls);
                timetable.AddLesson(lesson);
            }
        }

        return timetable;
    }

    /// <summary>توقيت فترات عام (اليوم -1 = كل الأيام) لعدد محدد من الفترات</summary>
    public static List<PeriodTime> GeneralPeriodTimes(int fromPeriod, int toPeriod, int startMinutes = 8 * 60)
    {
        var list = new List<PeriodTime>();

        for (var p = fromPeriod; p <= toPeriod; p++)
        {
            list.Add(new PeriodTime
            {
                Period = (byte)p,
                StartMinutes = startMinutes + (p - fromPeriod) * 50,
                EndMinutes = startMinutes + (p - fromPeriod) * 50 + 45,
                Day = -1,
                Stage = null
            });
        }

        return list;
    }
}

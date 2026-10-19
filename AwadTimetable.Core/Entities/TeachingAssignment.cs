using System;

namespace AwadTimetable.Core.Entities;

/// <summary>
/// إسناد تدريسي مركزي: (معلم + مادة + فصل) بعدد حصص أسبوعية.
/// وحدة الإسناد هي العلاقة الثلاثية؛ قد يدرّس المعلم المادة نفسها في أكثر من فصل،
/// أو أكثر من مادة لنفس الفصل. القاعة ثانوية ولا تُحدَّد هنا.
/// تُحفظ مع ملف الجدول (غير مخزّنة في مخطط قاعدة البيانات).
/// </summary>
public class TeachingAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeacherId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid ClassId { get; set; }

    /// <summary>عدد الحصص الأسبوعية المطلوبة لهذا الإسناد.</summary>
    public int WeeklyPeriods { get; set; } = 1;

    /// <summary>هل هذا الإسناد مُفعَّل؟ الإسناد المعطَّل يبقى محفوظًا لكنه لا يولّد حصصًا.</summary>
    public bool IsEnabled { get; set; } = true;

    public string? Notes { get; set; }
}

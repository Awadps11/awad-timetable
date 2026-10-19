using System.Collections.Generic;

namespace AwadTimetable.UI.ViewModels;

/// <summary>نتيجة تشخيص حصة غير مُوضوعة: المواضع المناسبة وأسباب الرفض مرتبة حسب التكرار.</summary>
public sealed class LessonDiagnosis
{
    public string LessonTitle { get; set; } = string.Empty;

    /// <summary>ملخص قصير يُعرض أعلى التشخيص.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>عدد المواضع المناسبة (خلية فارغة بلا تعارض).</summary>
    public int FreeSlotsCount { get; set; }

    /// <summary>المواضع المناسبة نفسها بصيغة «يوم الفترة N».</summary>
    public List<string> GoodSlots { get; set; } = new();

    /// <summary>أسباب الرفض وعدد المواضع التي تنطبق عليها، مرتبة تنازليًا.</summary>
    public List<KeyValuePair<string, int>> ReasonCounts { get; set; } = new();
}

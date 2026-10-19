using System;
using System.Collections.Generic;
using AwadTimetable.Core.Enums;

namespace AwadTimetable.Core.Services;

/// <summary>
/// كتالوج محركات التوليد: الاسم والوصف وحالة التفعيل لكل محرك يعرضه مربع «جدول جديد»
/// </summary>
public static class GeneratorEngineCatalog
{
    public sealed record EngineInfo(GeneratorEngine Engine, string DisplayName, string Description, bool IsAvailable);

    public static IReadOnlyList<EngineInfo> All { get; } =
    [
        new EngineInfo(
            GeneratorEngine.CpSat,
            "CP-SAT (افتراضي)",
            "محرك برمجة القيود (OR-Tools). يضمن القيود الصلبة ويحسّن القيود اللينة ضمن المهلة.",
            IsAvailable: true),

        new EngineInfo(
            GeneratorEngine.Fet,
            "FET (خارجي)",
            "تشغيل برنامج FET 7.12 عبر سطر الأوامر (fet-cl). يتطلب تثبيت FET وتحديد مساره في الإعدادات.",
            IsAvailable: true),

        new EngineInfo(
            GeneratorEngine.Asc,
            "aSc Timetables (بالتعاون)",
            "يُصدَّر الجدول إلى aSc، فتولّده داخل واجهته ثم تعود بالنتيجة لاستيرادها. التوليد نفسه يتم داخل aSc ولا يعمل آلياً.",
            IsAvailable: true),
    ];

    public static EngineInfo Get(GeneratorEngine engine)
    {
        foreach (var info in All)
        {
            if (info.Engine == engine) return info;
        }

        throw new ArgumentOutOfRangeException(nameof(engine), engine, "محرك توليد غير معروف");
    }
}

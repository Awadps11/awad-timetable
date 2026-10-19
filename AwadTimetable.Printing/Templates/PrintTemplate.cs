using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwadTimetable.Printing.Services;

namespace AwadTimetable.Printing.Templates;

public enum PrintCellStyle : byte { Filled, Outlined, Minimal }

/// <summary>
/// قالب طباعة قابل للتخصيص: ألوان، خط، حجم صفحة، ومكوّنات العرض.
/// القوالب الجاهزة تُقرأ فقط، ويمكن للمستخدم نسخها وتعديلها وحفظها.
/// </summary>
public sealed class PrintTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "قالب جديد";
    public bool IsBuiltIn { get; set; }

    public PrintPageSize PageSize { get; set; } = PrintPageSize.A4;
    public PrintOrientation Orientation { get; set; } = PrintOrientation.Landscape;

    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 10;

    public string HeaderColor { get; set; } = "#4B6BAC";
    public string DayColor { get; set; } = "#3D5990";
    public string RowColor1 { get; set; } = "#FFFFFF";
    public string RowColor2 { get; set; } = "#F3F7FF";
    public string TextColor { get; set; } = "#112233";
    public string AccentColor { get; set; } = "#4B6BAC";

    public PrintCellStyle CellStyle { get; set; } = PrintCellStyle.Filled;

    public bool ShowPeriodTimes { get; set; } = true;
    public bool ShowTeacher { get; set; } = true;
    public bool ShowRoom { get; set; } = true;
    public bool ShowClass { get; set; } = true;
    public bool ShowLegend { get; set; } = true;
    public bool ShowSummary { get; set; } = true;
    public bool ShowStudents { get; set; } = true;
    public bool Zebra { get; set; } = true;
    public bool ShowGridLines { get; set; } = true;

    public string WatermarkText { get; set; } = string.Empty;
    public string FooterText { get; set; } = string.Empty;

    /// <summary>مسار شعار المدرسة المطبوع في الترويسة (اختياري)</summary>
    public string LogoPath { get; set; } = string.Empty;

    /// <summary>دمج الحصص الفردية المتتالية المتماثلة في الطباعة فقط (بأسلوب aSc)</summary>
    public bool MergeConsecutive { get; set; }

    public PrintTemplate Clone()
    {
        var copy = (PrintTemplate)MemberwiseClone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.IsBuiltIn = false;
        return copy;
    }
}

/// <summary>القوالب الجاهزة: أنماط aSc، حديث، أنيق، مضغوط، فارغ، وبطاقات.</summary>
public static class PrintTemplatePresets
{
    public static PrintTemplate AscClassic() => new()
    {
        Id = "builtin-asc",
        Name = "aSc كلاسيكي",
        IsBuiltIn = true,
        HeaderColor = "#4B6BAC",
        DayColor = "#3D5990",
        RowColor1 = "#E9F7FF",
        RowColor2 = "#D3E8FC",
        TextColor = "#112233",
        AccentColor = "#4B6BAC",
        CellStyle = PrintCellStyle.Filled,
        ShowLegend = true,
        ShowSummary = true,
    };

    public static PrintTemplate ModernMinimal() => new()
    {
        Id = "builtin-minimal",
        Name = "حديث بسيط",
        IsBuiltIn = true,
        HeaderColor = "#FFFFFF",
        DayColor = "#F1F3F5",
        RowColor1 = "#FFFFFF",
        RowColor2 = "#FAFBFC",
        TextColor = "#1F2933",
        AccentColor = "#2563EB",
        CellStyle = PrintCellStyle.Minimal,
        Zebra = false,
        ShowGridLines = true,
        ShowLegend = false,
        ShowSummary = false,
    };

    public static PrintTemplate ElegantGradient() => new()
    {
        Id = "builtin-elegant",
        Name = "أنيق متدرّج",
        IsBuiltIn = true,
        HeaderColor = "#6D28D9",
        DayColor = "#7C3AED",
        RowColor1 = "#F5F3FF",
        RowColor2 = "#EDE9FE",
        TextColor = "#1E1B4B",
        AccentColor = "#7C3AED",
        CellStyle = PrintCellStyle.Filled,
        ShowSummary = true,
    };

    public static PrintTemplate CompactA3() => new()
    {
        Id = "builtin-compact",
        Name = "مضغوط (A3)",
        IsBuiltIn = true,
        PageSize = PrintPageSize.A3,
        FontSize = 8,
        HeaderColor = "#374151",
        DayColor = "#4B5563",
        RowColor1 = "#FFFFFF",
        RowColor2 = "#F9FAFB",
        TextColor = "#111827",
        AccentColor = "#374151",
        CellStyle = PrintCellStyle.Outlined,
        ShowLegend = false,
        ShowSummary = false,
    };

    public static PrintTemplate BlankGrid() => new()
    {
        Id = "builtin-blank",
        Name = "شبكة فارغة (للتحضير)",
        IsBuiltIn = true,
        HeaderColor = "#FFFFFF",
        DayColor = "#E5E7EB",
        RowColor1 = "#FFFFFF",
        RowColor2 = "#FFFFFF",
        TextColor = "#111827",
        AccentColor = "#111827",
        CellStyle = PrintCellStyle.Minimal,
        ShowTeacher = false,
        ShowRoom = false,
        ShowLegend = false,
        ShowSummary = false,
        Zebra = false,
        ShowGridLines = true,
    };

    public static PrintTemplate TeacherCard() => new()
    {
        Id = "builtin-teacher-card",
        Name = "بطاقة المعلم",
        IsBuiltIn = true,
        HeaderColor = "#0F766E",
        DayColor = "#0D9488",
        RowColor1 = "#F0FDFA",
        RowColor2 = "#CCFBF1",
        TextColor = "#134E4A",
        AccentColor = "#0D9488",
        CellStyle = PrintCellStyle.Filled,
        ShowLegend = false,
        ShowSummary = false,
    };

    public static PrintTemplate ClassCard() => new()
    {
        Id = "builtin-class-card",
        Name = "بطاقة الفصل",
        IsBuiltIn = true,
        HeaderColor = "#B45309",
        DayColor = "#D97706",
        RowColor1 = "#FFFBEB",
        RowColor2 = "#FEF3C7",
        TextColor = "#451A03",
        AccentColor = "#D97706",
        CellStyle = PrintCellStyle.Filled,
        ShowLegend = false,
        ShowSummary = false,
    };

    public static PrintTemplate RoomCard() => new()
    {
        Id = "builtin-room-card",
        Name = "بطاقة القاعة",
        IsBuiltIn = true,
        HeaderColor = "#1E40AF",
        DayColor = "#1D4ED8",
        RowColor1 = "#EFF6FF",
        RowColor2 = "#DBEAFE",
        TextColor = "#172554",
        AccentColor = "#1D4ED8",
        CellStyle = PrintCellStyle.Filled,
        ShowLegend = false,
        ShowSummary = false,
    };

    public static IReadOnlyList<PrintTemplate> All { get; } = new List<PrintTemplate>
    {
        AscClassic(),
        ModernMinimal(),
        ElegantGradient(),
        CompactA3(),
        BlankGrid(),
        TeacherCard(),
        ClassCard(),
        RoomCard(),
    };

    public static PrintTemplate Default => AscClassic();

    public static PrintTemplate Get(string id)
        => All.FirstOrDefault(t => t.Id == id) ?? Default;
}

/// <summary>حفظ/تحميل القوالب المخصصة بجانب إعدادات التطبيق.</summary>
public static class PrintTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AwadTimetable",
        "print-templates.json");

    public static List<PrintTemplate> Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file)) return new List<PrintTemplate>();
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<List<PrintTemplate>>(json, JsonOptions) ?? new List<PrintTemplate>();
        }
        catch
        {
            return new List<PrintTemplate>();
        }
    }

    public static void Save(IEnumerable<PrintTemplate> templates, string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(templates, JsonOptions));
        }
        catch
        {
            // لا نُفشل التطبيق بسبب فشل الحفظ
        }
    }

    /// <summary>كل القوالب: المخصصة ثم الجاهزة</summary>
    public static IReadOnlyList<PrintTemplate> LoadAll(string? path = null)
    {
        var custom = Load(path).Where(t => !t.IsBuiltIn).ToList();
        var all = new List<PrintTemplate>();
        all.AddRange(custom);
        all.AddRange(PrintTemplatePresets.All);
        return all;
    }
}

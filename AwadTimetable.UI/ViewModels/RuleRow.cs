using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AwadTimetable.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AwadTimetable.UI.ViewModels;

/// <summary>خيار قائمة بقيمة وترجمة عربية.</summary>
public sealed record RuleOption<T>(T Value, string Text) where T : struct, Enum;

/// <summary>خيارات القواعد بالعربية (بدل أسماء enums الإنجليزية).</summary>
public static class RuleOptions
{
    public static IReadOnlyList<RuleOption<RuleKind>> Kinds { get; } = new List<RuleOption<RuleKind>>
    {
        new(RuleKind.EndTime, "وقت انتهاء — منع الحصص بعد فترة"),
        new(RuleKind.TeacherLink, "ربط معلم — تقييد أيام/فترات"),
        new(RuleKind.Availability, "إغلاق أوقات — حجب أيام/فترات"),
        new(RuleKind.SubjectDistribution, "توزيع مادة — حد أقصى يومي"),
        new(RuleKind.MaxConsecutive, "حد التتابع — أقصى حصص متتالية"),
        new(RuleKind.MaxGapsPerDay, "فجوات يومية — أقصى فراغات في اليوم"),
        new(RuleKind.DailyLoad, "عبء يومي — حد أدنى/أقصى للحصص"),
        new(RuleKind.MaxDaysPerWeek, "أقصى أيام — حد أقصى لأيام الحضور أسبوعياً"),
        new(RuleKind.MaxGapsPerWeek, "فجوات أسبوعية — أقصى فراغات في الأسبوع"),
        new(RuleKind.MaxSpanPerDay, "امتداد اليوم — أقصى طول اليوم الدراسي"),
        new(RuleKind.BeginsEarly, "بدايات مبكرة — حد الأيام التي تبدأ من الحصة الثانية"),
        new(RuleKind.SubjectPair, "زوج مواد — علاقة بين مادتين"),
        new(RuleKind.SubjectTwicePerDay, "مادة مرتين يومياً — مع خيار التتابع"),
        new(RuleKind.BlockLessons, "كتلة متصلة — بلا فجوات داخل اليوم"),
        new(RuleKind.SameStartingTime, "توقيت موحّد — بداية الحصص في وقت واحد"),
        new(RuleKind.NotOverlapping, "عدم تزامن — منع التداخل"),
        new(RuleKind.MinDaysBetweenActivities, "فاصل أيام — أدنى أيام بين التكرارات"),
        new(RuleKind.PreferredRoom, "قاعة مفضّلة — تفضيل/إلزام قاعة"),
        new(RuleKind.RoomChangeLimit, "تغيّر القاعات — حد تغييرات القاعة"),
        new(RuleKind.MinDaysPerWeek, "أدنى أيام — حد أدنى لأيام العمل أسبوعياً"),
        new(RuleKind.General, "قاعدة عامة"),
    };

    public static IReadOnlyList<RuleOption<RuleScope>> Scopes { get; } = new List<RuleOption<RuleScope>>
    {
        new(RuleScope.Teacher, "معلم"),
        new(RuleScope.Class, "فصل"),
        new(RuleScope.Subject, "مادة"),
        new(RuleScope.Room, "قاعة"),
        new(RuleScope.School, "المدرسة"),
    };

    public static IReadOnlyList<RuleOption<RulePriority>> Priorities { get; } = new List<RuleOption<RulePriority>>
    {
        new(RulePriority.Hard, "صلب — إلزامي"),
        new(RulePriority.High, "عالية"),
        new(RulePriority.Medium, "متوسطة"),
        new(RulePriority.Low, "منخفضة"),
        new(RulePriority.Ignore, "تجاهُل"),
    };
}

/// <summary>نصوص عربية لتصنيفات القواعد.</summary>
public static class RuleTexts
{
    public static string Kind(RuleKind k)
    {
        switch (k)
        {
            case RuleKind.EndTime: return "وقت انتهاء";
            case RuleKind.TeacherLink: return "ربط معلمين";
            case RuleKind.Availability: return "توفر معلم";
            case RuleKind.SubjectDistribution: return "توزيع مادة";
            case RuleKind.MaxConsecutive: return "حد التتابع";
            case RuleKind.MaxGapsPerDay: return "فجوات يومية";
            case RuleKind.DailyLoad: return "عبء يومي";
            case RuleKind.MaxDaysPerWeek: return "أقصى أيام أسبوعياً";
            case RuleKind.MaxGapsPerWeek: return "فجوات أسبوعية";
            case RuleKind.MaxSpanPerDay: return "امتداد اليوم";
            case RuleKind.BeginsEarly: return "بدايات مبكرة";
            case RuleKind.SubjectPair: return "زوج مواد";
            case RuleKind.SubjectTwicePerDay: return "مادة مرتين يومياً";
            case RuleKind.BlockLessons: return "كتلة متصلة";
            case RuleKind.SameStartingTime: return "توقيت موحّد";
            case RuleKind.NotOverlapping: return "عدم تزامن";
            case RuleKind.MinDaysBetweenActivities: return "فاصل أيام";
            case RuleKind.PreferredRoom: return "قاعة مفضّلة";
            case RuleKind.RoomChangeLimit: return "تغيير القاعات";
            case RuleKind.MinDaysPerWeek: return "أدنى أيام أسبوعياً";
            default: return "قاعدة عامة";
        }
    }

    public static string Scope(RuleScope s)
    {
        switch (s)
        {
            case RuleScope.Teacher: return "المعلمون";
            case RuleScope.Class: return "الفصول";
            case RuleScope.Subject: return "المواد";
            case RuleScope.Room: return "القاعات";
            default: return "عام";
        }
    }

    public static string Priority(RulePriority p)
    {
        switch (p)
        {
            case RulePriority.Hard: return "صلبة";
            case RulePriority.High: return "عالية";
            case RulePriority.Medium: return "متوسطة";
            case RulePriority.Low: return "منخفضة";
            case RulePriority.Ignore: return "تجاهل عند التوليد";
            default: return p.ToString();
        }
    }
}

/// <summary>صف بند قاعدة: يغلّف النموذج مباشرة فتُحفظ التعديلات مع الجدول.</summary>
public partial class RuleRow : ObservableObject
{
    public RuleItem Model { get; }

    public RuleRow(RuleItem model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <summary>اسم الهدف (معلم/فصل/مادة/قاعة) — يُملأ من ViewModel الرئيسي ويُحرَّر في وضع التحرير.</summary>
    [ObservableProperty] private string _targetName = "—";

    /// <summary>وضع التحرير (إظهار الحقول بدل الملخّص).</summary>
    [ObservableProperty] private bool _isEditing;

    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title == value) return;
            Model.Title = value ?? string.Empty;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public bool IsEnabled
    {
        get => Model.IsEnabled;
        set
        {
            if (Model.IsEnabled == value) return;
            Model.IsEnabled = value;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public RulePriority Priority
    {
        get => Model.Priority;
        set
        {
            if (Model.Priority == value) return;
            Model.Priority = value;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PriorityText));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public RuleKind Kind
    {
        get => Model.Kind;
        set
        {
            if (Model.Kind == value) return;
            Model.Kind = value;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KindText));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public RuleScope Scope
    {
        get => Model.Scope;
        set
        {
            if (Model.Scope == value) return;
            Model.Scope = value;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScopeText));
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>معاملات القاعدة (JSON): مثل {"days":[1],"periods":[3]} أو {"afterPeriod":6} أو {"maxPerDay":2}</summary>
    public string Parameters
    {
        get => Model.Parameters ?? string.Empty;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Model.Parameters == v) return;
            Model.Parameters = v;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string KindText => RuleTexts.Kind(Model.Kind);
    public string ScopeText => RuleTexts.Scope(Model.Scope);
    public string PriorityText => RuleTexts.Priority(Model.Priority);
    public string SourceText => Model.Source == "ai" ? "🤖 المساعد" : "👤 يدوي";

    public string Summary =>
        KindText + "  •  " + TargetName + "  •  " + Title + "  •  " + PriorityText
        + (IsEnabled ? string.Empty : "   (معطَّل)");

    public static RulePriority[] PriorityOptions { get; } =
    {
        RulePriority.Hard, RulePriority.High, RulePriority.Medium, RulePriority.Low, RulePriority.Ignore
    };

    public static RuleKind[] KindOptions { get; } =
    {
        RuleKind.EndTime, RuleKind.TeacherLink, RuleKind.Availability, RuleKind.SubjectDistribution,
        RuleKind.MaxConsecutive, RuleKind.MaxGapsPerDay, RuleKind.DailyLoad,
        RuleKind.MaxDaysPerWeek, RuleKind.MaxGapsPerWeek, RuleKind.MaxSpanPerDay, RuleKind.BeginsEarly,
        RuleKind.SubjectPair, RuleKind.SubjectTwicePerDay, RuleKind.BlockLessons,
        RuleKind.SameStartingTime, RuleKind.NotOverlapping, RuleKind.MinDaysBetweenActivities,
        RuleKind.PreferredRoom, RuleKind.RoomChangeLimit, RuleKind.MinDaysPerWeek, RuleKind.General
    };

    public static RuleScope[] ScopeOptions { get; } =
    {
        RuleScope.Teacher, RuleScope.Class, RuleScope.Subject, RuleScope.Room, RuleScope.School
    };

    [RelayCommand]
    private void ToggleEdit() => IsEditing = !IsEditing;
}

/// <summary>مجموعة قوائم مصنّفة حسب العلاقة (المعلمون / الفصول / …).</summary>
public sealed class RuleGroup
{
    public string Header { get; init; } = string.Empty;
    public ObservableCollection<RuleRow> Items { get; } = new ObservableCollection<RuleRow>();
    public int Count => Items.Count;
}

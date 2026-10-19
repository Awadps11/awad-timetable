using System;
using AwadTimetable.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AwadTimetable.UI.ViewModels;

/// <summary>صف عرض لطبقة الإسناد المركزية: المعلم + المادة + الفصل + عدد الحصص الأسبوعية.</summary>
public partial class TeachingAssignmentRow : ObservableObject
{
    public TeachingAssignment Model { get; }

    public TeachingAssignmentRow(TeachingAssignment model)
    {
        Model = model;
        _isEnabled = model.IsEnabled;
    }

    [ObservableProperty] private Teacher? _teacher;
    [ObservableProperty] private Subject? _subject;
    [ObservableProperty] private Class? _class;

    /// <summary>هل الصف في وضع التحرير (إظهار القوائم بدل الملخّص).</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>هل الإسناد مُفعَّل؟ المعطَّل لا يولّد حصصًا.</summary>
    [ObservableProperty] private bool _isEnabled = true;

    public int WeeklyPeriods
    {
        get => Model.WeeklyPeriods;
        set
        {
            var v = System.Math.Max(1, value);
            if (Model.WeeklyPeriods == v) return;
            Model.WeeklyPeriods = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string TeacherName => Teacher?.FullName ?? "—";
    public string SubjectName => Subject?.Name ?? "—";
    public string ClassName => Class?.FullName ?? "—";

    public string Summary => $"{SubjectName}  •  {TeacherName}  •  {ClassName}  •  {WeeklyPeriods} حصة"
        + (IsEnabled ? string.Empty : "   (معطَّل)");

    [RelayCommand]
    private void ToggleEdit() => IsEditing = !IsEditing;

    partial void OnIsEnabledChanged(bool value) => Model.IsEnabled = value;

    partial void OnTeacherChanged(Teacher? value)
    {
        Model.TeacherId = value is null ? Guid.Empty : (Guid)value.Id;
        OnPropertyChanged(nameof(TeacherName));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnSubjectChanged(Subject? value)
    {
        Model.SubjectId = value is null ? Guid.Empty : (Guid)value.Id;
        OnPropertyChanged(nameof(SubjectName));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnClassChanged(Class? value)
    {
        Model.ClassId = value is null ? Guid.Empty : (Guid)value.Id;
        OnPropertyChanged(nameof(ClassName));
        OnPropertyChanged(nameof(Summary));
    }
}

using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// نموذج عرض إدارة الجداول
/// </summary>
public partial class TimetableViewModel : ObservableObject
{
    private readonly ITimetableGenerator _generator;
    private readonly IConflictDetector _detector;

    [ObservableProperty]
    private ObservableCollection<Timetable> _timetables = new();

    [ObservableProperty]
    private Timetable? _selectedTimetable;

    [ObservableProperty]
    private bool _isLoading;

    public TimetableViewModel(ITimetableGenerator generator, IConflictDetector detector)
    {
        _generator = generator;
        _detector = detector;
    }

    [RelayCommand]
    private async Task LoadTimetablesAsync()
    {
        IsLoading = true;
        await Task.Delay(100); // محاكاة تحميل
        IsLoading = false;
    }

    [RelayCommand]
    private void NewTimetable()
    {
        // سيتم التنفيذ لاحقاً
    }

    [RelayCommand]
    private void DeleteTimetable()
    {
        if (SelectedTimetable != null)
        {
            Timetables.Remove(SelectedTimetable);
            SelectedTimetable = null;
        }
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AwadTimetable.Core.Constraints;
using AwadTimetable.Core.Entities;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// نموذج عرض محرر القيود
/// </summary>
public partial class ConstraintEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<IConstraint> _constraints = new();

    [ObservableProperty]
    private IConstraint? _selectedConstraint;

    [ObservableProperty]
    private bool _isLoading;

    public ConstraintEditorViewModel()
    {
        // تحميل القيود الافتراضية
        LoadDefaultConstraints();
    }

    private void LoadDefaultConstraints()
    {
        Constraints.Clear();
        foreach (var constraint in ConstraintRegistry.CreateAll())
        {
            Constraints.Add(constraint);
        }
    }

    [RelayCommand]
    private void AddConstraint()
    {
        // سيتم التنفيذ لاحقاً
    }

    [RelayCommand]
    private void RemoveConstraint()
    {
        if (SelectedConstraint != null)
        {
            Constraints.Remove(SelectedConstraint);
            SelectedConstraint = null;
        }
    }

    [RelayCommand]
    private void ToggleConstraint()
    {
        if (SelectedConstraint != null)
        {
            if (SelectedConstraint.IsEnabled)
                SelectedConstraint.Disable();
            else
                SelectedConstraint.Enable();
            OnPropertyChanged(nameof(SelectedConstraint));
        }
    }
}

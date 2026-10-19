using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;
using AwadTimetable.UI.ViewModels;

namespace AwadTimetable.UI.Views;

/// <summary>إدارة المواد مع إجراءات جماعية (تحديد متعدد).</summary>
public partial class SubjectsView : UserControl
{
    public SubjectsView()
    {
        InitializeComponent();
    }

    private SubjectManagementViewModel? Vm => DataContext as SubjectManagementViewModel;

    private void OnSelectAll(object sender, RoutedEventArgs e) => SubjectList.SelectAll();

    private void OnClearSelection(object sender, RoutedEventArgs e) => SubjectList.UnselectAll();

    private void OnCopySelected(object sender, RoutedEventArgs e)
        => Vm?.CopyMany(SubjectList.SelectedItems.OfType<Subject>().ToList());

    private async void OnExportSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ExportManyAsync(SubjectList.SelectedItems.OfType<Subject>().ToList());
    }

    private async void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.DeleteManyAsync(SubjectList.SelectedItems.OfType<Subject>().ToList());
    }
}

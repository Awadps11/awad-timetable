using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;
using AwadTimetable.UI.ViewModels;

namespace AwadTimetable.UI.Views;

/// <summary>إدارة المعلمين مع إجراءات جماعية (تحديد متعدد).</summary>
public partial class TeachersView : UserControl
{
    public TeachersView()
    {
        InitializeComponent();
    }

    private TeacherManagementViewModel? Vm => DataContext as TeacherManagementViewModel;

    private void OnSelectAll(object sender, RoutedEventArgs e) => TeacherList.SelectAll();

    private void OnClearSelection(object sender, RoutedEventArgs e) => TeacherList.UnselectAll();

    private void OnCopySelected(object sender, RoutedEventArgs e)
        => Vm?.CopyMany(TeacherList.SelectedItems.OfType<Teacher>().ToList());

    private async void OnExportSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ExportManyAsync(TeacherList.SelectedItems.OfType<Teacher>().ToList());
    }

    private async void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.DeleteManyAsync(TeacherList.SelectedItems.OfType<Teacher>().ToList());
    }
}

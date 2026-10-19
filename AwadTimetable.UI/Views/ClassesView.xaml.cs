using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;
using AwadTimetable.UI.ViewModels;

namespace AwadTimetable.UI.Views;

/// <summary>إدارة الفصول مع إجراءات جماعية (تحديد متعدد).</summary>
public partial class ClassesView : UserControl
{
    public ClassesView()
    {
        InitializeComponent();
    }

    private ClassManagementViewModel? Vm => DataContext as ClassManagementViewModel;

    private void OnSelectAll(object sender, RoutedEventArgs e) => ClassList.SelectAll();

    private void OnClearSelection(object sender, RoutedEventArgs e) => ClassList.UnselectAll();

    private void OnCopySelected(object sender, RoutedEventArgs e)
        => Vm?.CopyMany(ClassList.SelectedItems.OfType<Class>().ToList());

    private async void OnExportSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ExportManyAsync(ClassList.SelectedItems.OfType<Class>().ToList());
    }

    private async void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.DeleteManyAsync(ClassList.SelectedItems.OfType<Class>().ToList());
    }
}

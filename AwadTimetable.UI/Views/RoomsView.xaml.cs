using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;
using AwadTimetable.UI.ViewModels;

namespace AwadTimetable.UI.Views;

/// <summary>إدارة القاعات مع إجراءات جماعية (تحديد متعدد).</summary>
public partial class RoomsView : UserControl
{
    public RoomsView()
    {
        InitializeComponent();
    }

    private RoomManagementViewModel? Vm => DataContext as RoomManagementViewModel;

    private void OnSelectAll(object sender, RoutedEventArgs e) => RoomList.SelectAll();

    private void OnClearSelection(object sender, RoutedEventArgs e) => RoomList.UnselectAll();

    private void OnCopySelected(object sender, RoutedEventArgs e)
        => Vm?.CopyMany(RoomList.SelectedItems.OfType<Room>().ToList());

    private async void OnExportSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.ExportManyAsync(RoomList.SelectedItems.OfType<Room>().ToList());
    }

    private async void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.DeleteManyAsync(RoomList.SelectedItems.OfType<Room>().ToList());
    }
}

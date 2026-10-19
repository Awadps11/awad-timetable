using System.Windows;
using AwadTimetable.UI.ViewModels;

namespace AwadTimetable.UI.Views;

/// <summary>
/// نافذة الإعدادات
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(SettingsViewModel));
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            // انتظار الحفظ الفعلي إلى الملف قبل إغلاق النافذة
            await vm.SaveSettingsCommand.ExecuteAsync(null);
        }
        DialogResult = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

using System.Windows;
using AwadTimetable.Core.Constants;

namespace AwadTimetable.UI.Views;

/// <summary>
/// مربع معلومات البرنامج
/// </summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
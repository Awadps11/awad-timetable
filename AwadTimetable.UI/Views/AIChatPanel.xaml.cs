using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwadTimetable.UI.ViewModels;

namespace AwadTimetable.UI.Views;

/// <summary>
/// لوحة المحادثة الذكية
/// </summary>
public partial class AIChatPanel : UserControl
{
    public AIChatPanel()
    {
        InitializeComponent();
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            if (DataContext is AIChatPanelViewModel vm)
            {
                vm.SendMessageCommand.Execute(null);
            }
        }
    }
}
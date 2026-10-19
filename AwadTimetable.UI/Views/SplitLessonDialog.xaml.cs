using System;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;

namespace AwadTimetable.UI.Views;

public partial class SplitLessonDialog : Window
{
    public int Parts { get; private set; } = 2;
    public int? MinDaysBetween { get; private set; }
    public bool SplitDuration { get; private set; } = true;

    public SplitLessonDialog(Lesson lesson)
    {
        ArgumentNullException.ThrowIfNull(lesson);

        InitializeComponent();

        Title = $"تقسيم الحصة — {lesson.GetDisplayName()}";
        HeaderText.Text = $"{lesson.GetDisplayName()}  •  المدة الحالية: {(int)lesson.Duration} دقيقة";
        PartsBox.Text = "2";

        MinDaysBox.Items.Add(new ComboBoxItem { Content = "بلا حد" });
        for (var i = 1; i <= 7; i++)
            MinDaysBox.Items.Add(new ComboBoxItem { Content = i.ToString() });
        MinDaysBox.SelectedIndex = 0;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PartsBox.Text?.Trim(), out var parts) || parts < 2 || parts > 7)
        {
            MessageBox.Show(this, "أدخل عدد أجزاء بين 2 و 7.", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Parts = parts;
        MinDaysBetween = MinDaysBox.SelectedIndex >= 1 ? MinDaysBox.SelectedIndex : null;
        SplitDuration = SplitDurationCheck.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}

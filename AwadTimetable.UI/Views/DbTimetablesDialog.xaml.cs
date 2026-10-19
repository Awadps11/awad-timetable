using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using AwadTimetable.UI.Services;

namespace AwadTimetable.UI.Views;

/// <summary>
/// نافذة قائمة الجداول المحفوظة في قاعدة البيانات
/// </summary>
public partial class DbTimetablesDialog : Window
{
    public sealed class Row
    {
        public Guid Id { get; init; }
        public Guid SchoolId { get; init; }
        public string Name { get; init; } = "";
        public string SchoolName { get; init; } = "";
        public int YearStart { get; init; }
        public int YearEnd { get; init; }
        public int Term { get; init; }
        public string Status { get; init; } = "";
        public int LessonsCount { get; init; }
        public DateTime? UpdatedAt { get; init; }

        public string YearLabel => $"{YearStart}/{YearEnd}";
        public string UpdatedLabel => UpdatedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "—";
    }

    /// <summary>الجدول المختار للفتح (null عند الإلغاء)</summary>
    public Guid? SelectedTimetableId { get; private set; }

    /// <summary>الجدول المطلوب حذفه (null عند الإلغاء)</summary>
    public Guid? DeleteTargetId { get; private set; }

    public DbTimetablesDialog(IReadOnlyList<DbTimetableInfo> items)
    {
        InitializeComponent();

        var rows = new List<Row>();
        foreach (var i in items)
        {
            rows.Add(new Row
            {
                Id = i.Id,
                SchoolId = i.SchoolId,
                Name = i.Name,
                SchoolName = i.SchoolName,
                YearStart = i.YearStart,
                YearEnd = i.YearEnd,
                Term = i.Term,
                Status = i.Status,
                LessonsCount = i.LessonsCount,
                UpdatedAt = i.UpdatedAt
            });
        }

        TimetablesList.ItemsSource = rows;
        CountText.Text = $"{rows.Count} جدول محفوظ";
        if (rows.Count > 0) TimetablesList.SelectedIndex = 0;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (TimetablesList.SelectedItem is not Row row)
        {
            MessageBox.Show("اختر جدولًا من القائمة أولاً", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedTimetableId = row.Id;
        DialogResult = true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (TimetablesList.SelectedItem is not Row row)
        {
            MessageBox.Show("اختر جدولًا للحذف أولاً", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"هل تريد حذف «{row.Name}» ({row.LessonsCount} حصة) من قاعدة البيانات نهائياً؟",
            "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes) return;

        DeleteTargetId = row.Id;
        DialogResult = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) DialogResult = false;
    }
}

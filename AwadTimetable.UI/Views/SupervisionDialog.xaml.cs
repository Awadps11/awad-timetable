using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace AwadTimetable.UI.Views;

/// <summary>نافذة الإشراف (Dozor): توزيع فترات الاستراحة على القاعات.</summary>
public partial class SupervisionDialog : Window
{
    private static readonly string[] DayArabic =
        { "السبت", "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    private sealed record Row(string DayText, int Period, string RoomName, string SupervisorName, string Status);

    private readonly Timetable _timetable;

    public SupervisionDialog(Timetable timetable)
    {
        InitializeComponent();
        _timetable = timetable ?? throw new ArgumentNullException(nameof(timetable));
        DatePicker.SelectedDate = DateTime.Today;
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var date = DateOnly.FromDateTime(DatePicker.SelectedDate ?? DateTime.Today);

            List<Teacher> teachers;
            List<Room> rooms;

            using (var scope = App.Services.CreateScope())
            {
                teachers = (await scope.ServiceProvider.GetRequiredService<ITeacherRepository>()
                    .GetBySchoolAsync(_timetable.SchoolId)).ToList();
                rooms = (await scope.ServiceProvider.GetRequiredService<IRoomRepository>()
                    .GetBySchoolAsync(_timetable.SchoolId)).ToList();
            }

            if (rooms.Count == 0)
            {
                MessageBox.Show("لا توجد قاعات معرّفة. أضف قاعات أولاً.", "الإشراف",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = SupervisionGenerator.Generate(_timetable, teachers, rooms, Array.Empty<Guid>(), date);

            ResultsGrid.ItemsSource = result
                .OrderBy(r => r.Period)
                .Select(r => new Row(
                    DayArabic[Math.Clamp((int)r.Day, 0, 6)],
                    r.Period,
                    r.RoomName,
                    string.IsNullOrWhiteSpace(r.SupervisorName) ? "—" : r.SupervisorName,
                    r.Status))
                .ToList();

            var assigned = result.Count(r => r.Status == "معيّن");
            SummaryText.Text = result.Count == 0
                ? "لا توجد فترات استراحة لهذا اليوم."
                : $"{assigned} من {result.Count} قاعة مغطاة.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر توليد الإشراف:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

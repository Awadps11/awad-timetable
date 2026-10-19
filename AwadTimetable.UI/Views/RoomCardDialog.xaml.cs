using System;
using System.IO;
using System.Linq;
using System.Windows;
using AwadTimetable.Core.Entities;
using AwadTimetable.Data.Repositories;
using AwadTimetable.Printing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace AwadTimetable.UI.Views;

/// <summary>اختيار قاعة وحفظ بطاقتها (جدول أسبوعي) كملف PDF.</summary>
public partial class RoomCardDialog : Window
{
    private readonly Timetable _timetable;

    public RoomCardDialog(Timetable timetable)
    {
        InitializeComponent();
        _timetable = timetable ?? throw new ArgumentNullException(nameof(timetable));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            using var scope = App.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            var rooms = (await repo.GetBySchoolAsync(_timetable.SchoolId))
                .OrderBy(r => r.Name, StringComparer.CurrentCulture)
                .ToList();

            RoomComboBox.ItemsSource = rooms;
            if (rooms.Count > 0) RoomComboBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تحميل القاعات:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (RoomComboBox.SelectedItem is not Room room)
        {
            MessageBox.Show("اختر قاعة أولاً.", "بطاقة القاعة", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"قاعة-{room.Name}.pdf",
            Title = "حفظ بطاقة القاعة",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var print = (IPrintService)App.Services.GetService(typeof(IPrintService))!;
            var bytes = await print.GenerateRoomSchedulePdfAsync(room, _timetable, new PrintOptions
            {
                FontFamily = "Segoe UI",
                FontSize = 10,
            });

            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            MessageBox.Show("تم حفظ بطاقة القاعة.", "بطاقة القاعة", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر إنشاء البطاقة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

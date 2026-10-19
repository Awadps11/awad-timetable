using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Data.Repositories;
using AwadTimetable.UI.Services;
using Microsoft.Extensions.DependencyInjection;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.UI.Views;

/// <summary>
/// محرر التفريغ والندب: شبكة (أيام × فترات) لكل معلم، والخلية ثلاث حالات:
/// متاح، إغلاق ملزم (أولوية عالية)، إغلاق قابل للتجاوز (أولوية منخفضة).
/// </summary>
public partial class TeacherAvailabilityDialog : Window
{
    private enum CellState { Available, HardBlock, SoftBlock }

    private static readonly string[] DayArabic =
        { "السبت", "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    private sealed record TeacherItem(Guid Id, string Display);

    private readonly Timetable _timetable;
    private readonly Dictionary<(int Day, int Period), Button> _cells = new();
    private readonly Dictionary<Button, CellState> _visual = new();
    private readonly List<int> _days = new();
    private readonly int _maxPeriod;

    public bool Applied { get; private set; }

    public TeacherAvailabilityDialog(Timetable timetable)
    {
        InitializeComponent();
        _timetable = timetable ?? throw new ArgumentNullException(nameof(timetable));

        _days = _timetable.StageConfigs
            .SelectMany(c => c.WorkDays)
            .Distinct()
            .Where(d => d >= 0 && d <= 6)
            .OrderBy(d => d)
            .ToList();
        if (_days.Count == 0) _days = new List<int> { 0, 1, 2, 3, 4, 5 };

        _maxPeriod = Math.Max(
            _timetable.PeriodTimes.Count > 0 ? _timetable.PeriodTimes.Max(p => (int)p.Period) : 0,
            _timetable.Lessons.Count > 0 ? _timetable.Lessons.Max(l => (int)l.Period.Value) : 0);
        if (_maxPeriod <= 0) _maxPeriod = 7;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var repo = App.Services.GetService(typeof(ITeacherRepository)) as ITeacherRepository;
            if (repo is null) return;

            var teachers = await repo.GetBySchoolAsync(_timetable.SchoolId);
            var items = teachers
                .OrderBy(t => t.FullName, StringComparer.CurrentCulture)
                .Select(t => new TeacherItem(t.Id, t.FullName))
                .ToList();

            TeacherComboBox.ItemsSource = items;
            TeacherComboBox.DisplayMemberPath = "Display";
            TeacherComboBox.SelectionChanged += (_, _) => BuildGrid();
            if (items.Count > 0) TeacherComboBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تحميل المعلمين:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BuildGrid()
    {
        GridHost.Children.Clear();
        GridHost.RowDefinitions.Clear();
        GridHost.ColumnDefinitions.Clear();
        _cells.Clear();
        _visual.Clear();

        if (TeacherComboBox.SelectedItem is not TeacherItem teacher) return;

        // رأس الأعمدة: الفترات
        GridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        for (var p = 1; p <= _maxPeriod; p++)
            GridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

        var header = new TextBlock
        {
            Text = "اليوم / الفترة",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(header, 0);
        Grid.SetColumn(header, 0);
        GridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        GridHost.Children.Add(header);

        for (var p = 1; p <= _maxPeriod; p++)
        {
            var tb = new TextBlock
            {
                Text = p.ToString(),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4)
            };
            Grid.SetRow(tb, 0);
            Grid.SetColumn(tb, p);
            GridHost.Children.Add(tb);
        }

        // صف لكل يوم
        for (var r = 0; r < _days.Count; r++)
        {
            var day = _days[r];
            GridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var dayLabel = new TextBlock
            {
                Text = DayArabic[day],
                Margin = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(dayLabel, r + 1);
            Grid.SetColumn(dayLabel, 0);
            GridHost.Children.Add(dayLabel);

            for (var p = 1; p <= _maxPeriod; p++)
            {
                var btn = new Button
                {
                    Width = 44,
                    Height = 30,
                    Margin = new Thickness(2),
                    Tag = (day, p),
                    BorderThickness = new Thickness(0)
                };
                var state = CurrentState(teacher.Id, day, p);
                ApplyCellVisual(btn, state);
                btn.Click += OnCellClick;

                Grid.SetRow(btn, r + 1);
                Grid.SetColumn(btn, p);
                GridHost.Children.Add(btn);
                _cells[(day, p)] = btn;
            }
        }
    }

    private CellState CurrentState(Guid teacherId, int day, int period)
    {
        var avail = _timetable.TeacherAvailabilities.FirstOrDefault(a =>
            a.TeacherId.Value == teacherId && (int)a.Day == day && a.Period.Value == period);

        if (avail is null || avail.IsAvailable) return CellState.Available;
        return avail.Priority == AvailabilityPriority.High ? CellState.HardBlock : CellState.SoftBlock;
    }

    private void OnCellClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ValueTuple<int, int> tag) return;

        // دورة ثلاثية داخل الواجهة فقط؛ الحفظ عند الضغط على «حفظ»
        var current = VisualState(btn);
        var next = current switch
        {
            CellState.Available => CellState.HardBlock,
            CellState.HardBlock => CellState.SoftBlock,
            _ => CellState.Available
        };
        ApplyCellVisual(btn, next);
    }

    private CellState VisualState(Button btn) => _visual.TryGetValue(btn, out var s) ? s : CellState.Available;

    private void ApplyCellVisual(Button btn, CellState state)
    {
        _visual[btn] = state;
        switch (state)
        {
            case CellState.HardBlock:
                btn.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
                btn.Content = "ملزم";
                btn.Foreground = Brushes.White;
                break;
            case CellState.SoftBlock:
                btn.Background = new SolidColorBrush(Color.FromRgb(0xFB, 0x8C, 0x00));
                btn.Content = "متجاوز";
                btn.Foreground = Brushes.White;
                break;
            default:
                btn.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
                btn.Content = "متاح";
                btn.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));
                break;
        }
    }

    private void OnBlockAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var btn in _cells.Values) ApplyCellVisual(btn, CellState.HardBlock);
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var btn in _cells.Values) ApplyCellVisual(btn, CellState.Available);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (TeacherComboBox.SelectedItem is not TeacherItem teacher) { DialogResult = false; Close(); return; }

        var teacherId = (TeacherId)teacher.Id;

        // إزالة إغلاقات هذا المعلم ثم إعادة كتابتها من الشبكة
        var existing = _timetable.TeacherAvailabilities.Where(a => a.TeacherId.Value == teacher.Id).ToList();
        foreach (var a in existing) _timetable.TeacherAvailabilities.Remove(a);

        foreach (var kv in _cells)
        {
            var (day, period) = kv.Key;
            var state = VisualState(kv.Value);
            if (state == CellState.Available) continue;

            var priority = state == CellState.HardBlock ? AvailabilityPriority.High : AvailabilityPriority.Low;
            _timetable.TeacherAvailabilities.Add(new TeacherAvailability(
                teacherId, (DomainDayOfWeek)day, PeriodNumber.Create((byte)period), isAvailable: false, priority: priority));
        }

        Applied = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

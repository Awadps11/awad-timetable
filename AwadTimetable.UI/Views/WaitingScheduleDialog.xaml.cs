using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Data.Repositories;
using AwadTimetable.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AwadTimetable.UI.Views;

/// <summary>
/// نافذة جدول الانتظار: توليد «بديل لكل حصة» (انتظار 1) مع احتياط 2/3 لكل فترة،
/// وعرض الأنصبة قبل/بعد.
/// </summary>
public partial class WaitingScheduleDialog : Window
{
    private static readonly string[] DayArabic =
        { "السبت", "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    private readonly Timetable _timetable;
    private List<Teacher> _teachers = new();
    private List<Class> _classes = new();

    private sealed record DutyRow(string DayText, int Period, string ClassName, string TeacherName, int Tier);
    private sealed record LoadRow(string TeacherName, int BaseLoad, int WaitingLoad);

    public WaitingScheduleDialog(Timetable timetable)
    {
        InitializeComponent();
        _timetable = timetable ?? throw new ArgumentNullException(nameof(timetable));

        var maxPeriod = 0;
        if (_timetable.StageConfigs.Count > 0)
            maxPeriod = _timetable.StageConfigs.Max(c => c.LessonsPerDay);
        if (_timetable.Lessons.Count > 0)
            maxPeriod = Math.Max(maxPeriod, _timetable.Lessons.Max(l => (int)l.Period.Value));
        if (maxPeriod <= 0) maxPeriod = 7;

        PeriodsTextBox.Text = maxPeriod.ToString();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            using var scope = App.Services.CreateScope();
            var teacherRepo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            var classRepo = scope.ServiceProvider.GetRequiredService<IClassRepository>();

            _teachers = (await teacherRepo.GetBySchoolAsync(_timetable.SchoolId)).ToList();
            _classes = (await classRepo.GetBySchoolAsync(_timetable.SchoolId)).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تحميل المعلمين والفصول:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 3 شعب انتظار لكل مرحلة (طريقة aSc): تُملأ كل الفترات لكل شعبة
            var waitingClasses = new List<(string Stage, string WaitingClassName)>();
            foreach (var cfg in _timetable.StageConfigs)
            {
                for (var i = 1; i <= 3; i++)
                    waitingClasses.Add((cfg.Stage, $"انتظار {cfg.Stage} {i}"));
            }

            if (waitingClasses.Count == 0)
            {
                MessageBox.Show("لا توجد مراحل مُعرَّفة في الجدول.", "الانتظار", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var plans = WaitingLessonPlanner.Plan(_timetable, _teachers, waitingClasses);

            var teacherById = _teachers.ToDictionary(t => t.Id, t => t.FullName);
            var baseLoad = _teachers.ToDictionary(t => t.Id, t => _timetable.Lessons.Count(l => l.TeacherId.Value == t.Id));
            var waitingLoad = _teachers.ToDictionary(t => t.Id, t => plans.Count(p => p.TeacherId == t.Id));

            DutiesGrid.ItemsSource = plans
                .OrderBy(p => p.Day).ThenBy(p => p.Period).ThenBy(p => p.WaitingClassName)
                .Select(p => new DutyRow(
                    DayArabic[(int)p.Day],
                    p.Period,
                    p.WaitingClassName,
                    teacherById.GetValueOrDefault(p.TeacherId, p.TeacherId.ToString()),
                    1))
                .ToList();

            LoadsGrid.ItemsSource = _teachers
                .Select(t => new LoadRow(t.FullName, baseLoad[t.Id], waitingLoad[t.Id]))
                .OrderByDescending(r => r.WaitingLoad)
                .ThenBy(r => r.TeacherName)
                .ToList();

            var totals = _teachers.Select(t => baseLoad[t.Id] + waitingLoad[t.Id]).ToList();
            var uncovered = waitingClasses.Sum(w =>
            {
                var expected = _timetable.StageConfigs.First(c => c.Stage == w.Stage).LessonsPerDay
                               * Math.Max(1, _timetable.StageConfigs.First(c => c.Stage == w.Stage).WorkDays.Count);
                return Math.Max(0, expected - plans.Count(p => p.WaitingClassName == w.WaitingClassName));
            });

            SummaryText.Text =
                $"حصص الانتظار: {plans.Count} في {waitingClasses.Count} شعبة — غير مغطى: {uncovered}. " +
                (totals.Count > 0 ? $"إجمالي الأنصبة بعد الانتظار: من {totals.Min()} إلى {totals.Max()} حصة." : "");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر توليد جدول الانتظار:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

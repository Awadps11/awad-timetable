using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Data.Repositories;
using AwadTimetable.UI.Services;

namespace AwadTimetable.UI.Views;

/// <summary>
/// معالج إنشاء جدول جديد:
/// الخطوة 1: المعلومات الأساسية (الاسم، المدرسة، السنة، نوع المدرسة).
/// الخطوة 2: أيام الدراسة والفترات (عدد الحصص، زمن الحصة، الفسح تلقائياً، المحرك).
/// </summary>
public partial class NewTimetableDialog : Window
{
    private const string DefaultSchoolName = "الفاروق";
    private const string DefaultTimetableName = "جدول 1";

    private static readonly (string Name, int Index)[] DayOptions =
    {
        ("السبت", 0), ("الأحد", 1), ("الإثنين", 2), ("الثلاثاء", 3), ("الأربعاء", 4), ("الخميس", 5)
    };

    private static readonly (string Stage, SchoolType Type, bool Default)[] StageOptions =
    {
        ("متوسط", SchoolType.Middle, true),
        ("ثانوي", SchoolType.Secondary, true),
        ("ابتدائي", SchoolType.Primary, false),
    };

    private readonly List<CheckBox> _dayChecks = new();
    private readonly List<CheckBox> _stageChecks = new();
    private readonly Dictionary<string, (TextBox Lessons, TextBox Minutes)> _stageTiming = new();
    private readonly Dictionary<string, List<(int Day, TextBox Box)>> _stageDayTiming = new();

    private static readonly string[] DayArabic =
        { "السبت", "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    private bool _creating;

    public Timetable? CreatedTimetable { get; private set; }

    public NewTimetableDialog()
    {
        InitializeComponent();

        NameTextBox.Text = DefaultTimetableName;
        SchoolNameTextBox.Text = DefaultSchoolName;

        var year = DateTime.Now.Year;
        for (var y = year - 2; y <= year + 4; y++)
        {
            StartYearComboBox.Items.Add(y);
            EndYearComboBox.Items.Add(y + 1);
        }
        StartYearComboBox.SelectedItem = year;
        EndYearComboBox.SelectedItem = year + 1;

        foreach (var (name, index) in DayOptions)
        {
            var cb = new CheckBox
            {
                Content = name,
                Tag = index,
                IsChecked = true,
                Margin = new Thickness(0, 0, 16, 8),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("ArabicFont")
            };
            cb.Checked += (_, _) => UpdateBreakSummary();
            cb.Unchecked += (_, _) => UpdateBreakSummary();
            _dayChecks.Add(cb);
            DaysWrap.Children.Add(cb);
        }

        foreach (var (stage, type, isDefault) in StageOptions)
        {
            var cb = new CheckBox
            {
                Content = stage,
                Tag = type,
                IsChecked = isDefault,
                Margin = new Thickness(0, 0, 16, 8),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("ArabicFont")
            };
            cb.Checked += (_, _) => UpdateBreakSummary();
            cb.Unchecked += (_, _) => UpdateBreakSummary();
            _stageChecks.Add(cb);
            StageWrap.Children.Add(cb);
        }

        // لا تُعرض محركات غير قابلة للعمل فعلياً (FET بلا مسار fet-cl) حتى لا يوهم المستخدم
        var settings = App.Services.GetService(typeof(ISettingsService)) as ISettingsService;
        var fetPath = settings?.Get<string>(FetSettings.FetClPathKey, string.Empty) ?? string.Empty;
        var engines = GeneratorEngineCatalog.All
            .Where(e => e.Engine != GeneratorEngine.Fet
                || (!string.IsNullOrWhiteSpace(fetPath) && File.Exists(fetPath)))
            .ToList();

        EngineComboBox.ItemsSource = engines;
        EngineComboBox.SelectionChanged += OnEngineSelectionChanged;
        EngineComboBox.SelectedValue = GeneratorEngine.CpSat;
        OnEngineSelectionChanged(this, null!);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void OnEngineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EngineComboBox.SelectedItem is GeneratorEngineCatalog.EngineInfo info)
            EngineDescriptionText.Text = info.Description;
    }

    private List<(SchoolType Type, string Stage)> SelectedStages()
        => _stageChecks
            .Where(c => c.IsChecked == true)
            .Select(c => ((SchoolType)c.Tag, StageFor((SchoolType)c.Tag)))
            .ToList();

    private List<int> SelectedDays()
        => _dayChecks.Where(c => c.IsChecked == true).Select(c => (int)c.Tag).OrderBy(i => i).ToList();

    /// <summary>يبني صفاً لكل مرحلة مختارة: عدد الحصص وزمن الحصة</summary>
    private void RebuildStageTimingRows()
    {
        StagesTimingPanel.Children.Clear();
        _stageTiming.Clear();
        _stageDayTiming.Clear();

        foreach (var (type, stage) in SelectedStages())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = stage,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (System.Windows.Media.FontFamily)FindResource("ArabicFont")
            };
            var lessons = new TextBox { Text = TimingBuilder.DefaultLessonsPerDay.ToString(), Margin = new Thickness(4), Padding = new Thickness(4, 3, 4, 3) };
            var minutes = new TextBox { Text = TimingBuilder.DefaultLessonMinutes.ToString(), Margin = new Thickness(4), Padding = new Thickness(4, 3, 4, 3) };
            System.Windows.Automation.AutomationProperties.SetName(lessons, $"حصص {stage} في اليوم");
            System.Windows.Automation.AutomationProperties.SetName(minutes, $"زمن حصة {stage} بالدقائق");

            lessons.TextChanged += (_, _) => UpdateBreakSummary();

            Grid.SetColumn(label, 0);
            Grid.SetColumn(lessons, 1);
            Grid.SetColumn(minutes, 2);
            row.Children.Add(label);
            row.Children.Add(lessons);
            row.Children.Add(minutes);

            _stageTiming[stage] = (lessons, minutes);
            StagesTimingPanel.Children.Add(row);

            // تفاوت عدد الحصص بين الأيام (اتركه فارغاً/مطابقاً للعام لعدم التخصيص)
            var dayPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            var boxes = new List<(int Day, TextBox Box)>();

            foreach (var day in SelectedDays())
            {
                dayPanel.Children.Add(new TextBlock
                {
                    Text = DayArabic[Math.Clamp(day, 0, 6)] + ":",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 4, 0),
                    FontFamily = (System.Windows.Media.FontFamily)FindResource("ArabicFont"),
                });

                var box = new TextBox
                {
                    Text = lessons.ToString(),
                    Width = 42,
                    TextAlignment = TextAlignment.Center,
                    FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Thickness(0, 0, 4, 0),
                };
                System.Windows.Automation.AutomationProperties.SetName(box, $"حصص {stage} يوم {DayArabic[Math.Clamp(day, 0, 6)]}");

                dayPanel.Children.Add(box);
                boxes.Add((day, box));
            }

            _stageDayTiming[stage] = boxes;
            StagesTimingPanel.Children.Add(dayPanel);
        }
    }

    private static int ParseClamped(string? text, int fallback, int min, int max)
        => int.TryParse(text, out var n) ? Math.Clamp(n, min, max) : fallback;

    private void UpdateBreakSummary()
    {
        if (BreakSummaryText is null) return;

        var lines = new List<string>();
        foreach (var (type, stage) in SelectedStages())
        {
            var lessons = _stageTiming.TryGetValue(stage, out var t)
                ? ParseClamped(t.Lessons.Text, TimingBuilder.DefaultLessonsPerDay, 1, 12)
                : TimingBuilder.DefaultLessonsPerDay;

            var parts = TimingBuilder.BreakPlan(type)
                .Where(b => b.AfterLesson <= lessons)
                .Select(b => $"بعد الحصة {b.AfterLesson} — {b.Minutes} دقيقة")
                .ToList();

            if (parts.Count > 0)
                lines.Add($"{stage}: {string.Join(" ، ", parts)}");
        }

        BreakSummaryText.Text = lines.Count > 0
            ? string.Join("  |  ", lines)
            : "لا توجد فسح ضمن عدد الحصص المحدد.";
    }

    private void ShowStep(int step)
    {
        var onStep2 = step == 2;
        BasicPanel.Visibility = onStep2 ? Visibility.Collapsed : Visibility.Visible;
        DaysPanel.Visibility = onStep2 ? Visibility.Visible : Visibility.Collapsed;

        NextButton.Visibility = onStep2 ? Visibility.Collapsed : Visibility.Visible;
        CreateButton.Visibility = onStep2 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = onStep2 ? Visibility.Visible : Visibility.Collapsed;

        Step1Badge.FontWeight = onStep2 ? FontWeights.Normal : FontWeights.Bold;
        Step2Badge.FontWeight = onStep2 ? FontWeights.Bold : FontWeights.Normal;
        Step1Badge.Foreground = onStep2
            ? (System.Windows.Media.Brush)FindResource("OnSurfaceVariantBrush")
            : (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush");
        Step2Badge.Foreground = onStep2
            ? (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush")
            : (System.Windows.Media.Brush)FindResource("OnSurfaceVariantBrush");

        if (onStep2)
        {
            RebuildStageTimingRows();
            UpdateBreakSummary();
        }
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show("يرجى إدخال اسم الجدول", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(SchoolNameTextBox.Text))
        {
            MessageBox.Show("يرجى إدخال اسم المدرسة", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            SchoolNameTextBox.Focus();
            return;
        }

        if (StartYearComboBox.SelectedItem is not int start || EndYearComboBox.SelectedItem is not int end)
        {
            MessageBox.Show("يرجى اختيار السنة الدراسية", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (end <= start)
        {
            MessageBox.Show("سنة النهاية يجب أن تكون أكبر من سنة البداية", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedStages().Count == 0)
        {
            MessageBox.Show("اختر مرحلة دراسية واحدة على الأقل", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShowStep(2);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => ShowStep(1);

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (_creating) return;
        _creating = true;
        CreateButton.IsEnabled = false;
        try
        {

        var days = SelectedDays();
        if (days.Count == 0)
        {
            MessageBox.Show("اختر يوم دراسة واحداً على الأقل", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selected = EngineComboBox.SelectedItem as GeneratorEngineCatalog.EngineInfo;
        if (selected is null || !selected.IsAvailable)
        {
            MessageBox.Show("يرجى اختيار محرك توليد متاح", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var stages = SelectedStages();
        if (stages.Count == 0)
        {
            MessageBox.Show("اختر مرحلة دراسية واحدة على الأقل", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var startYear = (int)StartYearComboBox.SelectedItem;
        var endYear = (int)EndYearComboBox.SelectedItem;
        var term = int.TryParse((TermComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var termNo) ? termNo : 1;

        var schoolService = (CurrentSchoolService)App.Services.GetService(typeof(CurrentSchoolService))!;
        var schoolName = SchoolNameTextBox.Text.Trim();

        // جدول جديد = مساحة عمل مستقلة فارغة تماماً (إلا إذا طلب المستخدم إعادة استخدام بيانات مدرسة سابقة)
        Core.ValueObjects.SchoolId schoolId;
        try
        {
            schoolId = ReuseExistingCheck.IsChecked == true
                ? await schoolService.GetOrCreateWorkspaceByNameAsync(schoolName)
                : await schoolService.CreateEmptyWorkspaceAsync(schoolName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تجهيز مساحة العمل:\n{ex.Message}", "خطأ",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var timetable = new Timetable(schoolId, NameTextBox.Text.Trim(), startYear, endYear, term);
        timetable.SetSchoolType(stages[0].Type);
        timetable.SetEngine(selected.Engine);

        // توقيت مستقل لكل مرحلة (متوسط بقالبه، ثانوي بقالبه)
        var stageBuilds = new List<(SchoolType Type, string Stage, int LessonsPerDay, int LessonMinutes)>();
        var configs = new List<StageScheduleConfig>();
        var perDayPeriods = new Dictionary<string, IReadOnlyDictionary<int, int>>();

        foreach (var (type, stage) in stages)
        {
            var lessons = _stageTiming.TryGetValue(stage, out var t)
                ? ParseClamped(t.Lessons.Text, TimingBuilder.DefaultLessonsPerDay, 1, 12)
                : TimingBuilder.DefaultLessonsPerDay;
            var minutes = _stageTiming.TryGetValue(stage, out var m)
                ? ParseClamped(m.Minutes.Text, TimingBuilder.DefaultLessonMinutes, 20, 120)
                : TimingBuilder.DefaultLessonMinutes;

            // تفاوت الأيام: يُسجَّل فقط ما يختلف عن العدد العام
            var byDay = new Dictionary<int, int>();
            if (_stageDayTiming.TryGetValue(stage, out var dayBoxes))
            {
                foreach (var (day, box) in dayBoxes)
                {
                    var v = ParseClamped(box.Text, lessons, 1, 12);
                    if (v != lessons) byDay[day] = v;
                }
            }

            if (byDay.Count > 0) perDayPeriods[stage] = byDay;

            stageBuilds.Add((type, stage, lessons, minutes));
            configs.Add(new StageScheduleConfig
            {
                Stage = stage,
                WorkDays = days,
                LessonsPerDay = lessons,
                LessonsPerDayByDay = byDay,
            });
        }

        timetable.PeriodTimes = TimingBuilder.BuildForStages(stageBuilds, days, perDayPeriods);
        timetable.StageConfigs = configs;

        CreatedTimetable = timetable;
        DialogResult = true;
        Close();
        }
        finally
        {
            _creating = false;
            if (DialogResult != true) CreateButton.IsEnabled = true;
        }
    }

    private static string StageFor(SchoolType type) => type switch
    {
        SchoolType.Primary => "ابتدائي",
        SchoolType.Secondary => "ثانوي",
        _ => "متوسط",
    };

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

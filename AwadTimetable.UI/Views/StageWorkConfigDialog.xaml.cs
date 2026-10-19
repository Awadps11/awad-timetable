using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;

namespace AwadTimetable.UI.Views;

/// <summary>
/// نافذة إعداد أيام العمل وعدد الحصص اليومية لكل مرحلة تعليمية
/// </summary>
public partial class StageWorkConfigDialog : Window
{
    public List<StageScheduleConfig> ResultConfigs { get; private set; } = new();

    /// <summary>السبت=0 … الخميس=5 (الجمعة=6 ليست يوم دراسي)</summary>
    private static readonly (int Day, string Name)[] WorkDayNames =
    {
        (0, "السبت"), (1, "الأحد"), (2, "الاثنين"),
        (3, "الثلاثاء"), (4, "الأربعاء"), (5, "الخميس"),
    };

    private static readonly string[] AllStages = { "ابتدائي", "متوسط", "ثانوي" };

    private sealed class StageRow
    {
        public string Stage = "";
        public List<CheckBox> DayBoxes = new();
        public TextBox LessonsBox = null!;
    }

    private readonly List<StageRow> _rows = new();

    public StageWorkConfigDialog(IEnumerable<StageScheduleConfig> current)
    {
        InitializeComponent();

        var byStage = (current ?? Enumerable.Empty<StageScheduleConfig>())
            .GroupBy(c => c.Stage)
            .ToDictionary(g => g.Key, g => g.First());

        // اعرض المراحل الثلاث دائماً؛ المراحل غير المُهيّأة تحصل على الافتراضي (سبت-خميس، 7 حصص)
        foreach (var stage in AllStages)
        {
            byStage.TryGetValue(stage, out var cfg);
            var workDays = cfg?.WorkDays?.Where(d => d >= 0 && d <= 5).Distinct().ToList()
                ?? WorkDayNames.Select(d => d.Day).ToList();
            var lessons = cfg?.LessonsPerDay > 0 ? cfg!.LessonsPerDay : 7;
            AddRow(stage, workDays, lessons);
        }
    }

    private void AddRow(string stage, IReadOnlyCollection<int> workDays, int lessonsPerDay)
    {
        var row = new StageRow { Stage = stage };

        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };

        var header = new TextBlock
        {
            Text = stage,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(header);

        var daysPanel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (day, name) in WorkDayNames)
        {
            var box = new CheckBox
            {
                Content = name,
                Tag = day,
                IsChecked = workDays.Contains(day),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            daysPanel.Children.Add(box);
            row.DayBoxes.Add(box);
        }
        panel.Children.Add(daysPanel);

        var lessonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
        };
        lessonsPanel.Children.Add(new TextBlock
        {
            Text = "الحصص اليومية:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        row.LessonsBox = new TextBox
        {
            Width = 50,
            Text = lessonsPerDay.ToString(),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        lessonsPanel.Children.Add(row.LessonsBox);
        panel.Children.Add(lessonsPanel);

        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 0) });

        _rows.Add(row);
        RowsPanel.Children.Add(panel);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var result = new List<StageScheduleConfig>();

        foreach (var row in _rows)
        {
            var workDays = row.DayBoxes
                .Where(b => b.IsChecked == true && b.Tag is int d)
                .Select(b => (int)b.Tag!)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (workDays.Count == 0)
                continue; // مرحلة بلا أيام عمل = بلا قيد (تُomit)

            if (!int.TryParse(row.LessonsBox.Text?.Trim(), out var lessons) || lessons < 1 || lessons > 12)
            {
                MessageBox.Show(
                    $"عدد الحصص اليومية لمرحلة {row.Stage} يجب أن يكون بين 1 و 12.",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                row.LessonsBox.Focus();
                return;
            }

            result.Add(new StageScheduleConfig
            {
                Stage = row.Stage,
                WorkDays = workDays,
                LessonsPerDay = lessons,
            });
        }

        if (result.Count == 0)
        {
            MessageBox.Show(
                "اختر يوم عمل واحد على الأقل لمرحلة واحدة على الأقل.",
                "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultConfigs = result;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

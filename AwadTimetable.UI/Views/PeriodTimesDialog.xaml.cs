using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;

namespace AwadTimetable.UI.Views;

/// <summary>
/// نافذة إعداد توقيت الفترات الدراسية
/// (يمكن تخصيص التوقيت حسب اليوم والمرحلة)
/// </summary>
public partial class PeriodTimesDialog : Window
{
    public List<PeriodTime> ResultTimes { get; private set; } = new();

    private static readonly string[] DayNames =
        { "كل الأيام", "السبت", "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    private static readonly string[] StageNames =
        { "كل المراحل", "ابتدائي", "متوسط", "ثانوي" };

    public PeriodTimesDialog(IEnumerable<PeriodTime> current)
    {
        InitializeComponent();

        var list = current.ToList();
        if (list.Count == 0)
        {
            // افتراضي: 8 فترات 07:30-15:00 متساوية
            var start = 7 * 60 + 30;
            for (byte p = 1; p <= 8; p++)
            {
                list.Add(new PeriodTime { Period = p, StartMinutes = start, EndMinutes = start + 45, Day = -1, Stage = null });
                start += 50;
            }
        }

        foreach (var t in list) AddRow(t);
    }

    private void AddRow(PeriodTime? t)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var periodCombo = new ComboBox { Margin = new Thickness(4) };
        for (byte p = 1; p <= 12; p++)
        {
            periodCombo.Items.Add(new ComboBoxItem { Content = PeriodNames.GetFullName(p), Tag = p });
        }
        var period = t?.Period ?? 1;
        periodCombo.SelectedIndex = period - 1;

        var fromBox = new TextBox { Margin = new Thickness(4), Text = t == null ? "07:30" : PeriodTime.FormatMinutes(t.StartMinutes), Padding = new Thickness(4, 3, 4, 3) };
        var toBox = new TextBox { Margin = new Thickness(4), Text = t == null ? "08:15" : PeriodTime.FormatMinutes(t.EndMinutes), Padding = new Thickness(4, 3, 4, 3) };

        var dayCombo = new ComboBox { Margin = new Thickness(4) };
        foreach (var d in DayNames) dayCombo.Items.Add(new ComboBoxItem { Content = d });
        dayCombo.SelectedIndex = t == null ? 0 : t.Day + 1; // -1 -> 0

        var stageCombo = new ComboBox { Margin = new Thickness(4) };
        foreach (var s in StageNames) stageCombo.Items.Add(new ComboBoxItem { Content = s });
        var stageIdx = 0;
        if (t?.Stage != null) stageIdx = Array.IndexOf(StageNames, t.Stage);
        stageCombo.SelectedIndex = stageIdx < 0 ? 0 : stageIdx;

        var breakCheck = new CheckBox
        {
            Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = t?.IsBreak ?? false,
            ToolTip = "اجعل هذه الفترة استراحة (مثل استراحة الغداء) — لا تُجدَّل فيها حصص"
        };

        var delBtn = new Button { Content = "✕", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), ToolTip = "حذف الفترة" };
        delBtn.Click += (_, _) => RowsPanel.Children.Remove(row);

        // تعديل زمن الحصة 5 دقائق أعلى/أسفل مع إزاحة الفترات التالية
        var minusBtn = new Button { Content = "−5", Margin = new Thickness(2, 4, 2, 4), Padding = new Thickness(6, 3, 6, 3), ToolTip = "إنقاص زمن الحصة 5 دقائق (مع تعديل ما بعدها)" };
        var plusBtn = new Button { Content = "+5", Margin = new Thickness(2, 4, 2, 4), Padding = new Thickness(6, 3, 6, 3), ToolTip = "زيادة زمن الحصة 5 دقائق (مع تعديل ما بعدها)" };
        minusBtn.Click += (_, _) => AdjustRowDuration(row, -5);
        plusBtn.Click += (_, _) => AdjustRowDuration(row, +5);

        var adjust = new StackPanel { Orientation = Orientation.Horizontal };
        adjust.Children.Add(minusBtn);
        adjust.Children.Add(plusBtn);

        Grid.SetColumn(periodCombo, 0);
        Grid.SetColumn(fromBox, 1);
        Grid.SetColumn(toBox, 2);
        Grid.SetColumn(dayCombo, 3);
        Grid.SetColumn(stageCombo, 4);
        Grid.SetColumn(breakCheck, 5);
        Grid.SetColumn(adjust, 6);
        Grid.SetColumn(delBtn, 7);

        row.Children.Add(periodCombo);
        row.Children.Add(fromBox);
        row.Children.Add(toBox);
        row.Children.Add(dayCombo);
        row.Children.Add(stageCombo);
        row.Children.Add(breakCheck);
        row.Children.Add(adjust);
        row.Children.Add(delBtn);

        RowsPanel.Children.Add(row);
    }

    /// <summary>
    /// يعدّل زمن الحصة (±5 دقائق) ثم يزحزح بدايات/نهايات الفترات التالية في نفس اليوم والمرحلة.
    /// </summary>
    private void AdjustRowDuration(Grid row, int delta)
    {
        if (row.Children.Count < 8) return;

        var fromBox = (TextBox)row.Children[1];
        var toBox = (TextBox)row.Children[2];
        var dayCombo = (ComboBox)row.Children[3];
        var stageCombo = (ComboBox)row.Children[4];

        if (!PeriodTime.TryParseTime(fromBox.Text, out var start)) return;
        if (!PeriodTime.TryParseTime(toBox.Text, out var end)) return;

        var newEnd = end + delta;
        var duration = newEnd - start;
        if (duration is < 20 or > 120) return;

        toBox.Text = PeriodTime.FormatMinutes(newEnd);

        var index = RowsPanel.Children.IndexOf(row);

        for (var i = index + 1; i < RowsPanel.Children.Count; i++)
        {
            if (RowsPanel.Children[i] is not Grid g || g.Children.Count < 8) continue;
            if (!SameDayAndStage(g, dayCombo, stageCombo)) continue;

            var f = (TextBox)g.Children[1];
            var t = (TextBox)g.Children[2];

            if (PeriodTime.TryParseTime(f.Text, out var fs))
                f.Text = PeriodTime.FormatMinutes(Math.Max(0, fs + delta));
            if (PeriodTime.TryParseTime(t.Text, out var te))
                t.Text = PeriodTime.FormatMinutes(Math.Max(0, te + delta));
        }
    }

    private static bool SameDayAndStage(Grid other, ComboBox dayCombo, ComboBox stageCombo)
    {
        var otherDay = (ComboBox)other.Children[3];
        var otherStage = (ComboBox)other.Children[4];
        return otherDay.SelectedIndex == dayCombo.SelectedIndex
            && otherStage.SelectedIndex == stageCombo.SelectedIndex;
    }

    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        AddRow(null);
    }

    /// <summary>تعبئة القوالب الجاهزة: مشترك (متوسط+ثانوي) / متوسط فقط / ثانوي فقط / افتراضي عام</summary>
    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        var preset = tag switch
        {
            "shared" => SchoolTimingPresets.SharedMiddleSecondary(),
            "middle" => SchoolTimingPresets.MiddleOnly(),
            "secondary" => SchoolTimingPresets.SecondaryOnly(),
            _ => SchoolTimingPresets.DefaultGeneral()
        };
        RowsPanel.Children.Clear();
        foreach (var t in preset) AddRow(t);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var result = new List<PeriodTime>();

        foreach (var child in RowsPanel.Children.OfType<Grid>())
        {
            if (child.Children.Count < 8) continue;
            var periodCombo = (ComboBox)child.Children[0];
            var fromBox = (TextBox)child.Children[1];
            var toBox = (TextBox)child.Children[2];
            var dayCombo = (ComboBox)child.Children[3];
            var stageCombo = (ComboBox)child.Children[4];
            var breakCheck = (CheckBox)child.Children[5];

            var period = (byte)((ComboBoxItem)periodCombo.SelectedItem!).Tag;

            if (!PeriodTime.TryParseTime(fromBox.Text, out var start))
            {
                MessageBox.Show($"قيمة غير صحيحة في حقل \"من\" لـ {PeriodNames.GetFullName(period)}.\nالصيغة: 07:30",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                fromBox.Focus();
                return;
            }
            if (!PeriodTime.TryParseTime(toBox.Text, out var end))
            {
                MessageBox.Show($"قيمة غير صحيحة في حقل \"إلى\" لـ {PeriodNames.GetFullName(period)}.\nالصيغة: 08:15",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                toBox.Focus();
                return;
            }
            if (end <= start)
            {
                MessageBox.Show($"وقت النهاية يجب أن يكون بعد وقت البداية لـ {PeriodNames.GetFullName(period)}",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                toBox.Focus();
                return;
            }

            var dayIdx = dayCombo.SelectedIndex; // 0 = كل الأيام
            var stageSel = ((ComboBoxItem)stageCombo.SelectedItem!).Content?.ToString();

            result.Add(new PeriodTime
            {
                Period = period,
                StartMinutes = start,
                EndMinutes = end,
                Day = dayIdx <= 0 ? -1 : dayIdx - 1,
                Stage = stageSel == null || stageSel == "كل المراحل" ? null : stageSel,
                IsBreak = breakCheck.IsChecked == true
            });
        }

        if (result.Count == 0)
        {
            MessageBox.Show("أضف فترة واحدة على الأقل", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultTimes = result;
        DialogResult = true;
    }
}

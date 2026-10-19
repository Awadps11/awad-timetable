using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace AwadTimetable.UI.Views;

/// <summary>
/// خطة الانتظار بطريقة aSc: يحدد المستخدم لكل (مرحلة، فئة) المعلمين وعدد حصص كل معلم،
/// ثم تُنشأ حصص انتظار غير موضوعة تُجدول عند توليد الجدول.
/// </summary>
public partial class WaitingPlanDialog : Window
{
    private sealed record TeacherItem(Guid Id, string Display);
    private sealed record PlanRow(string Stage, int Tier, string TeacherName, int Periods, Guid TeacherId);

    private readonly Timetable _timetable;
    private readonly ObservableCollection<PlanRow> _rows = new();
    private List<Teacher> _teachers = new();

    public bool Applied { get; private set; }

    public WaitingPlanDialog(Timetable timetable)
    {
        InitializeComponent();
        _timetable = timetable ?? throw new ArgumentNullException(nameof(timetable));

        PlanGrid.ItemsSource = _rows;

        foreach (var cfg in _timetable.StageConfigs)
            StageComboBox.Items.Add(cfg.Stage);
        if (StageComboBox.Items.Count > 0) StageComboBox.SelectedIndex = 0;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            using var scope = App.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            _teachers = (await repo.GetBySchoolAsync(_timetable.SchoolId))
                .OrderBy(t => t.FullName, StringComparer.CurrentCulture)
                .ToList();

            TeacherComboBox.ItemsSource = _teachers.Select(t => new TeacherItem(t.Id, t.FullName)).ToList();
            TeacherComboBox.DisplayMemberPath = "Display";
            if (TeacherComboBox.Items.Count > 0) TeacherComboBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تحميل المعلمين:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (StageComboBox.SelectedItem is not string stage)
        {
            MessageBox.Show("اختر المرحلة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tier = int.TryParse((TierComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var t) ? t : 1;
        if (TeacherComboBox.SelectedItem is not TeacherItem teacher)
        {
            MessageBox.Show("اختر المعلم.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(PeriodsTextBox.Text, out var periods) || periods < 1)
        {
            MessageBox.Show("أدخل عدد حصص صحيحاً (1 أو أكثر).", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // إن كان المعلم موجوداً في نفس (المرحلة، الفئة) نجمع الحصص
        var existing = _rows.FirstOrDefault(r => r.Stage == stage && r.Tier == tier && r.TeacherId == teacher.Id);
        if (existing is not null)
        {
            var idx = _rows.IndexOf(existing);
            _rows[idx] = existing with { Periods = existing.Periods + periods };
        }
        else
        {
            _rows.Add(new PlanRow(stage, tier, teacher.Display, periods, teacher.Id));
        }

        UpdateSummary();
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (PlanGrid.SelectedItem is PlanRow row)
        {
            _rows.Remove(row);
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var parts = _rows
            .GroupBy(r => (r.Stage, r.Tier))
            .Select(g => $"{g.Key.Stage} فئة {g.Key.Tier}: {g.Sum(r => r.Periods)} حصة")
            .ToList();

        SummaryText.Text = parts.Count == 0 ? "لا بنود بعد." : string.Join("  |  ", parts);
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var entries = _rows.Select(r => new WaitingPlanEntry(r.Stage, r.Tier, r.TeacherId, r.Periods)).ToList();
        var errors = WaitingPlan.Validate(_timetable, entries);

        if (errors.Count > 0)
        {
            MessageBox.Show("راجع الخطة:\n• " + string.Join("\n• ", errors), "خطة الانتظار",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var scope = App.Services.CreateScope();
            var subjectRepo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
            var classRepo = scope.ServiceProvider.GetRequiredService<IClassRepository>();

            var subjects = subjectRepo.GetBySchoolAsync(_timetable.SchoolId).GetAwaiter().GetResult().ToList();
            var classes = classRepo.GetBySchoolAsync(_timetable.SchoolId).GetAwaiter().GetResult().ToList();

            Subject GetOrCreateSubject(int tier)
            {
                var name = WaitingPlan.SubjectName(tier);
                var s = subjects.FirstOrDefault(x => x.Name == name);
                if (s is not null) return s;
                s = new Subject(_timetable.SchoolId, name, $"انتظار{tier}");
                subjectRepo.AddAsync(s).GetAwaiter().GetResult();
                subjects.Add(s);
                return s;
            }

            Class GetOrCreateClass(string stage, int tier)
            {
                var name = WaitingPlan.ClassName(stage, tier);
                var c = classes.FirstOrDefault(x => x.Name == name);
                if (c is not null) return c;
                var grade = stage == "متوسط" ? 7 : stage == "ثانوي" ? 10 : 1;
                c = new Class(_timetable.SchoolId, name, grade, 'أ');
                c.SetShortName($"انتظار {tier}");
                classRepo.AddAsync(c).GetAwaiter().GetResult();
                classes.Add(c);
                return c;
            }

            var teacherById = _teachers.ToDictionary(t => t.Id, t => t);
            var created = 0;

            // إزالة حصص الانتظار السابقة لهذه المواد
            var waitingSubjectIds = _rows.Select(r => WaitingPlan.SubjectName(r.Tier)).Distinct()
                .Select(n => subjects.FirstOrDefault(s => s.Name == n)?.Id)
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .ToHashSet();

            foreach (var old in _timetable.Lessons.Where(l => waitingSubjectIds.Contains(l.SubjectId.Value)).ToList())
                _timetable.RemoveLesson(old);

            foreach (var entry in entries)
            {
                if (!teacherById.TryGetValue(entry.TeacherId, out var teacher)) continue;

                var subject = GetOrCreateSubject(entry.Tier);
                var cls = GetOrCreateClass(entry.Stage, entry.Tier);

                for (var i = 0; i < entry.Periods; i++)
                {
                    var lesson = new Lesson((TimetableId)_timetable.Id, (SubjectId)subject.Id,
                        (TeacherId)teacher.Id, (ClassId)cls.Id, AwadTimetable.Core.Enums.DayOfWeek.Saturday,
                        new PeriodNumber(0)); // غير موضوعة: تُجدول عند التوليد
                    lesson.AttachReferences(subject, teacher, cls);
                    _timetable.AddLesson(lesson);
                    created++;
                }
            }

            Applied = true;
            MessageBox.Show(
                $"أُنشئت {created} حصة انتظار غير موضوعة.\nولّد الجدول لتوزيعها على الشعب وإظهارها في الصفوف.",
                "خطة الانتظار", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر توليد حصص الانتظار:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

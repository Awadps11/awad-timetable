using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AwadTimetable.UI.Views;

/// <summary>
/// صف غياب في قائمة المعلمين (ربط مباشر مع DataGrid)
/// </summary>
public sealed class AbsentRow
{
    public Teacher Teacher { get; }
    public bool IsAbsent { get; set; }
    public string FullName => Teacher.FullName;
    public string Sub1Name { get; }
    public string Sub2Name { get; }
    public string Sub3Name { get; }

    public AbsentRow(Teacher teacher, IReadOnlyDictionary<Guid, Teacher> byId)
    {
        Teacher = teacher;
        Sub1Name = NameOf(teacher.Substitute1TeacherId, byId);
        Sub2Name = NameOf(teacher.Substitute2TeacherId, byId);
        Sub3Name = NameOf(teacher.Substitute3TeacherId, byId);
    }

    private static string NameOf(Guid? id, IReadOnlyDictionary<Guid, Teacher> byId)
        => id.HasValue && byId.TryGetValue(id.Value, out var t) ? t.FullName : "—";
}

/// <summary>صف نتيجة يظهر في جدول النتائج</summary>
public sealed class ResultRow
{
    public byte Period { get; init; }
    public string ClassName { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public string AbsentName { get; init; } = "";
    public string SubstituteName { get; init; } = "";
    public string TierLabel { get; init; } = "";
    public string Status { get; init; } = "";
    public SubstitutionAssignmentRow Raw { get; init; } = null!;
}

/// <summary>
/// نافذة البديل والانتظار: توليد تعيينات المعلمين البديلين بثلاث فئات أولوية
/// </summary>
public partial class SubstitutionWindow : Window
{
    private readonly IReadOnlyList<Teacher> _teachers;
    private readonly IReadOnlyDictionary<Guid, Teacher> _teacherById;
    private readonly IReadOnlyDictionary<Guid, Class> _classById;
    private readonly IReadOnlyDictionary<Guid, Subject> _subjectById;
    private readonly Timetable? _timetable;
    private readonly SubstitutionStorageService _storage;
    private List<SubstitutionAssignmentRow> _lastResults = new();

    public SubstitutionWindow(
        IReadOnlyList<Teacher> teachers,
        IReadOnlyList<Class> classes,
        IReadOnlyList<Subject> subjects,
        Timetable? timetable)
    {
        InitializeComponent();

        _teachers = teachers;
        _teacherById = teachers.ToDictionary(t => t.Id);
        _classById = classes.ToDictionary(c => c.Id);
        _subjectById = subjects.ToDictionary(s => s.Id);
        _timetable = timetable;
        _storage = App.Services.GetRequiredService<SubstitutionStorageService>();

        DatePicker.SelectedDate = DateTime.Today;

        foreach (var teacher in teachers.Where(t => t.IsActive).OrderBy(t => t.LastName).ThenBy(t => t.FirstName))
        {
            TeachersGrid.Items.Add(new AbsentRow(teacher, _teacherById));
        }

        DatePicker.SelectedDateChanged += OnDateChanged;
        Loaded += async (_, _) => await LoadSavedDayAsync();
    }

    private async void OnDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        await LoadSavedDayAsync();
    }

    private async System.Threading.Tasks.Task LoadSavedDayAsync()
    {
        try
        {
            if (DatePicker.SelectedDate == null) return;
            var date = DateOnly.FromDateTime(DatePicker.SelectedDate.Value);
            var saved = await _storage.LoadDayAsync(date);

            ResultsGrid.Items.Clear();
            foreach (var s in saved)
            {
                ResultsGrid.Items.Add(new ResultRow
                {
                    Period = s.Period,
                    ClassName = _classById.TryGetValue(s.ClassId, out var c) ? c.FullName : s.ClassId.ToString("N")[..8],
                    SubjectName = _subjectById.TryGetValue(s.SubjectId, out var sub) ? sub.Name : "—",
                    AbsentName = _teacherById.TryGetValue(s.AbsentTeacherId, out var a) ? a.FullName : "—",
                    SubstituteName = s.SubstituteTeacherId.HasValue && _teacherById.TryGetValue(s.SubstituteTeacherId.Value, out var r) ? r.FullName : "—",
                    TierLabel = TierText(s.Tier),
                    Status = s.Status,
                    Raw = new SubstitutionAssignmentRow(s.LessonId, (AwadTimetable.Core.Enums.DayOfWeek)s.Day, s.Period, s.ClassId, s.SubjectId, s.AbsentTeacherId, s.SubstituteTeacherId, s.Tier, s.Status)
                });
            }

            StatusText.Text = saved.Count > 0
                ? $"توجد {saved.Count} تعيين محفوظ مسبقاً في هذا التاريخ."
                : "لا توجد تعيينات محفوظة في هذا التاريخ.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تعذر تحميل التعيينات المحفوظة: {ex.Message}";
        }
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_timetable == null)
            {
                MessageBox.Show("أنشئ جدولًا وتولّده أولاً — التوليد يحتاج جدولًا حاضراً بالحصص", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DatePicker.SelectedDate == null)
            {
                MessageBox.Show("اختر تاريخ الغياب أولاً", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var date = DateOnly.FromDateTime(DatePicker.SelectedDate.Value);
            var dayIndex = SubstitutionGenerator.GetTimetableDayIndex(date);
            if (dayIndex > 5)
            {
                MessageBox.Show("يوم الجمعة لا يحتوي حصصاً في الجدول", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var absentIds = TeachersGrid.Items
                .Cast<AbsentRow>()
                .Where(r => r.IsAbsent)
                .Select(r => r.Teacher.Id)
                .ToList();

            if (absentIds.Count == 0)
            {
                MessageBox.Show("حدد المعلمين الغائبين من القائمة أولاً", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rows = SubstitutionGenerator.Generate(_timetable, _teachers, absentIds, date);
            _lastResults = rows.ToList();

            ResultsGrid.Items.Clear();
            foreach (var row in _lastResults)
            {
                ResultsGrid.Items.Add(new ResultRow
                {
                    Period = row.Period,
                    ClassName = _classById.TryGetValue(row.ClassId, out var c) ? c.FullName : "—",
                    SubjectName = _subjectById.TryGetValue(row.SubjectId, out var s) ? s.Name : "—",
                    AbsentName = _teacherById.TryGetValue(row.AbsentTeacherId, out var a) ? a.FullName : "—",
                    SubstituteName = row.SubstituteTeacherId.HasValue && _teacherById.TryGetValue(row.SubstituteTeacherId.Value, out var r) ? r.FullName : "—",
                    TierLabel = TierText(row.Tier),
                    Status = row.Status,
                    Raw = row
                });
            }

            var assigned = _lastResults.Count(r => r.SubstituteTeacherId.HasValue);
            var missing = _lastResults.Count - assigned;
            StatusText.Text = $"تم تعيين {assigned} بديل، بدون بديل {missing}. اضغط «حفظ التعيينات» للتخزين.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"فشل توليد التعيينات:\n{ex.Message}", "خطأ",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastResults.Count == 0)
            {
                MessageBox.Show("لا توجد نتائج لتوليدها — اضغط «توليد التعيينات» أولاً", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DatePicker.SelectedDate == null || _timetable == null) return;

            var date = DateOnly.FromDateTime(DatePicker.SelectedDate.Value);
            await _storage.SaveDayAsync(date, _timetable.Id, _lastResults);

            MessageBox.Show($"تم حفظ {_lastResults.Count} تعيين بديل لتاريخ اليوم المحدد", "تم",
                MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = $"تم حفظ {_lastResults.Count} تعيين.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"فشل حفظ التعيينات:\n{ex.Message}", "خطأ",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnClearClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DatePicker.SelectedDate == null) return;

            var confirm = MessageBox.Show("هل تريد حذف كل تعيينات هذا اليوم؟", "تأكيد الحذف",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var date = DateOnly.FromDateTime(DatePicker.SelectedDate.Value);
            await _storage.ClearDayAsync(date);

            _lastResults = new List<SubstitutionAssignmentRow>();
            ResultsGrid.Items.Clear();
            StatusText.Text = "تم حذف تعيينات اليوم.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"فشل حذف التعيينات:\n{ex.Message}", "خطأ",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string TierText(int? tier) => tier switch
    {
        1 => "الأول",
        2 => "الثاني",
        3 => "الثالث",
        _ => "—"
    };
}

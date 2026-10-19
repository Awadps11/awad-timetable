using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;
using CoreDuration = AwadTimetable.Core.ValueObjects.Duration;

namespace AwadTimetable.UI.Views;

/// <summary>
/// نافذة إضافة حصة جديدة
/// </summary>
public partial class LessonDialog : Window
{
    private readonly Timetable _timetable;

    public Lesson? CreatedLesson { get; private set; }

    public LessonDialog(
        Timetable timetable,
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<Teacher> teachers,
        IReadOnlyList<Class> classes,
        IReadOnlyList<Room> rooms)
    {
        InitializeComponent();
        _timetable = timetable;

        SubjectComboBox.ItemsSource = subjects;
        TeacherComboBox.ItemsSource = teachers;
        ClassComboBox.ItemsSource = classes;
        ClassComboBox.SelectionChanged += (_, _) => RebuildPeriodItems();
        RoomComboBox.ItemsSource = rooms;

        // أيام الأسبوع
        DayComboBox.Items.Add(new ComboBoxItem { Content = "السبت", Tag = (byte)0 });
        DayComboBox.Items.Add(new ComboBoxItem { Content = "الأحد", Tag = (byte)1 });
        DayComboBox.Items.Add(new ComboBoxItem { Content = "الاثنين", Tag = (byte)2 });
        DayComboBox.Items.Add(new ComboBoxItem { Content = "الثلاثاء", Tag = (byte)3 });
        DayComboBox.Items.Add(new ComboBoxItem { Content = "الأربعاء", Tag = (byte)4 });
        DayComboBox.Items.Add(new ComboBoxItem { Content = "الخميس", Tag = (byte)5 });
        DayComboBox.Items.Add(new ComboBoxItem { Content = "الجمعة", Tag = (byte)6 });
        DayComboBox.SelectedIndex = 0;

        // الفترات (1-12) مع التوقيت حسب مرحلة الفصل المختار
        RebuildPeriodItems();
        PeriodComboBox.SelectedIndex = 0;

        // المدد المسموحة (30-120 بمضاعفات 5)
        foreach (var minutes in new[] { 45, 60, 90, 120 })
        {
            DurationComboBox.Items.Add(new ComboBoxItem { Content = $"{minutes} دقيقة", Tag = minutes });
        }
        DurationComboBox.SelectedIndex = 0;

        if (subjects.Count > 0) SubjectComboBox.SelectedIndex = 0;
        if (teachers.Count > 0) TeacherComboBox.SelectedIndex = 0;
        if (classes.Count > 0) ClassComboBox.SelectedIndex = 0;
    }

    /// <summary>إعادة بناء قائمة الفترات بتوقيت مرحلة الفصل المختار (في المدارس المشتركة لكل مرحلة توقيتها)</summary>
    private void RebuildPeriodItems()
    {
        var previous = PeriodComboBox.SelectedItem is ComboBoxItem prev ? (byte?)prev.Tag : null;
        PeriodComboBox.Items.Clear();

        var stage = ClassComboBox.SelectedItem is Class classRoom
            ? StageHelper.GetStage(classRoom.GradeLevel, classRoom.Name)
            : null;

        for (byte p = 1; p <= 12; p++)
        {
            var t = stage == null
                ? _timetable.GetPeriodTimeForDisplay(p)
                : _timetable.GetPeriodTime(p, -1, stage) ?? _timetable.GetPeriodTimeForDisplay(p);
            var label = t == null
                ? PeriodNames.GetFullName(p)
                : $"{PeriodNames.GetFullName(p)} ({t.RangeText})";
            PeriodComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = p });
        }

        if (previous is byte keep && keep >= 1 && keep <= 12)
            PeriodComboBox.SelectedIndex = keep - 1;
        else if (PeriodComboBox.Items.Count > 0)
            PeriodComboBox.SelectedIndex = 0;
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (SubjectComboBox.SelectedItem is not Subject subject)
        {
            MessageBox.Show("اختر المادة أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (TeacherComboBox.SelectedItem is not Teacher teacher)
        {
            MessageBox.Show("اختر المعلم أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (ClassComboBox.SelectedItem is not Class classRoom)
        {
            MessageBox.Show("اختر الفصل أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dayTag = (byte)((ComboBoxItem)DayComboBox.SelectedItem!).Tag;
        var periodTag = (byte)((ComboBoxItem)PeriodComboBox.SelectedItem!).Tag;
        var durationTag = (int)((ComboBoxItem)DurationComboBox.SelectedItem!).Tag;

        try
        {
            var lesson = new Lesson(
                (TimetableId)_timetable.Id,
                (SubjectId)subject.Id,
                (TeacherId)teacher.Id,
                (ClassId)classRoom.Id,
                (DomainDayOfWeek)dayTag,
                PeriodNumber.Create(periodTag),
                CoreDuration.FromMinutes(durationTag));

            if (RoomComboBox.SelectedItem is Room room)
            {
                lesson.AssignRoom((RoomId)room.Id);
            }

            var notes = NotesTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(notes))
            {
                lesson.SetNotes(notes);
            }

            CreatedLesson = lesson;
            DialogResult = true;
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"تعذر إنشاء الحصة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

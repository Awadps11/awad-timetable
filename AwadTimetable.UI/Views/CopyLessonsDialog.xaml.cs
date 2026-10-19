using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;

namespace AwadTimetable.UI.Views;

/// <summary>نتيجة حوار نسخ/نقل الحصص</summary>
public sealed record CopyLessonsRequest(string Mode, Guid SourceId, Guid TargetId, bool ReplaceTarget);

/// <summary>
/// حوار نسخ حصص فصل إلى فصل آخر (استبدال/إضافة) أو نقل حصص معلم إلى معلم آخر (بديل)
/// </summary>
public partial class CopyLessonsDialog : Window
{
    private readonly IReadOnlyList<Class> _classes;
    private readonly IReadOnlyList<Teacher> _teachers;

    public CopyLessonsRequest? Result { get; private set; }

    public CopyLessonsDialog(IReadOnlyList<Class> classes, IReadOnlyList<Teacher> teachers)
    {
        InitializeComponent();
        _classes = classes;
        _teachers = teachers;
        PopulateCombos();
    }

    private string CurrentMode =>
        (ModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "class";

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        PopulateCombos();
    }

    private void PopulateCombos()
    {
        var isClass = CurrentMode == "class";
        SourceLabel.Text = isClass ? "الفصل المصدر:" : "المعلم المصدر:";
        TargetLabel.Text = isClass ? "الفصل الهدف:" : "المعلم الهدف:";
        ReplaceCheckBox.IsEnabled = isClass;

        if (isClass)
        {
            SourceComboBox.ItemsSource = _classes;
            TargetComboBox.ItemsSource = _classes;
        }
        else
        {
            SourceComboBox.ItemsSource = _teachers;
            TargetComboBox.ItemsSource = _teachers;
        }

        if (SourceComboBox.Items.Count > 0) SourceComboBox.SelectedIndex = 0;
        // الهدف الافتراضي = الثاني حتى لا يُختار المصدر نفسه بالخطأ
        if (TargetComboBox.Items.Count > 1) TargetComboBox.SelectedIndex = 1;
        else if (TargetComboBox.Items.Count > 0) TargetComboBox.SelectedIndex = 0;
    }

    private void OnExecuteClick(object sender, RoutedEventArgs e)
    {
        Guid? sourceId = SelectedId(SourceComboBox);
        Guid? targetId = SelectedId(TargetComboBox);

        if (sourceId == null || targetId == null)
        {
            MessageBox.Show("اختر المصدر والهدف من القوائم.", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sourceId == targetId)
        {
            MessageBox.Show("لا يمكن اختيار المصدر والهدف نفسيهما — اختر هدفاً مختلفاً.", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new CopyLessonsRequest(CurrentMode, sourceId.Value, targetId.Value, ReplaceCheckBox.IsChecked == true);
        DialogResult = true;
        Close();
    }

    private static Guid? SelectedId(ComboBox combo)
    {
        return combo.SelectedItem switch
        {
            Class c => c.Id,
            Teacher t => t.Id,
            _ => null,
        };
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

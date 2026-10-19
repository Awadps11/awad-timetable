using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Data.Repositories;
using AwadTimetable.UI.Services;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// نموذج عرض إدارة الفصول
/// </summary>
public partial class ClassManagementViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSchoolService _school;

    [ObservableProperty]
    private ObservableCollection<Class> _classes = new();

    [ObservableProperty]
    private Class? _selectedClass;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // حقول الإضافة
    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private int _newGradeLevel = 1;

    [ObservableProperty]
    private string _newSection = "أ";

    [ObservableProperty]
    private int _newStudentCount = 0;

    [ObservableProperty]
    private string _newShortName = string.Empty;

    // تعديل الفصل المحدد
    [ObservableProperty]
    private string _editShortName = string.Empty;

    [ObservableProperty]
    private int _editStudentCount = 0;

    partial void OnSelectedClassChanged(Class? value)
    {
        if (value is null) return;
        EditShortName = value.ShortName ?? string.Empty;
        EditStudentCount = value.StudentCount;
    }

    /// <summary>تطبيق الاختصار وعدد الطلاب على الفصل المحدد</summary>
    [RelayCommand]
    private async Task ApplyEditAsync()
    {
        if (SelectedClass is null)
        {
            MessageBox.Show("اختر فصلاً أولاً", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SelectedClass.SetShortName(EditShortName);
            SelectedClass.SetStudentCount(EditStudentCount);

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IClassRepository>();
            await repo.UpdateAsync(SelectedClass);

            // تحديث العرض في القائمة
            var idx = Classes.IndexOf(SelectedClass);
            if (idx >= 0) { Classes[idx] = SelectedClass; }
            MessageBox.Show("تم تحديث بيانات الفصل.", "الفصول", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تحديث الفصل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public ClassManagementViewModel(IServiceScopeFactory scopeFactory, CurrentSchoolService school)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _school = school ?? throw new ArgumentNullException(nameof(school));
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IClassRepository>();
            var schoolId = await _school.GetSchoolIdAsync();
            var list = await repo.GetBySchoolAsync(schoolId);
            if (_school.Current is { } current && current != schoolId) return; // تغيّرت مساحة العمل أثناء التحميل

            Classes.Clear();
            foreach (var @class in list.OrderBy(c => c.GradeLevel).ThenBy(c => c.Section))
            {
                Classes.Add(@class);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تحميل قائمة الفصول:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            MessageBox.Show("أدخل اسم الفصل", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IClassRepository>();
            var schoolId = await _school.GetSchoolIdAsync();

            var section = string.IsNullOrWhiteSpace(NewSection) ? 'أ' : NewSection.Trim()[0];
            var @class = new Class(schoolId, NewName.Trim(), NewGradeLevel, section);
            @class.UpdateBasicInfo(@class.Name,
                string.IsNullOrWhiteSpace(NewShortName) ? null : NewShortName.Trim(),
                null, NewStudentCount);

            await repo.AddAsync(@class);

            Classes.Add(@class);
            NewName = string.Empty;
            NewShortName = string.Empty;
            NewStudentCount = 0;
            SelectedClass = @class;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر إضافة الفصل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedClass == null)
        {
            MessageBox.Show("اختر فصلًا أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"هل تريد حذف الفصل \"{SelectedClass.Name}\"؟",
            "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IClassRepository>();
            await repo.DeleteAsync(SelectedClass.Id);

            Classes.Remove(SelectedClass);
            SelectedClass = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر حذف الفصل:\n{ex.Message}\n(قد يكون مرتبطًا بحصص موجودة)", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }
    // ===== إجراءات جماعية =====
    public async Task DeleteManyAsync(IReadOnlyList<Class> selected)
    {
        if (selected.Count == 0) return;
        if (MessageBox.Show($"حذف {selected.Count} فصل؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        int deleted = 0, failed = 0;
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IClassRepository>();

        foreach (var item in selected.ToList())
        {
            try { await repo.DeleteAsync(item.Id); Classes.Remove(item); deleted++; }
            catch { failed++; }
        }

        MessageBox.Show($"تم حذف {deleted}." + (failed > 0 ? $" تعذر {failed}." : ""), "إجراء جماعي", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public Task ExportManyAsync(IReadOnlyList<Class> selected)
    {
        if (selected.Count == 0) return Task.CompletedTask;
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "الفصول.csv", Title = "تصدير الفصول" };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;

        var rows = selected.Select(c => new ComponentRow(c.Name, c.DisplayShortName, c.StudentCount));
        File.WriteAllText(dialog.FileName, ComponentTransfer.ToCsv(rows), new UTF8Encoding(true));
        return Task.CompletedTask;
    }

    public void CopyMany(IReadOnlyList<Class> selected)
    {
        if (selected.Count == 0) return;
        Clipboard.SetText(string.Join("\n", selected.Select(c => c.DisplayShortName)));
    }
}

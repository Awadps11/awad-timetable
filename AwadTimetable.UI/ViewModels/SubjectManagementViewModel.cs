using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
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
/// نموذج عرض إدارة المواد
/// </summary>
public partial class SubjectManagementViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSchoolService _school;

    [ObservableProperty]
    private ObservableCollection<Subject> _subjects = new();

    [ObservableProperty]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // حقول الإضافة
    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newShortName = string.Empty;

    [ObservableProperty]
    private int _newPeriodsPerWeek = 4;

    public SubjectManagementViewModel(IServiceScopeFactory scopeFactory, CurrentSchoolService school)
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
            var repo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
            var schoolId = await _school.GetSchoolIdAsync();
            var list = await repo.GetBySchoolAsync(schoolId);
            if (_school.Current is { } current && current != schoolId) return; // تغيّرت مساحة العمل أثناء التحميل

            Subjects.Clear();
            foreach (var subject in list.OrderBy(s => s.Name))
            {
                Subjects.Add(subject);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تحميل قائمة المواد:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("أدخل اسم المادة", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
            var schoolId = await _school.GetSchoolIdAsync();

            var subject = new Subject(schoolId, NewName.Trim(),
                string.IsNullOrWhiteSpace(NewShortName) ? null : NewShortName.Trim());
            subject.UpdateSchedule(NewPeriodsPerWeek, 1, Math.Max(NewPeriodsPerWeek, 10));

            await repo.AddAsync(subject);

            Subjects.Add(subject);
            NewName = string.Empty;
            NewShortName = string.Empty;
            NewPeriodsPerWeek = 4;
            SelectedSubject = subject;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر إضافة المادة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedSubject == null)
        {
            MessageBox.Show("اختر مادة أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"هل تريد حذف المادة \"{SelectedSubject.Name}\"؟",
            "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
            await repo.DeleteAsync(SelectedSubject.Id);

            Subjects.Remove(SelectedSubject);
            SelectedSubject = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر حذف المادة:\n{ex.Message}\n(قد تكون مرتبطه بحصص موجودة)", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    /// <summary>استيراد المواد من ملف Excel (.xlsx) أو CSV</summary>
    [RelayCommand]
    private async Task ImportFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel أو CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|جميع الملفات (*.*)|*.*",
            Title = "استيراد المواد",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IReadOnlyList<SubjectImportRow> rows;
            if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                rows = SubjectListImporter.ParseCsv(ReadTextWithEncoding(dialog.FileName));
            else
            {
                using var fs = File.OpenRead(dialog.FileName);
                rows = SubjectListImporter.ParseXlsx(fs);
            }

            await AddSubjectRowsAsync(rows, "الملف");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"فشل استيراد المواد:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>استيراد المواد من صورة بمساعدة الذكاء الاصطناعي</summary>
    [RelayCommand]
    private async Task ImportFromImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "صور (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|جميع الملفات (*.*)|*.*",
            Title = "استيراد المواد من صورة",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var vision = scope.ServiceProvider.GetRequiredService<IAiVisionService>();
            var rows = await vision.ExtractSubjectsFromImageAsync(dialog.FileName);
            await AddSubjectRowsAsync(rows, "الصورة");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"فشل استيراد المواد من الصورة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddSubjectRowsAsync(IReadOnlyList<SubjectImportRow> rows, string sourceLabel)
    {
        if (rows.Count == 0)
        {
            MessageBox.Show($"لم يُعثر على مواد في {sourceLabel}.", "استيراد المواد", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existing = Subjects.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0, skipped = 0;

        using var scope2 = _scopeFactory.CreateScope();
        var repo = scope2.ServiceProvider.GetRequiredService<ISubjectRepository>();
        var schoolId = await _school.GetSchoolIdAsync();

        foreach (var row in rows)
        {
            if (!existing.Add(row.Name)) { skipped++; continue; }

            var subject = new Subject(schoolId, row.Name, row.ShortName);
            var periods = row.PeriodsPerWeek ?? 4;
            subject.UpdateSchedule(periods, 1, Math.Max(periods, 10));
            await repo.AddAsync(subject);
            Subjects.Add(subject);
            added++;
        }

        MessageBox.Show(
            $"تمت إضافة {added} مادة من {sourceLabel}." + (skipped > 0 ? $"\nتم تخطي {skipped} (مكررة)." : string.Empty),
            "استيراد المواد", MessageBoxButton.OK,
            added > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>قراءة نص CSV مع استشعار الترميز (UTF-8 أو windows-1256 للعربية)</summary>
    private static string ReadTextWithEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch
        {
            try { return Encoding.GetEncoding(1256).GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }
    }
    // ===== إجراءات جماعية =====
    public async Task DeleteManyAsync(IReadOnlyList<Subject> selected)
    {
        if (selected.Count == 0) return;
        if (MessageBox.Show($"حذف {selected.Count} مادة؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        int deleted = 0, failed = 0;
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();

        foreach (var item in selected.ToList())
        {
            try { await repo.DeleteAsync(item.Id); Subjects.Remove(item); deleted++; }
            catch { failed++; }
        }

        MessageBox.Show($"تم حذف {deleted}." + (failed > 0 ? $" تعذر {failed}." : ""), "إجراء جماعي", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public Task ExportManyAsync(IReadOnlyList<Subject> selected)
    {
        if (selected.Count == 0) return Task.CompletedTask;
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "المواد.csv", Title = "تصدير المواد" };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;

        var rows = selected.Select(s => new ComponentRow(s.Name, s.ShortName, s.DefaultPeriodsPerWeek));
        File.WriteAllText(dialog.FileName, ComponentTransfer.ToCsv(rows), new UTF8Encoding(true));
        return Task.CompletedTask;
    }

    public void CopyMany(IReadOnlyList<Subject> selected)
    {
        if (selected.Count == 0) return;
        Clipboard.SetText(string.Join("\n", selected.Select(s => s.Name)));
    }
}

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
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Data.Repositories;
using AwadTimetable.UI.Services;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// نموذج عرض إدارة المعلمين
/// </summary>
public partial class TeacherManagementViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSchoolService _school;
    private readonly TeacherAppearanceService _appearance;
    private readonly ILogger<TeacherManagementViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<Teacher> _teachers = new();

    [ObservableProperty]
    private Teacher? _selectedTeacher;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // حقول الإضافة
    [ObservableProperty]
    private string _newDisplayName = string.Empty;

    [ObservableProperty]
    private string _newFirstName = string.Empty;

    [ObservableProperty]
    private string _newLastName = string.Empty;

    [ObservableProperty]
    private string _newSpecialization = string.Empty;

    // مظهر المعلم المحدد (اسم مختصر + لون مخصص)
    [ObservableProperty]
    private string _editShortName = string.Empty;

    [ObservableProperty]
    private string _editColorHex = string.Empty;

    // البديلون (نظام الانتظار بثلاث فئات)
    [ObservableProperty]
    private ObservableCollection<Teacher> _subCandidates = new();

    [ObservableProperty]
    private Teacher? _selectedSub1;

    [ObservableProperty]
    private Teacher? _selectedSub2;

    [ObservableProperty]
    private Teacher? _selectedSub3;

    partial void OnSelectedTeacherChanged(Teacher? value)
    {
        RefreshSubCandidates();

        var entry = value == null ? null : _appearance.Get(value.Id);
        EditShortName = entry?.ShortName ?? string.Empty;
        EditColorHex = entry?.ColorHex ?? string.Empty;
    }

    [RelayCommand]
    private void SaveAppearance()
    {
        if (SelectedTeacher == null)
        {
            MessageBox.Show("اختر معلمًا أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(EditColorHex) && !IsValidColor(EditColorHex))
        {
            MessageBox.Show("لون غير صالح. استخدم صيغة مثل #2563EB", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _appearance.Set(SelectedTeacher.Id, EditShortName, EditColorHex);
        SelectedTeacher.SetAppearance(EditShortName, EditColorHex);

        _logger.LogInformation("حُفظ مظهر المعلم {Teacher}: مختصر={Short} لون={Color}",
            SelectedTeacher.FullName, EditShortName, EditColorHex);

        MessageBox.Show("تم حفظ الاسم المختصر واللون للمعلم", "تم", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool IsValidColor(string hex)
    {
        try
        {
            System.Windows.Media.ColorConverter.ConvertFromString(hex.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshSubCandidates()
    {
        SubCandidates.Clear();
        SelectedSub1 = null;
        SelectedSub2 = null;
        SelectedSub3 = null;

        if (SelectedTeacher == null) return;

        foreach (var t in Teachers.Where(x => x.Id != SelectedTeacher.Id).OrderBy(x => x.LastName).ThenBy(x => x.FirstName))
        {
            SubCandidates.Add(t);
        }

        SelectedSub1 = SubCandidates.FirstOrDefault(t => t.Id == SelectedTeacher.Substitute1TeacherId);
        SelectedSub2 = SubCandidates.FirstOrDefault(t => t.Id == SelectedTeacher.Substitute2TeacherId);
        SelectedSub3 = SubCandidates.FirstOrDefault(t => t.Id == SelectedTeacher.Substitute3TeacherId);
    }

    [RelayCommand]
    private async Task SaveSubstitutesAsync()
    {
        if (SelectedTeacher == null)
        {
            MessageBox.Show("اختر معلمًا أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var subIds = new[] { SelectedSub1?.Id, SelectedSub2?.Id, SelectedSub3?.Id };
        if (subIds.Distinct().Count() != subIds.Count(id => id.HasValue))
        {
            MessageBox.Show("لا يمكن تعيين المعلم نفسه في أكثر من فئة بديل", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();

            var fresh = await repo.GetByIdAsync(SelectedTeacher.Id);
            if (fresh == null)
            {
                MessageBox.Show("لم يعد هذا المعلم موجوداً في قاعدة البيانات", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            fresh.UpdateSubstitutes(SelectedSub1?.Id, SelectedSub2?.Id, SelectedSub3?.Id);
            await repo.UpdateAsync(fresh);

            SelectedTeacher.UpdateSubstitutes(SelectedSub1?.Id, SelectedSub2?.Id, SelectedSub3?.Id);

            _logger.LogInformation("حُفظت بديلات المعلم {Teacher}: أول={S1} ثان={S2} ثالث={S3}",
                fresh.FullName,
                SelectedSub1?.FullName ?? "—",
                SelectedSub2?.FullName ?? "—",
                SelectedSub3?.FullName ?? "—");

            MessageBox.Show("تم حفظ فئات البديل للمعلم بنجاح", "تم", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر حفظ البديلات:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public TeacherManagementViewModel(
        IServiceScopeFactory scopeFactory,
        CurrentSchoolService school,
        TeacherAppearanceService appearance,
        ILogger<TeacherManagementViewModel> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _school = school ?? throw new ArgumentNullException(nameof(school));
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            var schoolId = await _school.GetSchoolIdAsync();
            var list = await repo.GetBySchoolAsync(schoolId);
            if (_school.Current is { } current && current != schoolId) return; // تغيّرت مساحة العمل أثناء التحميل
            _appearance.ApplyAll(list);

            Teachers.Clear();
            foreach (var teacher in list.OrderBy(t => t.LastName).ThenBy(t => t.FirstName))
            {
                Teachers.Add(teacher);
            }
            _logger.LogInformation("تم تحميل {Count} معلم", Teachers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في تحميل المعلمين");
            MessageBox.Show($"تعذر تحميل قائمة المعلمين:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>ألوان جاهزة لبطاقات المعلمين (يُختار أول لون غير مستخدم).</summary>
    private static readonly string[] ColorPalette =
    {
        "#2563EB", "#DC2626", "#16A34A", "#D97706", "#7C3AED", "#0891B2",
        "#DB2777", "#4B5563", "#65A30D", "#EA580C", "#0D9488", "#9333EA",
        "#0284C7", "#B91C1C", "#15803D", "#A16207", "#6D28D9", "#BE185D",
        "#0F766E", "#854D0E", "#1D4ED8", "#9F1239", "#3F6212", "#7E22CE",
    };

    /// <summary>يرجع أول لون غير مستخدم بين المعلمين الحاليين.</summary>
    private string PickUnusedColor()
    {
        var used = Teachers
            .Select(t => t.CustomColorHex)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var color in ColorPalette)
        {
            if (!used.Contains(color.ToUpperInvariant())) return color;
        }

        while (true)
        {
            var random = $"#{Random.Shared.Next(0x10, 0xF0):X2}{Random.Shared.Next(0x10, 0xF0):X2}{Random.Shared.Next(0x10, 0xF0):X2}";
            if (!used.Contains(random.ToUpperInvariant())) return random;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var name = NewDisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("أدخل اسم المعلم المختصر", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Teachers.Any(t => string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("يوجد معلم بهذا الاسم بالفعل", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            var schoolId = await _school.GetSchoolIdAsync();

            var teacher = new Teacher(schoolId, name, string.Empty);

            var color = PickUnusedColor();
            teacher.SetAppearance(name, color);
            _appearance.Set(teacher.Id, name, color);

            await repo.AddAsync(teacher);

            Teachers.Add(teacher);
            NewDisplayName = string.Empty;
            SelectedTeacher = teacher;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر إضافة المعلم:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedTeacher == null)
        {
            MessageBox.Show("اختر معلمًا أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"هل تريد حذف المعلم \"{SelectedTeacher.FullName}\"؟",
            "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            await repo.DeleteAsync(SelectedTeacher.Id);

            Teachers.Remove(SelectedTeacher);
            SelectedTeacher = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر حذف المعلم:\n{ex.Message}\n(قد يكون مرتبطًا بحصص موجودة)", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>استيراد معلمين من Excel/CSV (الاسم، الاختصار، الحد الأسبوعي)</summary>
    [RelayCommand]
    private async Task ImportFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel أو CSV (*.xlsx;*.csv)|*.xlsx;*.csv|جميع الملفات (*.*)|*.*",
            Title = "استيراد المعلمين",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IReadOnlyList<ComponentRow> rows;
            if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                rows = ComponentTransfer.FromCsv(ReadTextWithEncoding(dialog.FileName));
            else
            {
                using var fs = File.OpenRead(dialog.FileName);
                rows = ComponentTransfer.FromXlsx(fs);
            }

            await AddTeacherRowsAsync(rows, "الملف");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"فشل استيراد المعلمين:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>استيراد معلمين من صورة بالذكاء الاصطناعي</summary>
    [RelayCommand]
    private async Task ImportFromImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "صور (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|جميع الملفات (*.*)|*.*",
            Title = "استيراد المعلمين من صورة",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var vision = scope.ServiceProvider.GetRequiredService<IAiVisionService>();
            var rows = await vision.ExtractPeopleFromImageAsync(dialog.FileName, "teachers");
            await AddTeacherRowsAsync(rows, "الصورة");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"فشل استيراد المعلمين من الصورة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddTeacherRowsAsync(IReadOnlyList<ComponentRow> rows, string sourceLabel)
    {
        if (rows.Count == 0)
        {
            MessageBox.Show($"لم يُعثر على أسماء في {sourceLabel}.", "استيراد المعلمين", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existing = Teachers.Select(t => t.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0, skipped = 0;

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
        var schoolId = await _school.GetSchoolIdAsync();

        foreach (var r in rows)
        {
            var name = r.Name.Trim();
            if (name.Length == 0) continue;
            if (!existing.Add(name)) { skipped++; continue; }

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var first = parts.Length > 0 ? parts[0] : name;
            var last = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : first;

            var teacher = new Teacher(schoolId, first, last);
            if (!string.IsNullOrWhiteSpace(r.ShortName)) teacher.SetAppearance(r.ShortName, null);
            if (r.Number is > 0) teacher.UpdateEmploymentInfo(null, null, null, true, 6, r.Number.Value);

            await repo.AddAsync(teacher);
            Teachers.Add(teacher);
            added++;
        }

        MessageBox.Show($"تمت إضافة {added} معلماً من {sourceLabel}." + (skipped > 0 ? $"\nتم تخطي {skipped} (مكرر)." : ""),
            "استيراد المعلمين", MessageBoxButton.OK,
            added > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static string ReadTextWithEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch { return Encoding.UTF8.GetString(bytes); }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
}

    // ===== إجراءات جماعية (تحديد متعدد) =====

    public async Task DeleteManyAsync(IReadOnlyList<Teacher> selected)
    {
        if (selected.Count == 0) return;

        if (MessageBox.Show($"حذف {selected.Count} معلمًا؟", "تأكيد الحذف",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        int deleted = 0, failed = 0;
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();

        foreach (var teacher in selected.ToList())
        {
            try
            {
                await repo.DeleteAsync(teacher.Id);
                Teachers.Remove(teacher);
                deleted++;
            }
            catch
            {
                failed++;
            }
        }

        SelectedTeacher = null;
        MessageBox.Show($"تم حذف {deleted} معلمًا." + (failed > 0 ? $"\nتعذر حذف {failed} (مرتبطون بحصص)." : ""),
            "إجراء جماعي", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public Task ExportManyAsync(IReadOnlyList<Teacher> selected)
    {
        if (selected.Count == 0) return Task.CompletedTask;

        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "المعلمون.csv",
            Title = "تصدير المعلمين المحددين",
        };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;

        var rows = selected.Select(t => new ComponentRow(t.FullName, t.DisplayShortName, t.MaxPeriodsPerWeek));
        File.WriteAllText(dialog.FileName, ComponentTransfer.ToCsv(rows), new UTF8Encoding(true));
        return Task.CompletedTask;
    }

    public void CopyMany(IReadOnlyList<Teacher> selected)
    {
        if (selected.Count == 0) return;
        Clipboard.SetText(string.Join("\n", selected.Select(t => t.DisplayShortName)));
    }
}

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
/// نموذج عرض إدارة الغرف
/// </summary>
public partial class RoomManagementViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSchoolService _school;

    [ObservableProperty]
    private ObservableCollection<Room> _rooms = new();

    [ObservableProperty]
    private Room? _selectedRoom;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // حقول الإضافة
    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newCode = string.Empty;

    [ObservableProperty]
    private int _newCapacity = 30;

    [ObservableProperty]
    private bool _newIsLab;

    /// <summary>قاعة مشتركة بين أكثر من شعبة/معلم.</summary>
    [ObservableProperty]
    private bool _newIsShared;

    /// <summary>المعلم المخصَّص له القاعة (اختياري) — قاعة المعلم.</summary>
    [ObservableProperty]
    private Teacher? _newAssignedTeacher;

    /// <summary>المعلمون لاختيار قاعة المعلم.</summary>
    public ObservableCollection<Teacher> Teachers { get; } = new();

    /// <summary>عند تحديد قاعة: اعرض خياريها الحاليين في المحرّر حتى لا يُلغيهما زر التطبيق.</summary>
    partial void OnSelectedRoomChanged(Room? value)
    {
        if (value is null) return;
        NewIsShared = value.IsShared;
        NewAssignedTeacher = value.AssignedTeacherId is { } id
            ? Teachers.FirstOrDefault(t => (Guid)t.Id == id)
            : null;
    }

    public RoomManagementViewModel(IServiceScopeFactory scopeFactory, CurrentSchoolService school)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _school = school ?? throw new ArgumentNullException(nameof(school));
        _ = LoadAsync();
    }

    public async Task LoadAsync()    {
        IsLoading = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            var teacherRepo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            var schoolId = await _school.GetSchoolIdAsync();
            var list = await repo.GetBySchoolAsync(schoolId);
            if (_school.Current is { } current && current != schoolId) return; // تغيّرت مساحة العمل أثناء التحميل

            Rooms.Clear();
            foreach (var room in list.OrderBy(r => r.Name))
            {
                Rooms.Add(room);
            }

            Teachers.Clear();
            foreach (var teacher in (await teacherRepo.GetBySchoolAsync(schoolId))
                         .OrderBy(t => t.LastName).ThenBy(t => t.FirstName))
            {
                Teachers.Add(teacher);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تحميل قائمة الغرف:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("أدخل اسم الغرفة", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            var schoolId = await _school.GetSchoolIdAsync();

            var room = new Room(schoolId, NewName.Trim(),
                string.IsNullOrWhiteSpace(NewCode) ? null : NewCode.Trim(),
                NewCapacity);

            if (NewIsLab)
            {
                room.UpdateType(true, false, false, false, false);
            }

            room.SetShared(NewIsShared);
            if (NewAssignedTeacher is { } assignedTeacher)
                room.SetTeacherAssignment((Guid)assignedTeacher.Id);

            await repo.AddAsync(room);

            Rooms.Add(room);
            NewName = string.Empty;
            NewCode = string.Empty;
            NewCapacity = 30;
            NewIsLab = false;
            NewIsShared = false;
            NewAssignedTeacher = null;
            SelectedRoom = room;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر إضافة الغرفة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedRoom == null)
        {
            MessageBox.Show("اختر غرفة أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"هل تريد حذف الغرفة \"{SelectedRoom.Name}\"؟",
            "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            await repo.DeleteAsync(SelectedRoom.Id);

            Rooms.Remove(SelectedRoom);
            SelectedRoom = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر حذف الغرفة:\n{ex.Message}\n(قد تكون مرتبطه بحصص موجودة)", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    /// <summary>يطبّق خياري «مشتركة» و«قاعة معلم» على القاعة المحددة.</summary>
    [RelayCommand]
    private async Task ApplyOptionsToSelectedAsync()
    {
        if (SelectedRoom == null)
        {
            MessageBox.Show("اختر قاعة أولًا.", "خيارات القاعة", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SelectedRoom.SetShared(NewIsShared);
            SelectedRoom.SetTeacherAssignment(NewAssignedTeacher is { } teacher ? (Guid)teacher.Id : null);

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            await repo.UpdateAsync(SelectedRoom);

            var idx = Rooms.IndexOf(SelectedRoom);
            if (idx >= 0)
            {
                var item = SelectedRoom;
                Rooms[idx] = item; // تحديث العرض
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تحديث خيارات القاعة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    // ===== إجراءات جماعية =====
    public async Task DeleteManyAsync(IReadOnlyList<Room> selected)
    {
        if (selected.Count == 0) return;
        if (MessageBox.Show($"حذف {selected.Count} قاعة؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        int deleted = 0, failed = 0;
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRoomRepository>();

        foreach (var item in selected.ToList())
        {
            try { await repo.DeleteAsync(item.Id); Rooms.Remove(item); deleted++; }
            catch { failed++; }
        }

        MessageBox.Show($"تم حذف {deleted}." + (failed > 0 ? $" تعذر {failed}." : ""), "إجراء جماعي", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public Task ExportManyAsync(IReadOnlyList<Room> selected)
    {
        if (selected.Count == 0) return Task.CompletedTask;
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "القاعات.csv", Title = "تصدير القاعات" };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;

        var rows = selected.Select(r => new ComponentRow(r.Name, r.ShortName, r.Capacity));
        File.WriteAllText(dialog.FileName, ComponentTransfer.ToCsv(rows), new UTF8Encoding(true));
        return Task.CompletedTask;
    }

    public void CopyMany(IReadOnlyList<Room> selected)
    {
        if (selected.Count == 0) return;
        Clipboard.SetText(string.Join("\n", selected.Select(r => r.Name)));
    }
}

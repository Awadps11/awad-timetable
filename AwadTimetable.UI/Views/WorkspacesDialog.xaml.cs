using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.UI.Services;

namespace AwadTimetable.UI.Views;

/// <summary>
/// إدارة المدارس/مساحات العمل: عرض، إعادة تسمية، نسخ، حذف، وفتح أحدث جدول.
/// </summary>
public partial class WorkspacesDialog : Window
{
    public sealed class Row
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public DateTime? UpdatedAt { get; init; }
        public string UpdatedLabel => UpdatedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "—";
    }

    private readonly WorkspaceService _service;
    private List<Row> _rows = new();
    private bool _busy;

    /// <summary>مساحة العمل المطلوب فتح أحدث جدول فيها (null عند الإلغاء).</summary>
    public Guid? OpenWorkspaceId { get; private set; }

    public WorkspacesDialog(WorkspaceService service)
    {
        InitializeComponent();
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async System.Threading.Tasks.Task ReloadAsync()
    {
        try
        {
            var items = await _service.ListAsync();
            _rows = items.Select(i => new Row
            {
                Id = i.Id,
                Name = i.Name,
                Summary = i.Summary,
                UpdatedAt = i.UpdatedAt,
            }).ToList();

            WorkspacesList.ItemsSource = _rows;
            if (_rows.Count > 0) WorkspacesList.SelectedIndex = 0;
            StatusText.Text = $"{_rows.Count} مساحة عمل";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تعذر التحميل: {ex.Message}";
        }
    }

    private Row? Selected => WorkspacesList.SelectedItem as Row;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        NameBox.Text = Selected?.Name ?? string.Empty;
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not Row row) return;
        var newName = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName)) return;
        if (_busy) return;
        _busy = true;
        try
        {
            if (await _service.RenameAsync(row.Id, newName))
                await ReloadAsync();
        }
        finally { _busy = false; }
    }

    private async void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not Row row) return;
        if (_busy) return;
        _busy = true;
        try
        {
            var id = await _service.DuplicateAsync(row.Id);
            if (id is not null)
            {
                await ReloadAsync();
                MessageBox.Show("تم إنشاء نسخة جديدة ببيانات هذه المدرسة (بدون الجداول).",
                    "نسخ مساحة عمل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("تعذر النسخ.", "نسخ مساحة عمل", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally { _busy = false; }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not Row row) return;

        var answer = MessageBox.Show(
            $"حذف مساحة العمل «{row.Name}» نهائياً مع جداولها ومعلميها وموادها وفصولها وقاعاتها؟",
            "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        if (_busy) return;
        _busy = true;
        try
        {
            if (await _service.DeleteAsync(row.Id))
            {
                await ReloadAsync();
                MessageBox.Show("تم حذف مساحة العمل.", "حذف", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally { _busy = false; }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not Row row) return;
        OpenWorkspaceId = row.Id;
        DialogResult = true;
    }
}

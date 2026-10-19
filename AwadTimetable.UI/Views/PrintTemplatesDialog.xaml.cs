using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;
using AwadTimetable.Printing.Services;
using AwadTimetable.Printing.Templates;
using AwadTimetable.UI.Services;
using Microsoft.Win32;

namespace AwadTimetable.UI.Views;

/// <summary>
/// قوالب الطباعة: اختيار قالب جاهز، تخصيص بسيط (ألوان/خط/خيارات)، معاينة، وحفظ قالب جديد.
/// </summary>
public partial class PrintTemplatesDialog : Window
{
    private readonly Timetable _timetable;
    private readonly List<PrintTemplate> _custom;
    private readonly List<PrintTemplate> _all;

    public PrintTemplatesDialog(Timetable timetable)
    {
        InitializeComponent();
        _timetable = timetable ?? throw new ArgumentNullException(nameof(timetable));

        _custom = PrintTemplateStore.Load();
        _all = new List<PrintTemplate>();
        _all.AddRange(_custom.Where(t => !t.IsBuiltIn));
        _all.AddRange(PrintTemplatePresets.All);

        TemplateList.ItemsSource = _all;
        if (_all.Count > 0) TemplateList.SelectedIndex = 0;
    }

    private void OnTemplateSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateList.SelectedItem is not PrintTemplate t) return;

        NameBox.Text = t.Name;
        PageSizeCombo.SelectedIndex = t.PageSize switch
        {
            PrintPageSize.A3 => 1,
            PrintPageSize.Letter => 2,
            PrintPageSize.Legal => 3,
            _ => 0,
        };
        OrientationCombo.SelectedIndex = t.Orientation == PrintOrientation.Portrait ? 0 : 1;
        FontBox.Text = t.FontFamily;
        FontSizeBox.Text = t.FontSize.ToString();

        HeaderColorBox.Text = t.HeaderColor;
        DayColorBox.Text = t.DayColor;
        Row1Box.Text = t.RowColor1;
        Row2Box.Text = t.RowColor2;
        AccentBox.Text = t.AccentColor;
        TextColorBox.Text = t.TextColor;

        CellStyleCombo.SelectedIndex = (int)t.CellStyle;
        ShowTimesCheck.IsChecked = t.ShowPeriodTimes;
        ShowTeacherCheck.IsChecked = t.ShowTeacher;
        ShowRoomCheck.IsChecked = t.ShowRoom;
        ShowClassCheck.IsChecked = t.ShowClass;
        ShowSummaryCheck.IsChecked = t.ShowSummary;
        ShowLegendCheck.IsChecked = t.ShowLegend;
        ZebraCheck.IsChecked = t.Zebra;
        GridLinesCheck.IsChecked = t.ShowGridLines;
        MergeCheck.IsChecked = t.MergeConsecutive;
        LogoBox.Text = t.LogoPath;
        FooterBox.Text = t.FooterText;
    }

    private PrintTemplate ReadControls()
    {
        var baseT = TemplateList.SelectedItem as PrintTemplate ?? PrintTemplatePresets.Default;

        return new PrintTemplate
        {
            Id = baseT.IsBuiltIn ? Guid.NewGuid().ToString("N") : baseT.Id,
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "قالب" : NameBox.Text.Trim(),
            IsBuiltIn = false,
            PageSize = PageSizeCombo.SelectedIndex switch
            {
                1 => PrintPageSize.A3,
                2 => PrintPageSize.Letter,
                3 => PrintPageSize.Legal,
                _ => PrintPageSize.A4,
            },
            Orientation = OrientationCombo.SelectedIndex == 0 ? PrintOrientation.Portrait : PrintOrientation.Landscape,
            FontFamily = string.IsNullOrWhiteSpace(FontBox.Text) ? "Segoe UI" : FontBox.Text.Trim(),
            FontSize = int.TryParse(FontSizeBox.Text, out var fs) ? Math.Clamp(fs, 6, 24) : 10,
            HeaderColor = Normalize(HeaderColorBox.Text, "#4B6BAC"),
            DayColor = Normalize(DayColorBox.Text, "#3D5990"),
            RowColor1 = Normalize(Row1Box.Text, "#FFFFFF"),
            RowColor2 = Normalize(Row2Box.Text, "#F3F7FF"),
            AccentColor = Normalize(AccentBox.Text, "#4B6BAC"),
            TextColor = Normalize(TextColorBox.Text, "#112233"),
            CellStyle = (PrintCellStyle)Math.Clamp(CellStyleCombo.SelectedIndex, 0, 2),
            ShowPeriodTimes = ShowTimesCheck.IsChecked == true,
            ShowTeacher = ShowTeacherCheck.IsChecked == true,
            ShowRoom = ShowRoomCheck.IsChecked == true,
            ShowClass = ShowClassCheck.IsChecked == true,
            ShowSummary = ShowSummaryCheck.IsChecked == true,
            ShowLegend = ShowLegendCheck.IsChecked == true,
            Zebra = ZebraCheck.IsChecked == true,
            ShowGridLines = GridLinesCheck.IsChecked == true,
            MergeConsecutive = MergeCheck.IsChecked == true,
            LogoPath = LogoBox.Text?.Trim() ?? string.Empty,
            FooterText = FooterBox.Text?.Trim() ?? string.Empty,
        };
    }

    private void OnBrowseLogoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "صور (*.png;*.jpg;*.jpeg;*.gif;*.svg)|*.png;*.jpg;*.jpeg;*.gif;*.svg|كل الملفات (*.*)|*.*",
            Title = "اختيار شعار المدرسة",
        };

        if (dialog.ShowDialog() == true)
            LogoBox.Text = dialog.FileName;
    }

    private static string Normalize(string? value, string fallback)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0) return fallback;
        if (!v.StartsWith('#')) v = "#" + v;
        return v.Length == 7 ? v.ToUpperInvariant() : fallback;
    }

    private string BuildHtml(PrintTemplate template)
    {
        var report = new WebReportService();
        var options = new WebReportOptions
        {
            SchoolName = string.Empty,
            ProductName = "نظام عوض للجداول المدرسية",
        };

        return report.GenerateHtml(_timetable, options, Resolve, template);
    }

    private static (string Subject, string Teacher, string Class, string Room) Resolve(Lesson l)
        => (l.Subject?.Name ?? "", l.Teacher?.FullName ?? "", l.Class?.FullName ?? "", l.Room?.Name ?? "");

    private void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        var template = ReadControls();
        var html = BuildHtml(template);

        var window = new PrintPreviewWindow(html) { Owner = this };
        window.SavePdfAction = async () =>
        {
            await SaveHtmlAsync(template, html);
        };
        window.ShowDialog();
    }

    private async void OnSaveHtmlClick(object sender, RoutedEventArgs e)
    {
        var template = ReadControls();
        await SaveHtmlAsync(template, BuildHtml(template));
    }

    private Task SaveHtmlAsync(PrintTemplate template, string html)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML (*.html)|*.html",
            FileName = $"{_timetable.Name}-{template.Name}.html",
            Title = "حفظ التقرير HTML",
        };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;

        File.WriteAllText(dialog.FileName, html, new UTF8Encoding(true));
        MessageBox.Show("تم حفظ التقرير. يمكن فتحه في المتصفح وطباعته أو حفظه PDF بأعلى دقة.",
            "حفظ HTML", MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    private void OnSaveAsNewClick(object sender, RoutedEventArgs e)
    {
        var template = ReadControls();
        if (string.IsNullOrWhiteSpace(template.Name)) template.Name = "قالب جديد";

        _custom.RemoveAll(t => t.Id == template.Id);
        _custom.Add(template);
        PrintTemplateStore.Save(_custom);

        RefreshList(template);
    }

    private async void OnSetDefaultClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not PrintTemplate t)
        {
            MessageBox.Show("اختر قالباً أولاً.", "قوالب الطباعة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var settings = App.Services.GetService(typeof(ISettingsService)) as ISettingsService;
            if (settings is null) return;

            settings.Set("Print.TemplateId", t.Id);
            await settings.SaveAsync();

            MessageBox.Show($"تم تعيين «{t.Name}» قالباً افتراضياً للطباعة والمعاينة.",
                "قوالب الطباعة", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر حفظ القالب الافتراضي:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not PrintTemplate t || t.IsBuiltIn)
        {
            MessageBox.Show("لا يمكن حذف قالب جاهز. اختر قالباً مخصصاً.", "قوالب الطباعة",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _custom.RemoveAll(x => x.Id == t.Id);
        PrintTemplateStore.Save(_custom);
        RefreshList(null);
    }

    private void RefreshList(PrintTemplate? select)
    {
        _all.Clear();
        _all.AddRange(_custom.Where(t => !t.IsBuiltIn));
        _all.AddRange(PrintTemplatePresets.All);

        TemplateList.ItemsSource = null;
        TemplateList.ItemsSource = _all;

        if (select is not null)
        {
            var idx = _all.FindIndex(t => t.Id == select.Id);
            if (idx >= 0) TemplateList.SelectedIndex = idx;
        }
        else if (_all.Count > 0)
        {
            TemplateList.SelectedIndex = 0;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

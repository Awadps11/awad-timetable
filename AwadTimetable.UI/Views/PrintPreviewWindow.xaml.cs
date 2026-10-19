using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace AwadTimetable.UI.Views;

/// <summary>
/// معاينة الطباعة عبر WebView2 (محرّك Chromium) لعرض HTML الحديث وطباعته/حفظه PDF بأعلى دقة.
/// عند غياب WebView2 يُفتح التقرير في المتصفح الافتراضي كبديل.
/// </summary>
public partial class PrintPreviewWindow : Window
{
    private readonly string _html;
    private bool _webViewReady;

    /// <summary>إجراء بديل لحفظ PDF عندما لا يتوفر WebView2 (يحفظ HTML عادة).</summary>
    public Func<Task>? SavePdfAction { get; set; }

    public PrintPreviewWindow(string html)
    {
        InitializeComponent();
        _html = html;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var runtime = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(runtime))
            {
                ShowFallback("محرّك WebView2 غير مثبّت. سيُفتح التقرير في المتصفح الافتراضي.");
                OpenInBrowser();
                return;
            }

            await WebView.EnsureCoreWebView2Async();
            WebView.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;

            WebView.NavigateToString(_html);
            _webViewReady = true;
        }
        catch (Exception ex)
        {
            ShowFallback($"تعذر تشغيل WebView2: {ex.Message}\nسيُفتح التقرير في المتصفح الافتراضي.");
            OpenInBrowser();
        }
    }

    private void ShowFallback(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void OpenInBrowser()
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"awad-preview-{Guid.NewGuid():N}.html");
            File.WriteAllText(path, _html, new System.Text.UTF8Encoding(true));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // لا شيء إضافي
        }
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (!_webViewReady || WebView.CoreWebView2 is null)
        {
            OpenInBrowser();
            return;
        }

        try
        {
            // مربّع حوار الطباعة الخاص بالمتصفح (اختيار الطابعة والمعاينة)
            WebView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            await Task.CompletedTask;
        }
        catch
        {
            try
            {
                var settings = WebView.CoreWebView2.Environment.CreatePrintSettings();
                await WebView.CoreWebView2.PrintAsync(settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر الطباعة:\n{ex.Message}", "طباعة", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnSavePdfClick(object sender, RoutedEventArgs e)
    {
        if (_webViewReady && WebView.CoreWebView2 is not null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = "تقرير-الجدول.pdf",
                Title = "حفظ PDF",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                await WebView.CoreWebView2.PrintToPdfAsync(dialog.FileName);
                MessageBox.Show("تم حفظ PDF بأعلى دقة.", "حفظ PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر حفظ PDF:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return;
        }

        if (SavePdfAction is null)
        {
            OpenInBrowser();
            return;
        }

        try
        {
            await SavePdfAction();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر الحفظ:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

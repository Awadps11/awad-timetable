using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AwadTimetable.UI.Services;
using QRCoder;

namespace AwadTimetable.UI.Views;

/// <summary>
/// نافذة مشاركة الجدول: خادم محلي + رمز QR + رابط الشبكة
/// </summary>
public partial class ShareWindow : Window
{
    private readonly ShareServerService _server;
    private readonly Func<string> _htmlProvider;
    private readonly List<string> _links = new();
    private bool _stopped;

    public ShareWindow(ShareServerService server, Func<string> htmlProvider)
    {
        InitializeComponent();
        _server = server;
        _htmlProvider = htmlProvider;
        Loaded += Window_Loaded;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _server.Start(_htmlProvider);
            StatusText.Text = $"الخادم يعمل — المنفذ {_server.Port}";

            var addresses = ShareServerService.GetLocalIPv4Addresses();
            _links.Clear();
            foreach (var ip in addresses)
            {
                _links.Add($"http://{ip}:{_server.Port}/");
            }

            LinksComboBox.ItemsSource = _links;
            if (_links.Count > 0) LinksComboBox.SelectedIndex = 0;

            HintText.Text = addresses.Count > 1
                ? "اختر رابط الشبكة المحلية من القائمة. لا يعمل الرابط إلا أثناء فتح هذه النافذة، والجهاز يجب أن يكون على نفس الشبكة."
                : "تعذر تحديد عنوان الشبكة المحلية — استخدم 127.0.0.1 على هذا الجهاز فقط.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تعذر تشغيل خادم المشاركة: {ex.Message}";
        }
    }

    private void LinksComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LinksComboBox.SelectedIndex < 0 || LinksComboBox.SelectedIndex >= _links.Count) return;
        UpdateQr(_links[LinksComboBox.SelectedIndex]);
    }

    private void UpdateQr(string url)
    {
        try
        {
            using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(data);
            var bytes = png.GetGraphic(8);

            using var ms = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            QrImage.Source = image;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تعذر إنشاء رمز QR: {ex.Message}";
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var url = LinksComboBox.SelectedItem as string;
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("لا يوجد رابط لنسخه", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Clipboard.SetText(url);
            StatusText.Text = "تم نسخ الرابط إلى الحافظة";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تعذر النسخ: {ex.Message}";
        }
    }

    private void OpenBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var url = LinksComboBox.SelectedItem as string;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تعذر فتح المتصفح: {ex.Message}";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stopped) return;
        _stopped = true;
        _server.Stop();
        StatusText.Text = "تم إيقاف خادم المشاركة";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _server.Stop();
    }
}

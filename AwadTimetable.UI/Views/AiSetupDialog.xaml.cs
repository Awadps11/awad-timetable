using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AwadTimetable.AI.Clients;
using AwadTimetable.UI.Services;

namespace AwadTimetable.UI.Views;

/// <summary>
/// فحص توفّر المساعد وتنزيل النموذج (اختياري) عند أول تشغيل.
/// </summary>
public partial class AiSetupDialog : Window
{
    private readonly AiModelSetupService _setup;
    private readonly ISettingsService _settings;
    private CancellationTokenSource? _cts;

    public AiSetupDialog(AiModelSetupService setup, ISettingsService settings)
    {
        InitializeComponent();
        _setup = setup ?? throw new ArgumentNullException(nameof(setup));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "جارٍ التحقق من Ollama والنموذج…";
        var status = await _setup.GetStatusAsync();

        StatusText.Text = status.Message +
            (status.OllamaReachable && !status.ModelInstalled
                ? "\nيمكن تنزيل النموذج الآن (يحتاج اتصال إنترنت ووقتاً حسب سرعة الشبكة)."
                : string.Empty);

        DownloadButton.IsEnabled = status.Enabled && status.OllamaReachable && !status.ModelInstalled;
        if (status.ModelInstalled)
        {
            DownloadButton.Content = "النموذج محمّل ✓";
            DownloadButton.IsEnabled = false;
        }

        LaterButton.IsEnabled = true;
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (DownloadButton.IsEnabled == false) return;

        _cts = new CancellationTokenSource();
        DownloadButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        DisableButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;

        var progress = new Progress<OllamaPullProgress>(p =>
        {
            var pct = p.Percent;
            Progress.Value = pct >= 0 ? pct : 0;
            ProgressText.Text = pct >= 0 ? $"{p.Status} — {pct:0.0}%" : p.Status;
        });

        try
        {
            await Task.Run(() => _setup.PullAsync(progress, _cts.Token));
            Progress.Value = 100;
            ProgressText.Text = "تم تنزيل النموذج بنجاح.";
            StatusText.Text = $"المساعد جاهز — النموذج: {_setup.Model}";
            _setup.MarkSetupHandled();
            MessageBox.Show("تم تنزيل النموذج. المساعد جاهز للاستخدام.", "المساعد الذكي",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "أُلغي التنزيل.";
        }
        catch (Exception ex)
        {
            ProgressText.Text = $"تعذر التنزيل: {ex.Message}";
            MessageBox.Show($"تعذر تنزيل النموذج:\n{ex.Message}\n\nيمكنك المحاولة من الإعدادات ← المساعد الذكي.",
                "المساعد الذكي", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            DownloadButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            DisableButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        // لا نزعج المستخدم في كل تشغيل: يُسجَّل أنه رأى النافذة، ويمكنه التنزيل من الإعدادات لاحقاً
        _setup.MarkSetupHandled();
        Close();
    }

    private void OnDisableClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.Set(AiSettings.EnableKey, false);
            _ = _settings.SaveAsync();
        }
        catch
        {
            // تجاهل
        }

        _setup.MarkSetupHandled();
        MessageBox.Show("تم تعطيل المساعد. يمكنك تفعيله من الإعدادات ← المساعد الذكي.",
            "المساعد الذكي", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }
}

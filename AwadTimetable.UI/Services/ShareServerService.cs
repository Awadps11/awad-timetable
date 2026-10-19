using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AwadTimetable.UI.Services;

/// <summary>
/// خادم HTTP بسيط (TcpListener) لعرض الجدول الحالي على أجهزة الشبكة المحلية
/// بدون صلاحيات إدارية — مناسب لمسح رمز QR من الهاتف.
/// </summary>
public sealed class ShareServerService : IDisposable
{
    private readonly ILogger<ShareServerService> _logger;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Func<string>? _htmlProvider;

    public ShareServerService(ILogger<ShareServerService> logger)
    {
        _logger = logger;
    }

    public int Port { get; private set; }
    public bool IsRunning => _listener != null;

    public void Start(Func<string> htmlProvider, int port = 8765)
    {
        Stop();
        _htmlProvider = htmlProvider;

        // اختيار منفذ متاح
        for (var p = port; p < port + 10; p++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, p);
                _listener.Start();
                Port = p;
                break;
            }
            catch (SocketException)
            {
                _listener = null;
            }
        }

        if (_listener == null)
            throw new InvalidOperationException("تعذر فتح منفذ للمشاركة (المنافذ 8765-8774 مشغولة)");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => AcceptLoopAsync(token));
        _logger.LogInformation("خادم المشاركة يعمل على المنفذ {Port}", Port);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* تم إغلاقه مسبقاً */ }
        _listener = null;
        _cts = null;
        if (Port != 0)
        {
            _logger.LogInformation("تم إيقاف خادم المشاركة");
            Port = 0;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(client), token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "خطأ في قبول اتصال خادم المشاركة");
                if (token.IsCancellationRequested) break;
                await Task.Delay(200, token).ContinueWith(_ => { });
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();

                // قراءة طلب HTTP (سطر الطلب + الترويسات حتى سطر فارغ)
                var buffer = new byte[4096];
                var request = new StringBuilder();
                while (!request.ToString().Contains("\r\n\r\n"))
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0) return;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    if (request.Length > 16384) break;
                }

                var text = request.ToString();
                var firstLine = text.Split('\n')[0].Trim();
                _logger.LogDebug("طلب مشاركة: {Line}", firstLine);

                if (!firstLine.StartsWith("GET", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 405, "text/plain; charset=utf-8", "Method Not Allowed");
                    return;
                }

                var path = firstLine.Split(' ').ElementAtOrDefault(1) ?? "/";

                // أي مسار (ما عدا الأيقونات) يعيد الجدول الحالي
                if (path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 404, "text/plain", "");
                    return;
                }

                var html = _htmlProvider?.Invoke() ?? "<html><body><p>لا يوجد جدول حالياً</p></body></html>";
                var body = Encoding.UTF8.GetBytes(html);
                var header =
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "Connection: close\r\n" +
                    "Cache-Control: no-store\r\n" +
                    "\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(header);
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                await stream.WriteAsync(body, 0, body.Length);
                await stream.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "انقطع اتصال عميل المشاركة");
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string contentType, string bodyText)
    {
        var body = Encoding.UTF8.GetBytes(bodyText);
        var reason = status == 200 ? "OK" : status == 405 ? "Method Not Allowed" : "Not Found";
        var header =
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
        if (body.Length > 0) await stream.WriteAsync(body, 0, body.Length);
        await stream.FlushAsync();
    }

    /// <summary>عناوين IPv4 المحلية المناسبة للعرض في رابط المشاركة</summary>
    public static IReadOnlyList<string> GetLocalIPv4Addresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address.ToString();
                    if (ip.StartsWith("169.254.")) continue; // رابط ذاتي
                    if (!result.Contains(ip)) result.Add(ip);
                }
            }
        }
        catch
        {
            // تجاهل — سنعرض localhost فقط
        }

        if (!result.Contains("127.0.0.1")) result.Insert(0, "127.0.0.1");
        return result;
    }

    public void Dispose() => Stop();
}

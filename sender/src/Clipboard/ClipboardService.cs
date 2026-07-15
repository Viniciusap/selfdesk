using System.Drawing.Imaging;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using SelfDesk.Sender.Network;
namespace SelfDesk.Sender.Clipboard;

/// <summary>
/// Monitora o clipboard local e sincroniza com o receiver via broker.
/// Roda leitura/escrita de clipboard em thread STA dedicada (requisito Win32).
/// Suporta texto (UTF-8) e imagens (PNG) — issue #8.
/// </summary>
public sealed class ClipboardService
{
    private const int MaxClipboardBytes = 4 * 1024 * 1024; // 4 MB — bumped for images (issue #8)

    private readonly BrokerConnection _conn;
    private readonly ILogger _log;
    private volatile string _lastText = string.Empty;
    private byte[]? _lastImageBytes;

    public ClipboardService(BrokerConnection conn, ILogger log)
    {
        _conn = conn;
        _log  = log;
    }

    /// <summary>Recebeu CLIPBOARD do broker — escreve no clipboard local.</summary>
    public void OnRemoteClipboard(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length == 0) return;
        var kind = payload.Span[0];
        var data = payload[1..];

        switch (kind)
        {
            case ClipboardKind.Text:
                var text = Encoding.UTF8.GetString(data.Span);
                if (text == _lastText) return;
                _lastText = text;
                RunOnSta(() => SetClipboardText(text));
                break;

            case ClipboardKind.Image:
                var imageBytes = data.ToArray();
                _lastImageBytes = imageBytes;
                RunOnSta(() => SetClipboardImage(imageBytes));
                break;
        }
    }

    /// <summary>Loop de monitoramento — chama periodicamente da session loop do SenderService.</summary>
    public async Task MonitorAsync(string agentId, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var (text, imageBytes) = GetClipboardSta();

            if (text is not null && text != _lastText)
            {
                _lastText = text;
                var payload = Encoding.UTF8.GetBytes(text);
                if (payload.Length > MaxClipboardBytes) continue;
                var msg = WireProtocol.BuildClipboardText(text, agentId);
                try { await _conn.SendAsync(msg, ct); }
                catch (Exception ex) { _log.LogWarning(ex, "Failed to send clipboard text"); }
            }
            else if (imageBytes is not null && !BytesEqual(imageBytes, _lastImageBytes))
            {
                _lastImageBytes = imageBytes;
                if (imageBytes.Length > MaxClipboardBytes) continue;
                var msg = WireProtocol.BuildClipboardImage(imageBytes, agentId);
                try { await _conn.SendAsync(msg, ct); }
                catch (Exception ex) { _log.LogWarning(ex, "Failed to send clipboard image"); }
            }
        }
    }

    private static bool BytesEqual(byte[]? a, byte[]? b)
    {
        if (a is null || b is null) return a == b;
        return a.AsSpan().SequenceEqual(b);
    }

    private static (string? text, byte[]? imageBytes) GetClipboardSta()
    {
        string? text = null;
        byte[]? imageBytes = null;
        RunOnSta(() =>
        {
            try
            {
                if (System.Windows.Forms.Clipboard.ContainsText())
                {
                    text = System.Windows.Forms.Clipboard.GetText();
                }
                else if (System.Windows.Forms.Clipboard.ContainsImage())
                {
                    using var img = System.Windows.Forms.Clipboard.GetImage();
                    if (img is not null)
                    {
                        using var ms = new MemoryStream();
                        img.Save(ms, ImageFormat.Png);
                        imageBytes = ms.ToArray();
                    }
                }
            }
            catch { }
        });
        return (text, imageBytes);
    }

    private static void SetClipboardText(string text)
    {
        try { System.Windows.Forms.Clipboard.SetText(text); }
        catch { }
    }

    private static void SetClipboardImage(byte[] pngBytes)
    {
        try
        {
            using var ms  = new MemoryStream(pngBytes);
            using var img = System.Drawing.Image.FromStream(ms);
            System.Windows.Forms.Clipboard.SetImage(img);
        }
        catch { }
    }

    private static void RunOnSta(Action action)
    {
        var thread = new Thread(() => action());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}

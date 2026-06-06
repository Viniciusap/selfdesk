using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using SelfDesk.Sender.Network;
namespace SelfDesk.Sender.Clipboard;

/// <summary>
/// Monitora o clipboard local e sincroniza com o receiver via broker.
/// Roda leitura/escrita de clipboard em thread STA dedicada (requisito Win32).
/// </summary>
public sealed class ClipboardService
{
    private readonly BrokerConnection _conn;
    private readonly ILogger _log;
    private string _lastText = string.Empty;

    public ClipboardService(BrokerConnection conn, ILogger log)
    {
        _conn = conn;
        _log  = log;
    }

    /// <summary>Recebeu CLIPBOARD do broker — escreve no clipboard local.</summary>
    public void OnRemoteClipboard(ReadOnlyMemory<byte> payload)
    {
        var text = Encoding.UTF8.GetString(payload.Span);
        if (text == _lastText) return;
        _lastText = text;
        RunOnSta(() => SetClipboardText(text));
    }

    /// <summary>Loop de monitoramento — chama periodicamente da session loop do SenderService.</summary>
    public async Task MonitorAsync(string agentId, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var text = GetClipboardTextSta();
            if (text is null || text == _lastText) continue;
            _lastText = text;

            var payload = Encoding.UTF8.GetBytes(text);
            var msg = WireProtocol.BuildClipboard(payload, agentId);
            try { await _conn.SendAsync(msg, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to send clipboard"); }
        }
    }

    private static string? GetClipboardTextSta()
    {
        string? result = null;
        RunOnSta(() =>
        {
            try { result = System.Windows.Forms.Clipboard.GetText(); }
            catch { result = null; }
        });
        return result;
    }

    private static void SetClipboardText(string text)
    {
        try { System.Windows.Forms.Clipboard.SetText(text); }
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

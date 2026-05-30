using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Viewer.Decode;
using SelfDesk.Viewer.Network;
using SelfDesk.Viewer.Protocol;
using SelfDesk.Viewer.ViewModels;

namespace SelfDesk.Viewer;

public sealed class ViewerService : BackgroundService
{
    private readonly ViewerConfig          _cfg;
    private readonly MainWindowViewModel   _vm;
    private readonly ILogger<ViewerService> _log;
    private readonly IFrameDecoder          _decoder;

    public ViewerService(
        IOptions<ViewerConfig> cfg,
        MainWindowViewModel vm,
        IFrameDecoder decoder,
        ILogger<ViewerService> log)
    {
        _cfg     = cfg.Value;
        _vm      = vm;
        _decoder = decoder;
        _log     = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(2);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(ct);
                retryDelay = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Sessão encerrada. Reconectando em {Delay}s", retryDelay.TotalSeconds);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _vm.IsConnected      = false;
                    _vm.ConnectionStatus = $"Reconectando em {retryDelay.TotalSeconds:0}s…";
                    _vm.Senders.Clear();
                });
                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 60));
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        await using var conn = new BrokerConnection(_cfg, _log);
        string _lastClipboard = string.Empty;

        conn.MessageReceived += (type, peerId, payload) =>
        {
            if (type == MessageType.Clipboard)
            {
                var text = Encoding.UTF8.GetString(payload.Span);
                if (text == _lastClipboard) return;
                _lastClipboard = text;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try { System.Windows.Clipboard.SetText(text); }
                    catch { }
                });
                return;
            }
            OnMessage(type, peerId, payload);
        };

        await conn.ConnectAndAuthAsync(ct);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _vm.IsConnected      = true;
            _vm.ConnectionStatus = "Conectado";
        });

        // Conecta drag & drop da janela
        var window = Application.Current?.MainWindow as SelfDesk.Viewer.Views.MainWindow;
        if (window is not null)
            window.FileDropped += files => _ = SendFilesAsync(files, conn, ct);

        _ = conn.StartHeartbeatAsync(ct);
        var recvLoop = conn.RunReceiveLoopAsync(ct);

        var clipboardLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(ct))
            {
                string? text = null;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try { text = System.Windows.Clipboard.ContainsText()
                        ? System.Windows.Clipboard.GetText() : null; }
                    catch { text = null; }
                });
                if (text is null || text == _lastClipboard) continue;
                _lastClipboard = text;
                var targetId = _vm.SelectedSender?.AgentId ?? string.Empty;
                var msg = WireProtocol.BuildClipboard(Encoding.UTF8.GetBytes(text), targetId);
                try { await conn.SendAsync(msg, ct); }
                catch { break; }
            }
        }, ct);

        await Task.WhenAny(recvLoop, clipboardLoop);
    }

    private static uint _transferIdCounter;
    private const int ChunkSize = 512 * 1024;

    private async Task SendFilesAsync(string[] paths, BrokerConnection conn, CancellationToken ct)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var targetId = _vm.SelectedSender?.AgentId ?? string.Empty;
            if (string.IsNullOrEmpty(targetId)) continue;

            var id       = ++_transferIdCounter;
            var fileName = Path.GetFileName(path);
            var info     = new FileInfo(path);
            long total   = info.Length;
            long sent    = 0;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _vm.TransferProgress = 0;
                _vm.TransferStatus   = $"Enviando {fileName}…";
                _vm.NotifyStatusBarChanged();
            });

            try
            {
                await conn.SendAsync(WireProtocol.BuildFileHeader(id, total, fileName, targetId), ct);

                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var buf = new byte[ChunkSize];
                int read;
                while ((read = await fs.ReadAsync(buf.AsMemory(0, ChunkSize), ct)) > 0)
                {
                    await conn.SendAsync(WireProtocol.BuildFileChunk(id, buf.AsSpan(0, read), targetId), ct);
                    sent += read;
                    var pct = (int)(sent * 100 / total);
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _vm.TransferProgress = pct;
                        _vm.TransferStatus   = $"Enviando {fileName} — {pct}%";
                        _vm.NotifyStatusBarChanged();
                    });
                }

                await conn.SendAsync(WireProtocol.BuildFileDone(id, targetId), ct);
                _log.LogInformation("Arquivo enviado: {Name}", fileName);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha ao enviar {Name}", fileName);
                try { await conn.SendAsync(WireProtocol.BuildFileError(id, targetId), ct); } catch { }
            }
            finally
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _vm.TransferProgress = -1;
                    _vm.TransferStatus   = string.Empty;
                    _vm.NotifyStatusBarChanged();
                });
            }
        }
    }

    private void OnMessage(byte type, string peerId, ReadOnlyMemory<byte> payload)
    {
        switch (type)
        {
            case MessageType.SenderUp:
            {
                var doc = System.Text.Json.JsonDocument.Parse(payload.ToArray());
                var id  = doc.RootElement.GetProperty("agentId").GetString() ?? peerId;
                var mac = doc.RootElement.TryGetProperty("mac", out var macEl) ? macEl.GetString() : null;
                Application.Current?.Dispatcher.Invoke(() => _vm.AddSender(id, mac));
                break;
            }
            case MessageType.SenderDown:
            {
                var doc = System.Text.Json.JsonDocument.Parse(payload.ToArray());
                var id  = doc.RootElement.GetProperty("agentId").GetString() ?? peerId;
                Application.Current?.Dispatcher.Invoke(() => _vm.RemoveSender(id));
                break;
            }
            case MessageType.VideoFrame:
                ProcessVideoFrame(peerId, payload);
                break;
        }
    }

    private void ProcessVideoFrame(string senderId, ReadOnlyMemory<byte> payload)
    {
        var span  = payload.Span;
        if (span.Length < VideoFrameOffsets.Data) return;

        var ts     = BinaryPrimitives.ReadInt64BigEndian(span[VideoFrameOffsets.Timestamp..]);
        var width  = (int)BinaryPrimitives.ReadUInt16BigEndian(span[VideoFrameOffsets.Width..]);
        var height = (int)BinaryPrimitives.ReadUInt16BigEndian(span[VideoFrameOffsets.Height..]);
        var codec  = span[VideoFrameOffsets.Codec];
        var data   = span[VideoFrameOffsets.Data..];

        if (codec != VideoFrameOffsets.CodecJpeg && codec != VideoFrameOffsets.CodecH264) return;

        DecodedFrame decoded;
        try { decoded = _decoder.Decode(data, width, height, ts); }
        catch (Exception ex) { _log.LogWarning(ex, "Falha ao decodificar frame"); return; }

        var rtt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            var sender = _vm.Senders.FirstOrDefault(s => s.AgentId == senderId);
            if (sender is not null)
            {
                sender.LastRttMs   = rtt;
                sender.FrameWidth  = decoded.Width;
                sender.FrameHeight = decoded.Height;
                sender.Codec       = codec == VideoFrameOffsets.CodecH264 ? "H264" : "JPEG";
                _vm.NotifyStatusBarChanged();
            }

            if (_vm.SelectedSender?.AgentId != senderId) return;

            if (_vm.VideoFrame is null ||
                _vm.VideoFrame.PixelWidth  != decoded.Width ||
                _vm.VideoFrame.PixelHeight != decoded.Height)
            {
                _vm.VideoFrame = new WriteableBitmap(
                    decoded.Width, decoded.Height, 96, 96, PixelFormats.Bgra32, null);
            }

            _vm.VideoFrame.Lock();
            _vm.VideoFrame.WritePixels(
                new Int32Rect(0, 0, decoded.Width, decoded.Height),
                decoded.Bgra, decoded.Width * 4, 0);
            _vm.VideoFrame.AddDirtyRect(new Int32Rect(0, 0, decoded.Width, decoded.Height));
            _vm.VideoFrame.Unlock();
        });
    }
}

using System.Buffers.Binary;
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
        await using var conn = new BrokerConnection(_cfg, _log);

        conn.MessageReceived += (type, peerId, payload) =>
            OnMessage(type, peerId, payload);

        try
        {
            await conn.ConnectAndAuthAsync(ct);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _vm.IsConnected      = true;
                _vm.ConnectionStatus = "Conectado";
            });

            _ = conn.StartHeartbeatAsync(ct);
            await conn.RunReceiveLoopAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "Erro no ViewerService");
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _vm.ConnectionStatus = "Erro de conexão";
                _vm.IsConnected      = false;
            });
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
                Application.Current?.Dispatcher.Invoke(() => _vm.AddSender(id));
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

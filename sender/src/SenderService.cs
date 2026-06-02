using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Sender.Capture;
using SelfDesk.Sender.Clipboard;
using SelfDesk.Sender.Encode;
using SelfDesk.Sender.FileTransfer;
using SelfDesk.Sender.Inject;
using SelfDesk.Sender.Network;
using SelfDesk.Sender.Protocol;

namespace SelfDesk.Sender;

public sealed class SenderService : BackgroundService
{
    private readonly SenderConfig   _cfg;
    private readonly IScreenCapturer _capturer;
    private readonly IFrameEncoder   _encoder;
    private readonly IInputInjector  _injector;
    private readonly ILogger<SenderService> _log;

    public SenderService(
        IOptions<SenderConfig> cfg,
        IScreenCapturer capturer,
        IFrameEncoder encoder,
        IInputInjector injector,
        ILogger<SenderService> log)
    {
        _cfg      = cfg.Value;
        _capturer = capturer;
        _encoder  = encoder;
        _injector = injector;
        _log      = log;
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
                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 60));
            }
        }

        _log.LogInformation("SenderService encerrando");
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(1.0 / _cfg.TargetFps);

        _injector.ReleaseAllModifiers();

        await using var conn = new BrokerConnection(_cfg, _log);
        await conn.ConnectAndAuthAsync(ct);
        _injector.ReleaseAllModifiers();

        var channel = Channel.CreateBounded<EncodedFrame>(new BoundedChannelOptions(1)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

        var clipboard = new ClipboardService(conn, _log);
        var fileRx    = new FileTransferReceiver(_log);

        conn.MessageReceived += (type, payload) =>
        {
            switch (type)
            {
                case MessageType.InputEvent:  _injector.Inject(payload.Span); break;
                case MessageType.RequestIdr:  _encoder.RequestKeyframe(); break;
                case MessageType.Clipboard:   clipboard.OnRemoteClipboard(payload); break;
                case MessageType.FileHeader:  fileRx.OnFileHeader(payload); break;
                case MessageType.FileChunk:   fileRx.OnFileChunk(payload); break;
                case MessageType.FileDone:    fileRx.OnFileDone(payload); break;
                case MessageType.FileError:   fileRx.OnFileError(payload); break;
            }
        };

        var heartbeat      = conn.StartHeartbeatAsync(ct);
        var recvLoop       = conn.RunReceiveLoopAsync(ct);
        var clipboardLoop  = clipboard.MonitorAsync(_cfg.SenderId, ct);

        var rttMonitor = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(ct))
            {
                var rtt = conn.LastRttMs;
                if (rtt <= 0) continue;
                var bps = rtt switch
                {
                    < 50  => 8_000_000L,
                    < 150 => 4_000_000L,
                    _     => 1_500_000L,
                };
                _encoder.UpdateBitrate(bps);
            }
        }, ct);

        var producer = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                try
                {
                    var frame   = _capturer.CaptureFrame();
                    var encoded = _encoder.Encode(frame);
                    if (encoded.Data.Length == 0) continue;
                    await channel.Writer.WriteAsync(encoded, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _log.LogError(ex, "Erro no capturer/encoder"); }

                var elapsed = DateTime.UtcNow - started;
                var delay   = interval - elapsed;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            }
            channel.Writer.Complete();
        }, ct);

        var consumer = Task.Run(async () =>
        {
            await foreach (var encoded in channel.Reader.ReadAllAsync(ct))
            {
                var msg = WireProtocol.BuildVideoFrame(
                    encoded.TimestampMs,
                    (ushort)encoded.Width,
                    (ushort)encoded.Height,
                    encoded.Codec,
                    encoded.Flags,
                    encoded.Data,
                    _cfg.SenderId);
                await conn.SendAsync(msg, ct);
            }
        }, ct);

        await Task.WhenAny(producer, consumer, heartbeat, recvLoop, clipboardLoop, rttMonitor);
    }
}

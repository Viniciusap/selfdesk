using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Sender.Audio;
using SelfDesk.Sender.Capture;
using SelfDesk.Sender.Clipboard;
using SelfDesk.Sender.Encode;
using SelfDesk.Sender.FileTransfer;
using SelfDesk.Sender.Inject;
using SelfDesk.Sender.Network;
using SenderWire = SelfDesk.Sender.Protocol.WireProtocol;

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
        var version = System.Reflection.Assembly
            .GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        _log.LogInformation(
            "SelfDesk.Sender v{Version} starting — id={SenderId} broker={Host}:{Port} capturer={Capturer} encoder={Encoder}",
            version, _cfg.SenderId, _cfg.BrokerHost, _cfg.BrokerPort, _cfg.Capturer, _cfg.Encoder);

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
                _log.LogWarning(ex, "Session ended. Reconnecting in {Delay}s", retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 60));
            }
        }

        _log.LogInformation("SenderService stopping");
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(1.0 / _cfg.TargetFps);

        _injector.ReleaseAllModifiers();

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sCt = sessionCts.Token;

        await using var conn = new BrokerConnection(_cfg, _log);
        await conn.ConnectAndAuthAsync(sCt);
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
                case MessageType.RemoteReboot:
                    _log.LogWarning("Remote reboot requested — rebooting in 5 seconds");
                    System.Diagnostics.Process.Start("shutdown", "/r /t 5 /c \"SelfDesk remote reboot\"");
                    break;
                case MessageType.RequestIdr:
                    _encoder.RequestKeyframe();
                    // Viewer acabou de conectar — reenviar lista de monitores
                    _ = conn.SendAsync(SenderWire.BuildMonitorList(
                        MonitorEnumerator.Enumerate(), _cfg.SenderId), sCt);
                    break;
                case MessageType.Clipboard:   clipboard.OnRemoteClipboard(payload); break;
                case MessageType.FileHeader:  fileRx.OnFileHeader(payload); break;
                case MessageType.FileChunk:   fileRx.OnFileChunk(payload); break;
                case MessageType.FileDone:    fileRx.OnFileDone(payload); break;
                case MessageType.FileError:   fileRx.OnFileError(payload); break;
                case MessageType.SelectMonitor:
                    try
                    {
                        var idx = JsonDocument.Parse(payload.ToArray())
                            .RootElement.GetProperty("monitorIndex").GetInt32();
                        _capturer.SwitchMonitor(idx);
                        _injector.MonitorIndex = idx;
                        _log.LogInformation("Switched to monitor {Index}", idx);
                    }
                    catch (Exception ex) { _log.LogWarning(ex, "Failed to switch monitor"); }
                    break;
            }
        };

        // envia lista de monitores ao conectar
        var monitors = MonitorEnumerator.Enumerate();
        await conn.SendAsync(SenderWire.BuildMonitorList(monitors, _cfg.SenderId), sCt);

        var heartbeat      = conn.StartHeartbeatAsync(sCt);
        var recvLoop       = conn.RunReceiveLoopAsync(sCt);
        var clipboardLoop  = clipboard.MonitorAsync(_cfg.SenderId, sCt);

        var rttMonitor = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(sCt))
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
        }, sCt);

        var producer = Task.Run(async () =>
        {
            while (!sCt.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                try
                {
                    var frame   = _capturer.CaptureFrame();
                    var encoded = _encoder.Encode(frame);
                    if (encoded.Data.Length == 0) continue;
                    await channel.Writer.WriteAsync(encoded, sCt);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _log.LogError(ex, "Error in capturer/encoder"); }

                var elapsed = DateTime.UtcNow - started;
                var delay   = interval - elapsed;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, sCt);
            }
            channel.Writer.Complete();
        }, sCt);

        var consumer = Task.Run(async () =>
        {
            await foreach (var encoded in channel.Reader.ReadAllAsync(sCt))
            {
                var msg = SenderWire.BuildVideoFrame(
                    encoded.TimestampMs,
                    (ushort)encoded.Width,
                    (ushort)encoded.Height,
                    encoded.Codec,
                    encoded.Flags,
                    encoded.Data,
                    _cfg.SenderId);
                await conn.SendAsync(msg, sCt);
            }
        }, sCt);

        // fire-and-forget: audio failure must not terminate the video session
        _ = AudioPipeline.Start(conn, _cfg.SenderId, _log, sCt);

        try
        {
            await Task.WhenAny(producer, consumer, heartbeat, recvLoop, clipboardLoop, rttMonitor);
        }
        finally
        {
            await sessionCts.CancelAsync();
        }
    }
}

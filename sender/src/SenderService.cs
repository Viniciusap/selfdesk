using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Sender.Audio;
using SelfDesk.Sender.Capture;
using SelfDesk.Sender.Clipboard;
using SelfDesk.Sender.Diagnostics;
using SelfDesk.Sender.Encode;
using SelfDesk.Sender.FileTransfer;
using SelfDesk.Sender.Inject;
using SelfDesk.Sender.Network;
using SenderWire = SelfDesk.Sender.Protocol.WireProtocol;

namespace SelfDesk.Sender;

public sealed class SenderService : BackgroundService
{
    // RTT->bitrate adaptation thresholds, matching the naming pattern used for
    // the equivalent adaptive-quality thresholds in viewer/src/ViewerService.cs.
    private const int  RttLowMs        = 50;
    private const int  RttMediumMs     = 150;
    private const long BitrateHighBps   = 8_000_000L;
    private const long BitrateMediumBps = 4_000_000L;
    private const long BitrateLowBps    = 1_500_000L;

    private readonly SenderConfig   _cfg;
    private readonly IScreenCapturer _capturer;
    private readonly IFrameEncoder   _encoder;
    private readonly IInputInjector  _injector;
    private readonly BenchmarkLog    _benchmarkLog;
    private readonly ILogger<SenderService> _log;

    public SenderService(
        IOptions<SenderConfig> cfg,
        IScreenCapturer capturer,
        IFrameEncoder encoder,
        IInputInjector injector,
        BenchmarkLog benchmarkLog,
        ILogger<SenderService> log)
    {
        _cfg          = cfg.Value;
        _capturer     = capturer;
        _encoder      = encoder;
        _injector     = injector;
        _benchmarkLog = benchmarkLog;
        _log          = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var version = System.Reflection.Assembly
            .GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        _log.LogInformation(
            "SelfDesk.Sender v{Version} starting — id={SenderId} capturer={Capturer} encoder={Encoder}",
            version, _cfg.SenderId, _cfg.Capturer, _cfg.Encoder);

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

    // Issue #14: runtime-adjustable via QUALITY_HINT; reset to the configured
    // default at the start of each session so a degraded value doesn't survive a reconnect.
    private int _currentTargetFps;

    private async Task RunSessionAsync(CancellationToken ct)
    {
        _currentTargetFps = _cfg.TargetFps;

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

        // Issue #21: opt-in, off by default — see MIC_ENABLED in SenderConfig.
        using var remoteAudio = _cfg.MicEnabled ? new RemoteAudioPlayer(_log) : null;

        var shutdownExe   = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
        var rebootScheduled = false;

        // S23: throttle input injection to a 5ms minimum interval — a flooding or
        // malfunctioning viewer should not be able to hammer SendInput unbounded.
        const long minInjectIntervalMs = 5;
        var lastInjectMs = 0L;

        conn.MessageReceived += (type, payload) =>
        {
            switch (type)
            {
                case MessageType.InputEvent:
                    var nowMs = Environment.TickCount64;
                    if (nowMs - lastInjectMs < minInjectIntervalMs) break;
                    lastInjectMs = nowMs;
                    _injector.Inject(payload.Span);
                    break;
                case MessageType.RemoteReboot:
                    if (!rebootScheduled)
                    {
                        rebootScheduled = true;
                        _log.LogWarning("Remote reboot requested — rebooting in 5 seconds");
                        // Sonar flags any Process.Start as a hotspot needing manual review: safe here
                        // because shutdownExe is a fixed path under System32 and the argument string is
                        // a fixed literal — nothing from the (already-authenticated) network message
                        // reaches the command line.
                        System.Diagnostics.Process.Start(shutdownExe, "/r /t 5 /c \"SelfDesk remote reboot\"");
                    }
                    break;
                case MessageType.RequestIdr:
                    _encoder.RequestKeyframe();
                    _ = conn.SendAsync(SenderWire.BuildMonitorList(
                        MonitorEnumerator.Enumerate(), _cfg.SenderId), sCt);
                    break;
                case MessageType.Clipboard:   clipboard.OnRemoteClipboard(payload); break;
                case MessageType.FileHeader:  fileRx.OnFileHeader(payload); break;
                case MessageType.FileChunk:   fileRx.OnFileChunk(payload); break;
                case MessageType.FileDone:    fileRx.OnFileDone(payload); break;
                case MessageType.FileError:   fileRx.OnFileError(payload); break;
                case MessageType.AudioMic:    remoteAudio?.OnAudioMic(payload); break;
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
                case MessageType.QualityHint when payload.Length >= 2:
                    var quality = payload.Span[QualityHintOffsets.Quality];
                    var fps     = payload.Span[QualityHintOffsets.Fps];
                    _encoder.UpdateQuality(quality);
                    Volatile.Write(ref _currentTargetFps, Math.Clamp((int)fps, 1, 60));
                    _log.LogDebug("Applied QUALITY_HINT: quality={Quality} fps={Fps}", quality, fps);
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
                    < RttLowMs    => BitrateHighBps,
                    < RttMediumMs => BitrateMediumBps,
                    _             => BitrateLowBps,
                };
                _encoder.UpdateBitrate(bps);
            }
        }, sCt);

        // Issue #22: only re-capture/send when the cursor handle actually changes —
        // GetCursorInfo alone is cheap; Icon.ToBitmap (inside Capture) is not.
        var cursorLoop = Task.Run(async () =>
        {
            var lastHandle = IntPtr.Zero;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(sCt))
            {
                var handle = CursorShapeProvider.GetCurrentCursorHandle();
                if (handle == lastHandle) continue;
                lastHandle = handle;

                var shape = CursorShapeProvider.Capture(handle);
                if (shape is null) continue;

                try { await conn.SendAsync(SenderWire.BuildCursorShape(shape.Value, _cfg.SenderId), sCt); }
                catch (Exception ex) { _log.LogWarning(ex, "Failed to send cursor shape"); }
            }
        }, sCt);

        var producer = Task.Run(async () =>
        {
            while (!sCt.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                try
                {
                    var frame        = _capturer.CaptureFrame();
                    var capturedAt   = DateTime.UtcNow;
                    var encoded      = _encoder.Encode(frame);
                    var encodedAt    = DateTime.UtcNow;
                    if (encoded.Data.Length == 0) continue;
                    _benchmarkLog.WriteFrame(
                        frame.TimestampMs,
                        (capturedAt - started).TotalMilliseconds,
                        (encodedAt - capturedAt).TotalMilliseconds);
                    await channel.Writer.WriteAsync(encoded, sCt);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _log.LogError(ex, "Error in capturer/encoder"); }

                var interval = TimeSpan.FromSeconds(1.0 / Volatile.Read(ref _currentTargetFps));
                var elapsed  = DateTime.UtcNow - started;
                var delay    = interval - elapsed;
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
        if (_cfg.AudioEnabled)
            _ = AudioPipeline.Start(conn, _cfg.SenderId, _log, sCt);

        try
        {
            var done = await Task.WhenAny(producer, consumer, heartbeat, recvLoop, clipboardLoop, rttMonitor, cursorLoop);
            await done;
        }
        finally
        {
            await sessionCts.CancelAsync();
        }
    }
}

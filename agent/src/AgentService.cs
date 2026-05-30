using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Agent.Capture;
using SelfDesk.Agent.Encode;
using SelfDesk.Agent.Inject;
using SelfDesk.Agent.Network;
using SelfDesk.Agent.Protocol;

namespace SelfDesk.Agent;

public sealed class AgentService : BackgroundService
{
    private readonly AgentConfig   _cfg;
    private readonly IScreenCapturer _capturer;
    private readonly IFrameEncoder   _encoder;
    private readonly IInputInjector  _injector;
    private readonly ILogger<AgentService> _log;

    public AgentService(
        IOptions<AgentConfig> cfg,
        IScreenCapturer capturer,
        IFrameEncoder encoder,
        IInputInjector injector,
        ILogger<AgentService> log)
    {
        _cfg      = cfg.Value;
        _capturer = capturer;
        _encoder  = encoder;
        _injector = injector;
        _log      = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(1.0 / _cfg.TargetFps);

        await using var conn = new BrokerConnection(_cfg, _log);
        await conn.ConnectAndAuthAsync(ct);

        var channel = Channel.CreateBounded<EncodedFrame>(new BoundedChannelOptions(1)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

        conn.MessageReceived += (type, payload) =>
        {
            if (type == MessageType.InputEvent)
                _injector.Inject(payload.Span);
        };

        var heartbeat = conn.StartHeartbeatAsync(ct);
        var recvLoop  = conn.RunReceiveLoopAsync(ct);

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
                    _cfg.AgentId);
                await conn.SendAsync(msg, ct);
            }
        }, ct);

        await Task.WhenAny(producer, consumer, heartbeat, recvLoop);
        _log.LogInformation("AgentService encerrando");
    }
}

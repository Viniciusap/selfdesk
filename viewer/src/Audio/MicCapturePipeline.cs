using System.Threading.Channels;
using Concentus.Enums;
using Concentus.Structs;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SelfDesk.Viewer.Network;
using ViewerWire = SelfDesk.Viewer.Protocol.WireProtocol;

namespace SelfDesk.Viewer.Audio;

// Issue #21: viewer mic -> sender speakers, the reverse path of the existing
// sender system-audio loopback -> viewer pipeline (sender/src/Audio/AudioPipeline.cs).
// Muted by default (see MainWindowViewModel.IsMicMuted) — capture keeps running while
// muted (matching how speaker mute already behaves in this app) but frames are not
// built or sent, so the only cost of "muted" is the open capture device itself.
public static class MicCapturePipeline
{
    private const int SampleRate   = 48000;
    private const int Channels     = 2;
    private const int FrameSamples = 960; // 20ms @ 48kHz
    private const int FrameBytes   = FrameSamples * Channels * sizeof(short);

    public static Task Start(
        BrokerConnection conn, Func<string?> getTargetPeerId, Func<bool> isMuted,
        ILogger log, CancellationToken ct)
        => Task.Run(() => RunAsync(conn, getTargetPeerId, isMuted, log, ct), ct);

    private static async Task RunAsync(
        BrokerConnection conn, Func<string?> getTargetPeerId, Func<bool> isMuted,
        ILogger log, CancellationToken ct)
    {
        WasapiCapture? capture = null;
        try
        {
            capture = new WasapiCapture();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "WASAPI mic capture unavailable — mic passthrough disabled");
            return;
        }

        using (capture)
        {
            var rawBuffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration          = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true,
            };

            ISampleProvider sp = rawBuffer.ToSampleProvider();
            if (capture.WaveFormat.Channels == 1)
                sp = new MonoToStereoSampleProvider(sp);
            if (capture.WaveFormat.SampleRate != SampleRate)
                sp = new WdlResamplingSampleProvider(sp, SampleRate);
            IWaveProvider pcm16 = new SampleToWaveProvider16(sp);

            var encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bitrate = 32_000; // voice — lower than the system-audio path's 64kbps
            var opusBuf = new byte[4000];
            var readBuf = new byte[FrameBytes];

            var frameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(4)
            {
                FullMode     = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

            capture.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded == 0 || isMuted()) return;
                rawBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

                while (pcm16.Read(readBuf, 0, FrameBytes) == FrameBytes)
                {
                    int len;
                    try
                    {
                        var pcm = new short[FrameSamples * Channels];
                        Buffer.BlockCopy(readBuf, 0, pcm, 0, FrameBytes);
                        len = encoder.Encode(pcm.AsSpan(), FrameSamples, opusBuf.AsSpan(), opusBuf.Length);
                    }
                    catch { continue; }

                    if (len <= 0) continue;

                    var targetId = getTargetPeerId();
                    if (string.IsNullOrEmpty(targetId)) continue;

                    var msg = ViewerWire.BuildAudioMic(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), opusBuf.AsSpan(0, len), targetId);

                    frameChannel.Writer.TryWrite(msg);
                }
            };

            var sender = Task.Run(async () =>
            {
                await foreach (var msg in frameChannel.Reader.ReadAllAsync(ct))
                    await conn.SendAsync(msg, ct);
            }, ct);

            capture.StartRecording();
            log.LogInformation("Mic capture started ({Format})", capture.WaveFormat);

            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { }
            finally
            {
                capture.StopRecording();
                frameChannel.Writer.Complete();
                await sender.ConfigureAwait(false);
                log.LogInformation("Mic capture stopped");
            }
        }
    }
}

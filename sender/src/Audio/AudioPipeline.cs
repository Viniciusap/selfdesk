using System.Threading.Channels;
using Concentus.Enums;
using Concentus.Structs;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SelfDesk.Sender.Network;
using SelfDesk.Sender.Protocol;

namespace SelfDesk.Sender.Audio;

internal static class AudioPipeline
{
    private const int SampleRate   = 48000;
    private const int Channels     = 2;
    private const int FrameSamples = 960;             // 20ms @ 48kHz
    private const int FrameBytes   = FrameSamples * Channels * sizeof(short);

    public static Task Start(BrokerConnection conn, string senderId, ILogger log, CancellationToken ct)
        => Task.Run(() => RunAsync(conn, senderId, log, ct), ct);

    private static async Task RunAsync(BrokerConnection conn, string senderId, ILogger log, CancellationToken ct)
    {
        WasapiLoopbackCapture? capture = null;
        try
        {
            capture = new WasapiLoopbackCapture();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "WASAPI loopback não disponível — áudio desativado");
            return;
        }

        using (capture)
        {
            // Build resample chain: device format → 48kHz 16-bit stereo
            var rawBuffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration       = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true,
            };

            ISampleProvider sp = rawBuffer.ToSampleProvider();
            if (capture.WaveFormat.Channels == 1)
                sp = new MonoToStereoSampleProvider(sp);
            if (capture.WaveFormat.SampleRate != SampleRate)
                sp = new WdlResamplingSampleProvider(sp, SampleRate);
            IWaveProvider pcm16 = new SampleToWaveProvider16(sp);

            var encoder  = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
            encoder.Bitrate = 64_000;
            var opusBuf  = new byte[4000];
            var readBuf  = new byte[FrameBytes];

            // Channel decouples the NAudio capture thread from the send task
            var frameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(4)
            {
                FullMode     = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

            capture.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded == 0) return;
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

                    var msg = WireProtocol.BuildAudioFrame(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Channels, opusBuf.AsSpan(0, len), senderId);

                    frameChannel.Writer.TryWrite(msg);
                }
            };

            var sender = Task.Run(async () =>
            {
                await foreach (var msg in frameChannel.Reader.ReadAllAsync(ct))
                    await conn.SendAsync(msg, ct);
            }, ct);

            capture.StartRecording();
            log.LogInformation("Áudio: captura iniciada ({Format})", capture.WaveFormat);

            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { }
            finally
            {
                capture.StopRecording();
                frameChannel.Writer.Complete();
                await sender.ConfigureAwait(false);
                log.LogInformation("Áudio: captura encerrada");
            }
        }
    }
}

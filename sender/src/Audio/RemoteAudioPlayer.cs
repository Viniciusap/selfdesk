using Concentus;
using Concentus.Structs;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SelfDesk.Sender.Protocol;

namespace SelfDesk.Sender.Audio;

// Issue #21: plays viewer mic audio (AUDIO_MIC) through the sender's default output
// device — the reverse path of the existing system-audio loopback (AudioPipeline.cs).
// Opt-in via MIC_ENABLED in .env, disabled by default: a controlled machine's speakers
// should not unexpectedly play audio from the remote operator without the machine
// owner's consent.
public sealed class RemoteAudioPlayer : IDisposable
{
    private const int SampleRate   = 48000;
    private const int Channels     = 2;
    private const int FrameSamples = 960;

    private readonly BufferedWaveProvider _buffer;
    private          WasapiOut?           _output;
    private readonly ILogger              _log;
    private          IOpusDecoder?        _decoder;

    public RemoteAudioPlayer(ILogger log)
    {
        _log = log;
        WasapiOut? output = null;
        try
        {
            var fmt = new WaveFormat(SampleRate, 16, Channels);
            _buffer = new BufferedWaveProvider(fmt)
            {
                BufferDuration          = TimeSpan.FromMilliseconds(400),
                DiscardOnBufferOverflow = true,
            };
            output = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
            output.Init(_buffer);
            output.Play();
            _output = output;
            _log.LogInformation("Remote mic playback started (WASAPI, 48kHz stereo)");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WASAPI playback unavailable — remote mic disabled");
            output?.Dispose();
            _output = null;
            _buffer ??= new BufferedWaveProvider(new WaveFormat(SampleRate, 16, Channels));
        }
    }

    public void OnAudioMic(ReadOnlyMemory<byte> payload)
    {
        if (_output is null) return;
        if (payload.Length <= AudioFrameOffsets.Data) return;
        var opusData = payload.Span[AudioFrameOffsets.Data..];

        try
        {
            _decoder ??= OpusCodecFactory.CreateDecoder(SampleRate, Channels);
            var pcm = new short[FrameSamples * Channels];
            _decoder.Decode(opusData, pcm.AsSpan(), FrameSamples, false);
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            _buffer.AddSamples(bytes, 0, bytes.Length);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to decode remote mic frame"); }
    }

    public void Dispose()
    {
        _output?.Stop();
        _output?.Dispose();
    }
}

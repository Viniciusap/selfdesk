using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace SelfDesk.Viewer.Audio;

public sealed class WasapiAudioPlayer : IAudioPlayer
{
    private const int SampleRate = 48000;
    private const int Channels   = 2;

    private readonly BufferedWaveProvider _buffer;
    private readonly WasapiOut?           _output;
    private readonly ILogger              _log;
    private          bool                 _muted;

    public WasapiAudioPlayer(ILogger<WasapiAudioPlayer> log)
    {
        _log = log;
        try
        {
            var fmt = new WaveFormat(SampleRate, 16, Channels);
            _buffer = new BufferedWaveProvider(fmt)
            {
                BufferDuration          = TimeSpan.FromMilliseconds(400),
                DiscardOnBufferOverflow = true,
            };
            _output = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
            _output.Init(_buffer);
            _output.Play();
            _log.LogInformation("Reprodução de áudio iniciada (WASAPI, 48kHz estéreo)");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WASAPI playback não disponível — áudio desativado");
            _buffer ??= new BufferedWaveProvider(new WaveFormat(SampleRate, 16, Channels));
        }
    }

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (_output is not null)
                _output.Volume = value ? 0f : 1f;
        }
    }

    public void AddSamples(short[] pcm)
    {
        if (_muted || _output is null) return;
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        _buffer.AddSamples(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        _output?.Stop();
        _output?.Dispose();
    }
}

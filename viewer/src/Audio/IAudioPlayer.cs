namespace SelfDesk.Viewer.Audio;

public interface IAudioPlayer : IDisposable
{
    bool IsMuted { get; set; }
    void AddSamples(short[] pcm);
}

namespace SelfDesk.Sender.Capture;

public readonly record struct CapturedFrame(
    int Width,
    int Height,
    byte[] Bgra,
    long TimestampMs);

public interface IScreenCapturer : IDisposable
{
    CapturedFrame CaptureFrame();
    void SwitchMonitor(int monitorIndex);
}

namespace SelfDesk.Sender.Capture;

public sealed class FakeScreenCapturer : IScreenCapturer
{
    private readonly int _width;
    private readonly int _height;

    public FakeScreenCapturer(int width = 1920, int height = 1080)
    {
        _width  = width;
        _height = height;
    }

    public CapturedFrame CaptureFrame()
    {
        var bgra = new byte[_width * _height * 4];
        new Random().NextBytes(bgra);
        return new CapturedFrame(_width, _height, bgra, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void Dispose() { }
}

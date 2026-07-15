using System.Runtime.InteropServices;
using SkiaSharp;
using SelfDesk.Sender.Capture;
using SelfDesk.Sender.Protocol;

namespace SelfDesk.Sender.Encode;

public sealed class JpegFrameEncoder : IFrameEncoder
{
    private volatile int _quality;

    public JpegFrameEncoder(int quality = 75)
    {
        _quality = quality;
    }

    public EncodedFrame Encode(CapturedFrame frame)
    {
        using var bitmap = new SKBitmap(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        Marshal.Copy(frame.Bgra, 0, bitmap.GetPixels(), frame.Bgra.Length);

        using var img  = SKImage.FromBitmap(bitmap);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, _quality);

        return new EncodedFrame(
            Data:        data.ToArray(),
            Codec:       VideoFrameOffsets.CodecJpeg,
            Flags:       0,
            Width:       frame.Width,
            Height:      frame.Height,
            TimestampMs: frame.TimestampMs);
    }

    public void RequestKeyframe() { }
    public void UpdateBitrate(long bitsPerSecond) { }

    // Issue #14: adaptive quality hint from the viewer (RTT-based).
    public void UpdateQuality(int quality) => _quality = Math.Clamp(quality, 1, 100);

    public void Dispose() { }
}

using SkiaSharp;
using SelfDesk.Agent.Capture;
using SelfDesk.Agent.Protocol;

namespace SelfDesk.Agent.Encode;

public sealed class JpegFrameEncoder : IFrameEncoder
{
    private readonly int _quality;

    public JpegFrameEncoder(int quality = 75)
    {
        _quality = quality;
    }

    public EncodedFrame Encode(CapturedFrame frame)
    {
        using var bitmap = new SKBitmap(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        unsafe
        {
            fixed (byte* src = frame.Bgra)
            {
                var dst = bitmap.GetPixels();
                Buffer.MemoryCopy(src, (void*)dst, frame.Bgra.LongLength, frame.Bgra.LongLength);
            }
        }

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

    public void Dispose() { }
}

using System.IO;
using SkiaSharp;

namespace SelfDesk.Viewer.Decode;

public sealed class JpegFrameDecoder : IFrameDecoder
{
    public DecodedFrame Decode(ReadOnlySpan<byte> data, int width, int height, long timestampMs)
    {
        using var bitmap = SKBitmap.Decode(data);
        if (bitmap is null)
            throw new InvalidDataException("Failed to decode JPEG frame");

        const int MaxDim = 7680; // 8K
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || bitmap.Width > MaxDim || bitmap.Height > MaxDim)
            throw new InvalidDataException(
                $"JPEG dimensions {bitmap.Width}×{bitmap.Height} exceed limit ({MaxDim}px)");

        long bgraSize = (long)bitmap.Width * bitmap.Height * 4;
        if (bgraSize > int.MaxValue)
            throw new InvalidDataException($"Frame too large to allocate ({bgraSize:N0} bytes)");
        var bgra = new byte[(int)bgraSize];

        using var converted = bitmap.Copy(SKColorType.Bgra8888);
        unsafe
        {
            var src = (byte*)converted.GetPixels();
            fixed (byte* dst = bgra)
                Buffer.MemoryCopy(src, dst, bgra.LongLength, bgra.LongLength);
        }

        return new DecodedFrame(bitmap.Width, bitmap.Height, bgra, timestampMs);
    }
}

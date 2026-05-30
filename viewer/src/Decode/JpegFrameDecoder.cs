using System.IO;
using SkiaSharp;

namespace SelfDesk.Viewer.Decode;

public sealed class JpegFrameDecoder : IFrameDecoder
{
    public DecodedFrame Decode(ReadOnlySpan<byte> data, int width, int height, long timestampMs)
    {
        using var bitmap = SKBitmap.Decode(data);
        if (bitmap is null)
            throw new InvalidDataException("Falha ao decodificar JPEG");

        var bgra = new byte[bitmap.Width * bitmap.Height * 4];

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

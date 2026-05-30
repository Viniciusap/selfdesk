namespace SelfDesk.Viewer.Decode;

public readonly record struct DecodedFrame(
    int Width,
    int Height,
    byte[] Bgra,
    long TimestampMs);

public interface IFrameDecoder
{
    DecodedFrame Decode(ReadOnlySpan<byte> data, int width, int height, long timestampMs);
}

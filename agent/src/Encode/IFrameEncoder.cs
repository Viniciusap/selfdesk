using SelfDesk.Agent.Capture;

namespace SelfDesk.Agent.Encode;

public readonly record struct EncodedFrame(
    byte[] Data,
    byte Codec,
    byte Flags,
    int Width,
    int Height,
    long TimestampMs);

public interface IFrameEncoder : IDisposable
{
    EncodedFrame Encode(CapturedFrame frame);
}

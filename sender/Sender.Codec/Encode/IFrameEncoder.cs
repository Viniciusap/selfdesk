using SelfDesk.Sender.Capture;

namespace SelfDesk.Sender.Encode;

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
    void RequestKeyframe();
    void UpdateBitrate(long bitsPerSecond);

    // Issue #14: adaptive JPEG quality hint (1-100). No-op for H264 encoders,
    // which use UpdateBitrate instead.
    void UpdateQuality(int quality);
}

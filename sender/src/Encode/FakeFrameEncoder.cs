using SelfDesk.Sender.Capture;
using SelfDesk.Sender.Protocol;

namespace SelfDesk.Sender.Encode;

public sealed class FakeFrameEncoder : IFrameEncoder
{
    public EncodedFrame Encode(CapturedFrame frame) =>
        new(
            Data:        [0xFF, 0xD8, 0xFF, 0xD9],
            Codec:       VideoFrameOffsets.CodecJpeg,
            Flags:       0,
            Width:       (int)frame.Width,
            Height:      (int)frame.Height,
            TimestampMs: frame.TimestampMs);

    public void RequestKeyframe() { }
    public void UpdateBitrate(long bitsPerSecond) { }

    public void Dispose() { }
}

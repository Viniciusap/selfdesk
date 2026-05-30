using SelfDesk.Agent.Capture;
using SelfDesk.Agent.Protocol;

namespace SelfDesk.Agent.Encode;

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

    public void Dispose() { }
}

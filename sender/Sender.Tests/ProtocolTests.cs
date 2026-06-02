using System.Buffers.Binary;
using System.Text;
using SelfDesk.Sender.Protocol;
using Xunit;

namespace SelfDesk.Sender.Tests;

public class WireProtocolTests
{
    [Fact]
    public void BuildEnvelope_VersionByte_Is01()
    {
        var msg = WireProtocol.BuildEnvelope(MessageType.Hello, "x", []);
        Assert.Equal(ProtocolVersion.Current, msg[0]);
    }

    [Fact]
    public void BuildEnvelope_TypeByte_MatchesInput()
    {
        var msg = WireProtocol.BuildEnvelope(MessageType.AuthOk, "", []);
        Assert.Equal(MessageType.AuthOk, msg[1]);
    }

    [Fact]
    public void BuildEnvelope_PeerId_PaddedTo16BytesWithNulls()
    {
        const string peerId = "laptop-01";
        var msg = WireProtocol.BuildEnvelope(MessageType.Hello, peerId, []);

        var peerIdSlice = msg.AsSpan(ProtocolSizes.PeerIdOffset, ProtocolSizes.PeerIdSize);
        var encoded = Encoding.UTF8.GetBytes(peerId);
        Assert.Equal(encoded, peerIdSlice[..encoded.Length].ToArray());
        Assert.All(peerIdSlice[encoded.Length..].ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void BuildEnvelope_Length_BigEndianUInt32()
    {
        var payload = new byte[300];
        var msg     = WireProtocol.BuildEnvelope(MessageType.VideoFrame, "", payload);
        var length  = BinaryPrimitives.ReadUInt32BigEndian(msg.AsSpan(ProtocolSizes.LengthOffset));
        Assert.Equal(300u, length);
    }

    [Fact]
    public void BuildEnvelope_TotalSize_HeaderPlusPayload()
    {
        var payload = new byte[50];
        var msg     = WireProtocol.BuildEnvelope(MessageType.Auth, "", payload);
        Assert.Equal(ProtocolSizes.HeaderSize + 50, msg.Length);
    }

    [Fact]
    public void ParseHeader_ReturnsCorrectFields()
    {
        var payload = Encoding.UTF8.GetBytes("test");
        var msg     = WireProtocol.BuildEnvelope(MessageType.Auth, "agent-x", payload);
        var (type, peerId, length) = WireProtocol.ParseHeader(msg);

        Assert.Equal(MessageType.Auth, type);
        Assert.Equal("agent-x", peerId);
        Assert.Equal((uint)payload.Length, length);
    }

    [Fact]
    public void DecodePeerId_EmptyPeerId_ReturnsEmptyString()
    {
        var msg = WireProtocol.BuildEnvelope(MessageType.Pong, "", []);
        Assert.Equal(string.Empty, WireProtocol.DecodePeerId(msg));
    }

    [Fact]
    public void DecodePeerId_TruncatesAtNullByte()
    {
        const string id = "ab";
        var msg = WireProtocol.BuildEnvelope(MessageType.Ping, id, []);
        Assert.Equal(id, WireProtocol.DecodePeerId(msg));
    }

    [Fact]
    public void BuildHello_PayloadIsValidJson()
    {
        var msg  = WireProtocol.BuildHello("laptop-01", "sender");
        var body = msg.AsSpan(ProtocolSizes.HeaderSize);
        var json = System.Text.Json.JsonDocument.Parse(body.ToArray());
        Assert.Equal("laptop-01", json.RootElement.GetProperty("agentId").GetString());
        Assert.Equal("sender",    json.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public void BuildAuth_Payload_Is32Bytes()
    {
        var nonce = new byte[32];
        var msg   = WireProtocol.BuildAuth(nonce, "secret");
        var (type, _, length) = WireProtocol.ParseHeader(msg);
        Assert.Equal(MessageType.Auth, type);
        Assert.Equal(32u, length);
    }

    [Fact]
    public void BuildVideoFrame_Header_BigEndianFields()
    {
        var data      = new byte[100];
        var msg       = WireProtocol.BuildVideoFrame(12345L, 1920, 1080, VideoFrameOffsets.CodecJpeg, 0, data, "laptop-01");
        var payload   = msg.AsSpan(ProtocolSizes.HeaderSize);

        var ts     = BinaryPrimitives.ReadInt64BigEndian(payload[VideoFrameOffsets.Timestamp..]);
        var width  = BinaryPrimitives.ReadUInt16BigEndian(payload[VideoFrameOffsets.Width..]);
        var height = BinaryPrimitives.ReadUInt16BigEndian(payload[VideoFrameOffsets.Height..]);

        Assert.Equal(12345L,   ts);
        Assert.Equal(1920,     (int)width);
        Assert.Equal(1080,     (int)height);
        Assert.Equal(VideoFrameOffsets.CodecJpeg, payload[VideoFrameOffsets.Codec]);
    }

    [Fact]
    public void BuildPing_Payload_BigEndian8Bytes()
    {
        var ts  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var msg = WireProtocol.BuildPing(ts);
        var (type, _, length) = WireProtocol.ParseHeader(msg);
        Assert.Equal(MessageType.Ping, type);
        Assert.Equal(8u, length);
        var echo = BinaryPrimitives.ReadInt64BigEndian(msg.AsSpan(ProtocolSizes.HeaderSize));
        Assert.Equal(ts, echo);
    }
}

public class FakeInjectorTests
{
    [Fact]
    public void FakeInputInjector_RecordsPayload()
    {
        var injector = new Inject.FakeInputInjector();
        var payload  = new byte[] { 0x01, 0x10, 0x00, 0xFF, 0xFF };
        injector.Inject(payload);
        Assert.Single(injector.Received);
        Assert.Equal(payload, injector.Received[0]);
    }
}

public class FakeEncoderTests
{
    [Fact]
    public void FakeFrameEncoder_ReturnsJpegCodec()
    {
        var encoder = new Encode.FakeFrameEncoder();
        var frame   = new Capture.CapturedFrame(320, 240, new byte[320 * 240 * 4], 0L);
        var encoded = encoder.Encode(frame);
        Assert.Equal(VideoFrameOffsets.CodecJpeg, encoded.Codec);
        Assert.Equal(320, encoded.Width);
        Assert.Equal(240, encoded.Height);
    }
}

public class JpegEncoderTests
{
    [Fact]
    public void JpegFrameEncoder_ProducesNonEmptyData()
    {
        var encoder = new Encode.JpegFrameEncoder(75);
        var bgra    = new byte[64 * 64 * 4];
        new Random(42).NextBytes(bgra);
        var frame   = new Capture.CapturedFrame(64, 64, bgra, 0L);
        var encoded = encoder.Encode(frame);
        Assert.Equal(VideoFrameOffsets.CodecJpeg, encoded.Codec);
        Assert.NotEmpty(encoded.Data);
        Assert.Equal(64, encoded.Width);
        Assert.Equal(64, encoded.Height);
    }

    [Fact]
    public void JpegFrameEncoder_HigherQuality_LargerOutput()
    {
        var bgra = new byte[64 * 64 * 4];
        new Random(42).NextBytes(bgra);
        var frame = new Capture.CapturedFrame(64, 64, bgra, 0L);

        var lo  = new Encode.JpegFrameEncoder(10).Encode(frame);
        var hi  = new Encode.JpegFrameEncoder(95).Encode(frame);
        Assert.True(hi.Data.Length > lo.Data.Length, "Q95 deve ser maior que Q10");
    }
}

public class CoordinateNormalizationTests
{
    [Theory]
    [InlineData(0.0, 0.0, 0, 0)]
    [InlineData(1.0, 1.0, 65535, 65535)]
    [InlineData(0.5, 0.5, 32767, 32767)]
    [InlineData(0.0, 1.0, 0, 65535)]
    public void Normalize_CoversFullRange(double relX, double relY, ushort expX, ushort expY)
    {
        var nx = (ushort)Math.Clamp((int)(relX * 65535), 0, 65535);
        var ny = (ushort)Math.Clamp((int)(relY * 65535), 0, 65535);
        Assert.Equal(expX, nx);
        Assert.Equal(expY, ny);
    }
}

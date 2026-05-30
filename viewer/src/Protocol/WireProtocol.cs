using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SelfDesk.Viewer.Protocol;

public static class WireProtocol
{
    public static byte[] BuildEnvelope(byte type, string peerId, ReadOnlySpan<byte> payload)
    {
        var buf = new byte[ProtocolSizes.HeaderSize + payload.Length];
        buf[0] = ProtocolVersion.Current;
        buf[1] = type;

        var peerIdBytes = Encoding.UTF8.GetBytes(peerId);
        var copyLen = Math.Min(peerIdBytes.Length, ProtocolSizes.PeerIdSize);
        peerIdBytes.AsSpan(0, copyLen).CopyTo(buf.AsSpan(ProtocolSizes.PeerIdOffset));

        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(ProtocolSizes.LengthOffset), (uint)payload.Length);

        if (payload.Length > 0)
            payload.CopyTo(buf.AsSpan(ProtocolSizes.HeaderSize));

        return buf;
    }

    public static string DecodePeerId(ReadOnlySpan<byte> header)
    {
        var slice = header.Slice(ProtocolSizes.PeerIdOffset, ProtocolSizes.PeerIdSize);
        var nullIdx = slice.IndexOf((byte)0);
        return Encoding.UTF8.GetString(nullIdx < 0 ? slice : slice[..nullIdx]);
    }

    public static (byte type, string peerId, uint length) ParseHeader(ReadOnlySpan<byte> header)
    {
        var type   = header[1];
        var peerId = DecodePeerId(header);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header[ProtocolSizes.LengthOffset..]);
        return (type, peerId, length);
    }

    public static byte[] BuildHello(string agentId, string role)
    {
        var json    = JsonSerializer.Serialize(new { version = 1, role, agentId });
        var payload = Encoding.UTF8.GetBytes(json);
        return BuildEnvelope(MessageType.Hello, agentId, payload);
    }

    public static byte[] BuildAuth(ReadOnlySpan<byte> nonce, string sharedSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        var hash = hmac.ComputeHash(nonce.ToArray());
        return BuildEnvelope(MessageType.Auth, string.Empty, hash);
    }

    public static byte[] BuildPing(long timestampMs)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(payload, timestampMs);
        return BuildEnvelope(MessageType.Ping, string.Empty, payload);
    }

    public static byte[] BuildPong(ReadOnlySpan<byte> pingPayload) =>
        BuildEnvelope(MessageType.Pong, string.Empty, pingPayload);

    public static byte[] BuildInputEvent(byte kind, string targetPeerId, ReadOnlySpan<byte> kindPayload)
    {
        var payload = new byte[1 + kindPayload.Length];
        payload[0] = kind;
        kindPayload.CopyTo(payload.AsSpan(1));
        return BuildEnvelope(MessageType.InputEvent, targetPeerId, payload);
    }

    public static byte[] BuildMouseMove(string targetPeerId, ushort x, ushort y)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(data, x);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), y);
        return BuildInputEvent(InputEventKind.MouseMove, targetPeerId, data);
    }

    public static byte[] BuildMouseButton(string targetPeerId, byte btn, byte state, ushort x, ushort y)
    {
        var data = new byte[6];
        data[0] = btn;
        data[1] = state;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), x);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), y);
        return BuildInputEvent(InputEventKind.MouseButton, targetPeerId, data);
    }

    public static byte[] BuildKey(string targetPeerId, ushort vk, byte state, byte mods)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(data, vk);
        data[2] = state;
        data[3] = mods;
        return BuildInputEvent(InputEventKind.Key, targetPeerId, data);
    }

    public static byte[] BuildBye() =>
        BuildEnvelope(MessageType.Bye, string.Empty, ReadOnlySpan<byte>.Empty);

    public static byte[] BuildClipboard(ReadOnlySpan<byte> utf8Text, string targetPeerId = "") =>
        BuildEnvelope(MessageType.Clipboard, targetPeerId, utf8Text);
}

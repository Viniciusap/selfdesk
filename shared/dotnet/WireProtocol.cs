using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SelfDesk.Protocol;

/// <summary>
/// Framing e mensagens de controle compartilhadas entre sender e viewer.
/// Todos os campos multibyte são big-endian (seção 5 do SPEC).
/// </summary>
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

    public static byte[] BuildBye() =>
        BuildEnvelope(MessageType.Bye, string.Empty, ReadOnlySpan<byte>.Empty);

    public static byte[] BuildClipboard(ReadOnlySpan<byte> utf8Text, string peerId) =>
        BuildEnvelope(MessageType.Clipboard, peerId, utf8Text);
}

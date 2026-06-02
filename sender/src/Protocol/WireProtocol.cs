using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SelfDesk.Sender.Protocol;

/// <summary>
/// Framing e build de mensagens do protocolo SelfDesk.
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

    public static byte[] BuildHello(string agentId, string role, string? mac = null)
    {
        var ver = System.Reflection.Assembly
            .GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var json    = JsonSerializer.Serialize(new { version = 1, role, agentId, mac, senderVersion = ver });
        var payload = Encoding.UTF8.GetBytes(json);
        return BuildEnvelope(MessageType.Hello, agentId, payload);
    }

    public static string GetLocalMac()
    {
        try
        {
            var iface = System.Net.NetworkInformation.NetworkInterface
                .GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                    n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
                    n.GetPhysicalAddress().GetAddressBytes().Length == 6);
            if (iface is null) return string.Empty;
            var bytes = iface.GetPhysicalAddress().GetAddressBytes();
            return string.Join(":", bytes.Select(b => b.ToString("X2")));
        }
        catch { return string.Empty; }
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

    public static byte[] BuildVideoFrame(
        long timestampMs,
        ushort width,
        ushort height,
        byte codec,
        byte flags,
        ReadOnlySpan<byte> data,
        string agentId)
    {
        var frameHeader = new byte[VideoFrameOffsets.Data];
        BinaryPrimitives.WriteInt64BigEndian(frameHeader.AsSpan(VideoFrameOffsets.Timestamp), timestampMs);
        BinaryPrimitives.WriteUInt16BigEndian(frameHeader.AsSpan(VideoFrameOffsets.Width), width);
        BinaryPrimitives.WriteUInt16BigEndian(frameHeader.AsSpan(VideoFrameOffsets.Height), height);
        frameHeader[VideoFrameOffsets.Codec]  = codec;
        frameHeader[VideoFrameOffsets.Flags]  = flags;

        var payload = new byte[frameHeader.Length + data.Length];
        frameHeader.CopyTo(payload, 0);
        data.CopyTo(payload.AsSpan(frameHeader.Length));

        return BuildEnvelope(MessageType.VideoFrame, agentId, payload);
    }

    public static (byte kind, ReadOnlyMemory<byte> rest) ParseInputEvent(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length == 0) throw new InvalidDataException("INPUT_EVENT payload vazio");
        return (payload.Span[0], payload[1..]);
    }

    public static byte[] BuildClipboard(ReadOnlySpan<byte> utf8Text, string agentId) =>
        BuildEnvelope(MessageType.Clipboard, agentId, utf8Text);

    public static byte[] BuildAudioFrame(
        long timestampMs, int channels, ReadOnlySpan<byte> opusData, string agentId)
    {
        var header = new byte[12];
        BinaryPrimitives.WriteInt64BigEndian(header, timestampMs);
        header[8]  = (byte)channels;
        header[9]  = 0; // sample rate id: 0 = 48000 Hz
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), 960); // frame samples
        var payload = new byte[header.Length + opusData.Length];
        header.CopyTo(payload, 0);
        opusData.CopyTo(payload.AsSpan(header.Length));
        return BuildEnvelope(MessageType.AudioFrame, agentId, payload);
    }

    public static byte[] BuildMonitorList(
        IReadOnlyList<SelfDesk.Sender.Capture.MonitorInfo> monitors, string agentId)
    {
        var json = JsonSerializer.Serialize(monitors.Select(m => new
        {
            index     = m.Index,
            width     = m.Width,
            height    = m.Height,
            name      = m.Name,
            isPrimary = m.IsPrimary,
        }));
        return BuildEnvelope(MessageType.MonitorList, agentId, Encoding.UTF8.GetBytes(json));
    }
}

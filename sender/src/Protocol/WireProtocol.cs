using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SelfDesk.Protocol;

namespace SelfDesk.Sender.Protocol;

public static class WireProtocol
{
    public static byte[] BuildHello(string agentId, string role, string? mac = null)
    {
        var ver = System.Reflection.Assembly
            .GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var json    = JsonSerializer.Serialize(new { version = 1, role, agentId, mac, senderVersion = ver });
        var payload = Encoding.UTF8.GetBytes(json);
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.Hello, agentId, payload);
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

        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.VideoFrame, agentId, payload);
    }

    public static (byte kind, ReadOnlyMemory<byte> rest) ParseInputEvent(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length == 0) throw new InvalidDataException("INPUT_EVENT payload is empty");
        return (payload.Span[0], payload[1..]);
    }

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
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.AudioFrame, agentId, payload);
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
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.MonitorList, agentId, Encoding.UTF8.GetBytes(json));
    }
}

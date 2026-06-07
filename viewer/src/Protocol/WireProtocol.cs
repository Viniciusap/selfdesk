using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SelfDesk.Protocol;

namespace SelfDesk.Viewer.Protocol;

public static class WireProtocol
{
    public static byte[] BuildHello(string agentId, string role)
    {
        var json    = JsonSerializer.Serialize(new { version = 1, role, agentId });
        var payload = Encoding.UTF8.GetBytes(json);
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.Hello, agentId, payload);
    }

    public static byte[] BuildInputEvent(byte kind, string targetPeerId, ReadOnlySpan<byte> kindPayload)
    {
        var payload = new byte[1 + kindPayload.Length];
        payload[0] = kind;
        kindPayload.CopyTo(payload.AsSpan(1));
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.InputEvent, targetPeerId, payload);
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

    public static byte[] BuildRequestIdr(string targetPeerId) =>
        SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.RequestIdr, targetPeerId, ReadOnlySpan<byte>.Empty);

    public static byte[] BuildRemoteReboot(string targetPeerId) =>
        SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.RemoteReboot, targetPeerId, ReadOnlySpan<byte>.Empty);

    public static byte[] BuildSelectMonitor(string targetPeerId, int monitorIndex)
    {
        var json = JsonSerializer.Serialize(new { monitorIndex });
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.SelectMonitor, targetPeerId, Encoding.UTF8.GetBytes(json));
    }

    // FILE_HEADER: [0..3] transfer_id + [4..11] total_size + [12..13] name_len + [14..] filename
    public static byte[] BuildFileHeader(uint transferId, long totalSize, string fileName, string targetPeerId)
    {
        var nameBytes = Encoding.UTF8.GetBytes(fileName);
        var payload   = new byte[4 + 8 + 2 + nameBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload, transferId);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(4), totalSize);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(12), (ushort)nameBytes.Length);
        nameBytes.CopyTo(payload, 14);
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.FileHeader, targetPeerId, payload);
    }

    // FILE_CHUNK: [0..3] transfer_id + data
    public static byte[] BuildFileChunk(uint transferId, ReadOnlySpan<byte> data, string targetPeerId)
    {
        var payload = new byte[4 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload, transferId);
        data.CopyTo(payload.AsSpan(4));
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.FileChunk, targetPeerId, payload);
    }

    // FILE_DONE: [0..3] transfer_id
    public static byte[] BuildFileDone(uint transferId, string targetPeerId)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, transferId);
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.FileDone, targetPeerId, payload);
    }

    // FILE_ERROR: [0..3] transfer_id
    public static byte[] BuildFileError(uint transferId, string targetPeerId)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, transferId);
        return SelfDesk.Protocol.WireProtocol.BuildEnvelope(MessageType.FileError, targetPeerId, payload);
    }
}

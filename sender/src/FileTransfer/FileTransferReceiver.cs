using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SelfDesk.Sender.FileTransfer;

/// <summary>
/// Receives file transfers from the viewer via the broker and saves them to disk.
/// Protocol: FILE_HEADER → FILE_CHUNK* → FILE_DONE
/// </summary>
public sealed class FileTransferReceiver
{
    private const long MaxFileSizeBytes = 10L * 1024 * 1024 * 1024; // 10 GB

    private readonly string _outputDir;
    private readonly ILogger _log;

    private uint _activeId;
    private string? _fileName;
    private long _totalSize;
    private FileStream? _stream;
    private long _received;

    public FileTransferReceiver(ILogger log)
    {
        _log = log;
        _outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "SelfDesk");
        Directory.CreateDirectory(_outputDir);
    }

    // Payload: [0..3] transfer_id + [4..11] total_size + [12..13] name_len + [14..] filename
    public void OnFileHeader(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < 14) return;
        var span = payload.Span;

        var id        = BinaryPrimitives.ReadUInt32BigEndian(span[0..]);
        var totalSize = BinaryPrimitives.ReadInt64BigEndian(span[4..]);
        var nameLen   = BinaryPrimitives.ReadUInt16BigEndian(span[12..]);

        if (14 + nameLen > span.Length) return;
        var fileName  = Encoding.UTF8.GetString(span.Slice(14, nameLen));
        var safeName  = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName)) return;
        if (totalSize < 0 || totalSize > MaxFileSizeBytes)
        {
            _log.LogWarning("File header rejected: totalSize={Size} exceeds limit", totalSize);
            return;
        }

        AbortCurrent();

        _activeId  = id;
        _fileName  = safeName;
        _totalSize = totalSize;
        _received  = 0;

        var destPath = UniquePath(_outputDir, safeName);
        _stream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);

        _log.LogInformation("Receiving file {Name} ({Size:N0} bytes) → {Path}", safeName, totalSize, destPath);
    }

    // Payload: [0..3] transfer_id + data
    public void OnFileChunk(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < 5) return;
        var span = payload.Span;
        var id   = BinaryPrimitives.ReadUInt32BigEndian(span[0..]);
        if (id != _activeId || _stream is null) return;

        var data = span[4..];
        if (_received + data.Length > _totalSize)
        {
            _log.LogWarning("File chunk overrun for {Name} — aborting", _fileName);
            AbortCurrent();
            return;
        }
        _stream.Write(data);
        _received += data.Length;
    }

    // Payload: [0..3] transfer_id
    public void OnFileDone(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < 4) return;
        var id = BinaryPrimitives.ReadUInt32BigEndian(payload.Span);
        if (id != _activeId || _stream is null) return;

        _stream.Flush();
        _stream.Dispose();
        _stream = null;
        _log.LogInformation("File received: {Name} ({Bytes:N0} bytes)", _fileName, _received);

        _fileName  = null;
        _activeId  = 0;
        _received  = 0;
        _totalSize = 0;
    }

    public void OnFileError(ReadOnlyMemory<byte> payload)
    {
        _log.LogWarning("Transfer cancelled by the sender.");
        AbortCurrent();
    }

    private void AbortCurrent()
    {
        if (_stream is null) return;
        _stream.Dispose();
        _stream = null;
        _fileName = null;
    }

    private static string UniquePath(string dir, string name)
    {
        var dest = Path.Combine(dir, name);
        if (!File.Exists(dest)) return dest;
        var ext  = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        int i = 1;
        do { dest = Path.Combine(dir, $"{stem} ({i++}){ext}"); }
        while (File.Exists(dest));
        return dest;
    }
}

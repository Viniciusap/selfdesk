namespace SelfDesk.Sender.Diagnostics;

// Issue #24: opt-in per-frame latency CSV (capture + encode stages), correlated
// with the viewer's own benchmark CSV via the shared VIDEO_FRAME TIMESTAMP field.
// See scripts/benchmark.py. No-op when disabled — never opens the file.
public sealed class BenchmarkLog : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();

    public BenchmarkLog(bool enabled, string path)
    {
        if (!enabled) return;
        var isNew = !File.Exists(path);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        if (isNew) _writer.WriteLine("frameTimestampMs,captureMs,encodeMs");
    }

    public void WriteFrame(long frameTimestampMs, double captureMs, double encodeMs)
    {
        if (_writer is null) return;
        lock (_gate)
            _writer.WriteLine($"{frameTimestampMs},{captureMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},{encodeMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
    }

    public void Dispose() => _writer?.Dispose();
}

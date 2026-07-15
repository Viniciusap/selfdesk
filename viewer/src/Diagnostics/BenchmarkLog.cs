using System;
using System.IO;

namespace SelfDesk.Viewer.Diagnostics;

// Issue #24: opt-in per-frame latency CSV (one-way wire + decode stages),
// correlated with the sender's own benchmark CSV via the shared VIDEO_FRAME
// TIMESTAMP field. See scripts/benchmark.py. No-op when disabled — never opens the file.
public sealed class BenchmarkLog : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();

    public BenchmarkLog(bool enabled, string path)
    {
        if (!enabled) return;
        var isNew = !File.Exists(path);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        if (isNew) _writer.WriteLine("frameTimestampMs,oneWayMs,decodeMs,totalMs");
    }

    public void WriteFrame(long frameTimestampMs, long oneWayMs, double decodeMs, double totalMs)
    {
        if (_writer is null) return;
        lock (_gate)
            _writer.WriteLine($"{frameTimestampMs},{oneWayMs},{decodeMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},{totalMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
    }

    public void Dispose() => _writer?.Dispose();
}

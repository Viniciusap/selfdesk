using SelfDesk.Viewer.Diagnostics;
using Xunit;

namespace SelfDesk.Viewer.Tests;

public class BenchmarkLogTests
{
    [Fact]
    public void DisabledNeverCreatesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"selfdesk-bench-{Guid.NewGuid():N}.csv");
        using var log = new BenchmarkLog(enabled: false, path);
        log.WriteFrame(1000, 20, 5.5, 30.5);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void EnabledWritesHeaderAndRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"selfdesk-bench-{Guid.NewGuid():N}.csv");
        try
        {
            using (var log = new BenchmarkLog(enabled: true, path))
            {
                log.WriteFrame(1000, 20, 5.5, 30.5);
                log.WriteFrame(1033, 21, 6.0, 32.0);
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal("frameTimestampMs,oneWayMs,decodeMs,totalMs", lines[0]);
            Assert.Equal("1000,20,5.500,30.500", lines[1]);
            Assert.Equal("1033,21,6.000,32.000", lines[2]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

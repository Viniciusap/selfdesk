using SelfDesk.Sender.Diagnostics;
using Xunit;

namespace SelfDesk.Sender.Tests;

public class BenchmarkLogTests
{
    [Fact]
    public void DisabledNeverCreatesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"selfdesk-bench-{Guid.NewGuid():N}.csv");
        using var log = new BenchmarkLog(enabled: false, path);
        log.WriteFrame(1000, 3.2, 8.1);

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
                log.WriteFrame(1000, 3.2, 8.1);
                log.WriteFrame(1033, 3.5, 8.4);
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal("frameTimestampMs,captureMs,encodeMs", lines[0]);
            Assert.Equal("1000,3.200,8.100", lines[1]);
            Assert.Equal("1033,3.500,8.400", lines[2]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

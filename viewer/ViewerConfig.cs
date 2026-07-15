namespace SelfDesk.Viewer;

public sealed class ViewerConfig
{
    public string SharedSecret { get; set; } = string.Empty;
    public string BrokerHost   { get; set; } = "localhost";
    public int    BrokerPort   { get; set; } = 7000;
    public string TlsCaPath    { get; set; } = string.Empty;
    public bool   BenchmarkEnabled { get; set; } = false; // issue #24: opt-in, off by default
    public string BenchmarkLogPath { get; set; } = "benchmark-viewer.csv";
}

namespace SelfDesk.Agent;

public sealed class AgentConfig
{
    public string AgentId      { get; set; } = "laptop-01";
    public string SharedSecret { get; set; } = string.Empty;
    public string BrokerHost   { get; set; } = "localhost";
    public int    BrokerPort   { get; set; } = 7000;
    public string TlsCaPath    { get; set; } = string.Empty;
    public int    TargetFps    { get; set; } = 30;
    public string Encoder      { get; set; } = "jpeg";
    public int    JpegQuality  { get; set; } = 75;
}

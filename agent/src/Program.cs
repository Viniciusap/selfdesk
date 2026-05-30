using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SelfDesk.Agent;
using SelfDesk.Agent.Capture;
using SelfDesk.Agent.Encode;
using SelfDesk.Agent.Inject;

DotNetEnv.Env.Load();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentConfig>(cfg =>
{
    cfg.AgentId      = GetEnv("AGENT_ID",      "laptop-01");
    cfg.SharedSecret = GetEnv("SHARED_SECRET", string.Empty);
    cfg.BrokerHost   = GetEnv("BROKER_HOST",   "localhost");
    cfg.BrokerPort   = int.TryParse(GetEnv("BROKER_PORT", "7000"), out var p) ? p : 7000;
    cfg.TlsCaPath    = GetEnv("TLS_CA_PATH",   string.Empty);
    cfg.TargetFps    = int.TryParse(GetEnv("TARGET_FPS",  "30"),   out var fps) ? fps : 30;
    cfg.Encoder      = GetEnv("ENCODER",       "jpeg");
    cfg.JpegQuality  = int.TryParse(GetEnv("JPEG_QUALITY", "75"),  out var q) ? q : 75;
});

builder.Services.AddSingleton<IScreenCapturer, FakeScreenCapturer>();
builder.Services.AddSingleton<IFrameEncoder,   FakeFrameEncoder>();
builder.Services.AddSingleton<IInputInjector,  FakeInputInjector>();
builder.Services.AddHostedService<AgentService>();

await builder.Build().RunAsync();

static string GetEnv(string key, string def) =>
    Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : def;

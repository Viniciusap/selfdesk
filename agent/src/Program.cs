using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Agent;
using SelfDesk.Agent.Capture;
using SelfDesk.Agent.Encode;
using SelfDesk.Agent.Inject;

// Carrega .env se existir (ignorado quando rodando como serviço — usar variáveis de ambiente do serviço)
if (!WindowsServiceHelpers.IsWindowsService())
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

builder.Services.AddSingleton<IScreenCapturer, GdiScreenCapturer>();
builder.Services.AddSingleton<IFrameEncoder>(sp =>
{
    var cfg     = sp.GetRequiredService<IOptions<AgentConfig>>().Value;
    var logFact = sp.GetRequiredService<ILoggerFactory>();
    return cfg.Encoder.ToLowerInvariant() switch
    {
        "qsv" or "nvenc" => (IFrameEncoder)new H264Encoder(
            cfg.Encoder, cfg.TargetFps, logFact.CreateLogger<H264Encoder>()),
        _ => new JpegFrameEncoder(cfg.JpegQuality),
    };
});
builder.Services.AddSingleton<IInputInjector, Win32InputInjector>();
builder.Services.AddHostedService<AgentService>();

// Suporte a Windows Service (Fase 5): dotnet publish + sc.exe create
builder.Services.AddWindowsService(opts => opts.ServiceName = "SelfDesk.Agent");

await builder.Build().RunAsync();

static string GetEnv(string key, string def) =>
    Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : def;

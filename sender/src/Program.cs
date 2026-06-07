using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Sender;
using SelfDesk.Sender.Capture;
using SelfDesk.Sender.Encode;
using SelfDesk.Sender.Inject;

// DPI-aware: captures in physical pixels, input coordinates are correct
SetProcessDpiAwarenessContext(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

// Load .env if present (ignored when running as a service — use service environment variables instead)
if (!WindowsServiceHelpers.IsWindowsService())
    DotNetEnv.Env.Load();

// S25: fail fast if SHARED_SECRET is missing or too short
var _startupSecret = Environment.GetEnvironmentVariable("SHARED_SECRET");
if (string.IsNullOrEmpty(_startupSecret) || _startupSecret.Length < 32)
    throw new InvalidOperationException(
        "SHARED_SECRET missing or too short (minimum 32 chars). Run bootstrap.ps1 -Role sender.");

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SenderConfig>(cfg =>
{
    cfg.SenderId      = GetEnv("SENDER_ID",      "laptop-01");
    cfg.SharedSecret = GetEnv("SHARED_SECRET", string.Empty);
    cfg.BrokerHost   = GetEnv("BROKER_HOST",   "localhost");
    cfg.BrokerPort   = int.TryParse(GetEnv("BROKER_PORT", "7000"), out var p) ? p : 7000;
    cfg.TlsCaPath    = GetEnv("TLS_CA_PATH",   string.Empty);
    cfg.TargetFps    = int.TryParse(GetEnv("TARGET_FPS",  "30"),   out var fps) ? fps : 30;
    cfg.Encoder      = GetEnv("ENCODER",       "jpeg");
    cfg.JpegQuality  = int.TryParse(GetEnv("JPEG_QUALITY", "75"),  out var q) ? q : 75;
    cfg.Capturer     = GetEnv("CAPTURER",      "gdi");
});

builder.Services.AddSingleton<IScreenCapturer>(sp =>
{
    var cfg = sp.GetRequiredService<IOptions<SenderConfig>>().Value;
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Capturer");
    if (cfg.Capturer.Equals("dxgi", StringComparison.OrdinalIgnoreCase))
    {
        try { return new DxgiScreenCapturer(); }
        catch (Exception ex)
        {
            log.LogWarning(ex, "DXGI capturer failed, falling back to GDI");
        }
    }
    return new GdiScreenCapturer();
});
builder.Services.AddSingleton<IFrameEncoder>(sp =>
{
    var cfg     = sp.GetRequiredService<IOptions<SenderConfig>>().Value;
    var logFact = sp.GetRequiredService<ILoggerFactory>();
    return cfg.Encoder.ToLowerInvariant() switch
    {
        "qsv" or "nvenc" => (IFrameEncoder)new H264Encoder(
            cfg.Encoder, cfg.TargetFps, logFact.CreateLogger<H264Encoder>()),
        _ => new JpegFrameEncoder(cfg.JpegQuality),
    };
});
builder.Services.AddSingleton<IInputInjector, Win32InputInjector>();
builder.Services.AddHostedService<SenderService>();

// Suporte a Windows Service (Fase 5): dotnet publish + sc.exe create
builder.Services.AddWindowsService(opts => opts.ServiceName = "SelfDesk.Sender");

await builder.Build().RunAsync();

static string GetEnv(string key, string def) =>
    Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : def;

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern bool SetProcessDpiAwarenessContext(nint value);

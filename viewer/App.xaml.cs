using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SelfDesk.Viewer.Audio;
using SelfDesk.Viewer.Decode;
using SelfDesk.Viewer.ViewModels;
using SelfDesk.Viewer.Views;

namespace SelfDesk.Viewer;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DotNetEnv.Env.Load();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<ViewerConfig>(cfg =>
                {
                    cfg.SharedSecret = GetEnv("SHARED_SECRET", string.Empty);
                    cfg.BrokerHost   = GetEnv("BROKER_HOST",   "localhost");
                    cfg.BrokerPort   = int.TryParse(GetEnv("BROKER_PORT", "7000"), out var p) ? p : 7000;
                    cfg.TlsCaPath    = GetEnv("TLS_CA_PATH",   string.Empty);
                });
                var encoderEnv = GetEnv("ENCODER", "jpeg").ToLowerInvariant();
                if (encoderEnv is "qsv" or "nvenc")
                    services.AddSingleton<IFrameDecoder, H264Decoder>();
                else
                    services.AddSingleton<IFrameDecoder, JpegFrameDecoder>();
                services.AddSingleton<IAudioPlayer, WasapiAudioPlayer>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddHostedService<ViewerService>();
            })
            .Build();

        // Cria a janela ANTES de iniciar o host para que ViewerService
        // receba a instância já existente via DI (sem race condition).
        var window = _host.Services.GetRequiredService<MainWindow>();
        await _host.StartAsync();
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private static string GetEnv(string key, string def) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : def;
}

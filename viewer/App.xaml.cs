using System.Windows;
using System.Windows.Threading;
using System.Reflection;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Viewer.Audio;
using SelfDesk.Viewer.Decode;
using SelfDesk.Viewer.ViewModels;
using SelfDesk.Viewer.Views;

namespace SelfDesk.Viewer;

public partial class App : Application
{
    private IHost? _host;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILoggerFactory>()?.CreateLogger("App")
            .LogError(e.Exception, "Unhandled UI-thread exception");
        // Recoverable faults (network, decode) are already handled by ViewerService's
        // own reconnect loop; keep the window alive rather than crashing on a UI-thread fault.
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILoggerFactory>()?.CreateLogger("App")
            .LogCritical(e.ExceptionObject as Exception, "Unhandled non-UI exception, terminating={IsTerminating}", e.IsTerminating);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Print version and exit early when requested (avoid building the host)
        if (e.Args is { Length: > 0 } && (Array.IndexOf(e.Args, "--version") >= 0 || Array.IndexOf(e.Args, "-v") >= 0))
        {
            var asm = Assembly.GetEntryAssembly();
            var version = asm != null
                ? FileVersionInfo.GetVersionInfo(asm.Location).ProductVersion ?? asm.GetName().Version?.ToString()
                : "0.0.0";
            // WinExe has no console by default; attach to parent terminal so output is visible
            AttachConsole(-1);
            Console.WriteLine($"SelfDesk Viewer v{version}");
            Console.Out.Flush();
            FreeConsole();
            Shutdown();
            return;
        }

        DotNetEnv.Env.Load();

        // S25: fail fast if SHARED_SECRET is missing or too short
        var startupSecret = Environment.GetEnvironmentVariable("SHARED_SECRET");
        if (string.IsNullOrEmpty(startupSecret) || startupSecret.Length < 32)
            throw new InvalidOperationException(
                "SHARED_SECRET missing or too short (minimum 32 chars). Run bootstrap.ps1 -Role receiver.");

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<ViewerConfig>(cfg =>
                {
                    cfg.SharedSecret = GetEnv("SHARED_SECRET", string.Empty);
                    cfg.BrokerHost = GetEnv("BROKER_HOST", "localhost");
                    cfg.BrokerPort = int.TryParse(GetEnv("BROKER_PORT", "7000"), out var p) ? p : 7000;
                    cfg.TlsCaPath = GetEnv("TLS_CA_PATH", string.Empty);
                    cfg.BenchmarkEnabled = bool.TryParse(GetEnv("BENCHMARK", "false"), out var benchmarkEnabled) && benchmarkEnabled;
                    cfg.BenchmarkLogPath = GetEnv("BENCHMARK_LOG_PATH", "benchmark-viewer.csv");
                });
                var encoderEnv = GetEnv("ENCODER", "jpeg").ToLowerInvariant();
                if (encoderEnv is "qsv" or "nvenc")
                    services.AddSingleton<IFrameDecoder, H264Decoder>();
                else
                    services.AddSingleton<IFrameDecoder, JpegFrameDecoder>();
                services.AddSingleton<IAudioPlayer, WasapiAudioPlayer>();
                services.AddSingleton(sp =>
                {
                    var c = sp.GetRequiredService<IOptions<ViewerConfig>>().Value;
                    return new SelfDesk.Viewer.Diagnostics.BenchmarkLog(c.BenchmarkEnabled, c.BenchmarkLogPath);
                });
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddHostedService<ViewerService>();
            })
            .Build();

        // Create the window BEFORE starting the host so ViewerService
        // receives the already-created instance via DI (no race condition).
        var window = _host.Services.GetRequiredService<MainWindow>();
        ApplyWindowSettings(window);
        window.Closing += (_, _) => SaveWindowSettings(window);

        var vm = _host.Services.GetRequiredService<MainWindowViewModel>();
        var caPath = GetEnv("TLS_CA_PATH", string.Empty);
        vm.CertWarning = SelfDesk.Viewer.Settings.CertExpiryChecker.CheckCaExpiry(caPath) ?? string.Empty;

        CheckForUpdateInBackground(vm);

        await _host.StartAsync();
        window.Show();
    }

    private static void CheckForUpdateInBackground(MainWindowViewModel vm)
    {
        var asm = Assembly.GetEntryAssembly();
        var currentVersion = asm != null
            ? FileVersionInfo.GetVersionInfo(asm.Location).ProductVersion ?? asm.GetName().Version?.ToString()
            : null;
        if (string.IsNullOrEmpty(currentVersion)) return;

        _ = Task.Run(async () =>
        {
            var latestTag = await SelfDesk.Viewer.Settings.UpdateChecker.CheckForUpdateAsync(currentVersion);
            if (latestTag is null) return;
            Current?.Dispatcher.Invoke(() => vm.UpdateAvailable = latestTag);
        });
    }

    private static void ApplyWindowSettings(Window window)
    {
        var saved = SelfDesk.Viewer.Settings.WindowSettingsService.Load();
        if (saved is null) return;

        if (saved.Maximized)
        {
            window.WindowState = WindowState.Maximized;
            return;
        }

        if (saved.Width > 0 && saved.Height > 0 &&
            SelfDesk.Viewer.Settings.WindowSettingsService.IsOnScreen(saved.Left, saved.Top, saved.Width, saved.Height))
        {
            window.Left   = saved.Left;
            window.Top    = saved.Top;
            window.Width  = saved.Width;
            window.Height = saved.Height;
        }
        // Off-screen or invalid — fall back to the XAML defaults (WindowStartupLocation not set, so
        // WPF centers/places it normally).
    }

    private static void SaveWindowSettings(Window window)
    {
        var maximized = window.WindowState == WindowState.Maximized;
        // RestoreBounds holds the pre-maximize size/position even while maximized —
        // save that instead of the maximized bounds so un-maximizing later is sane.
        var bounds = maximized ? window.RestoreBounds : new Rect(window.Left, window.Top, window.Width, window.Height);
        SelfDesk.Viewer.Settings.WindowSettingsService.Save(new SelfDesk.Viewer.Settings.WindowSettings(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized));
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

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SelfDesk.Viewer.Network;
using SelfDesk.Viewer.Protocol;
using SelfDesk.Viewer.ViewModels;

namespace SelfDesk.Viewer.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ViewerConfig        _cfg;
    private readonly ILogger<MainWindow> _log;
    private readonly CancellationTokenSource _cts = new();

    public MainWindow(MainWindowViewModel vm, IOptions<ViewerConfig> cfg, ILogger<MainWindow> log)
    {
        _vm  = vm;
        _cfg = cfg.Value;
        _log = log;

        InitializeComponent();
        DataContext = _vm;

        Loaded  += (_, _) => _ = ConnectAsync();
        Closing += (_, _) => _cts.Cancel();

        VideoImage.MouseMove        += OnVideoMouseMove;
        VideoImage.MouseDown        += OnVideoMouseDown;
        VideoImage.MouseUp          += OnVideoMouseUp;
        VideoImage.MouseWheel       += OnVideoMouseWheel;
        VideoImage.PreviewKeyDown   += OnVideoKeyDown;
        VideoImage.PreviewKeyUp     += OnVideoKeyUp;
    }

    private BrokerConnection? _conn;

    private async Task ConnectAsync()
    {
        _vm.ConnectionStatus = "Conectando...";
        try
        {
            _conn = new BrokerConnection(_cfg, _log);
            _conn.MessageReceived += OnMessageReceived;
            await _conn.ConnectAndAuthAsync(_cts.Token);
            _vm.IsConnected      = true;
            _vm.ConnectionStatus = "Conectado";
            UpdateStatusIndicator(connected: true);
            _ = _conn.StartHeartbeatAsync(_cts.Token);
            await _conn.RunReceiveLoopAsync(_cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "Erro de conexão");
            _vm.ConnectionStatus = "Erro de conexão";
            UpdateStatusIndicator(connected: false);
        }
    }

    private void OnMessageReceived(byte type, string peerId, ReadOnlyMemory<byte> payload)
    {
        switch (type)
        {
            case MessageType.SenderUp:
                var upJson  = System.Text.Json.JsonDocument.Parse(payload.ToArray());
                var upId    = upJson.RootElement.GetProperty("agentId").GetString() ?? peerId;
                Dispatcher.Invoke(() => _vm.AddSender(upId));
                break;

            case MessageType.SenderDown:
                var downJson = System.Text.Json.JsonDocument.Parse(payload.ToArray());
                var downId   = downJson.RootElement.GetProperty("agentId").GetString() ?? peerId;
                Dispatcher.Invoke(() => _vm.RemoveSender(downId));
                break;

            case MessageType.VideoFrame:
                // Phase 1 — decode and blit
                break;
        }
    }

    private void UpdateStatusIndicator(bool connected)
    {
        Dispatcher.Invoke(() =>
        {
            StatusIndicator.Fill = connected
                ? (Brush)FindResource("BrushSuccess")
                : (Brush)FindResource("BrushError");
        });
    }

    private (ushort x, ushort y) NormalizePosition(Point p)
    {
        var w = VideoImage.ActualWidth;
        var h = VideoImage.ActualHeight;
        if (w <= 0 || h <= 0) return (0, 0);
        var nx = (ushort)Math.Clamp((int)(p.X / w * 65535), 0, 65535);
        var ny = (ushort)Math.Clamp((int)(p.Y / h * 65535), 0, 65535);
        return (nx, ny);
    }

    private void SendInput(byte[] msg)
    {
        if (_conn is null || _vm.SelectedSender is null) return;
        _ = _conn.SendAsync(msg, _cts.Token);
    }

    private void OnVideoMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var (x, y) = NormalizePosition(e.GetPosition(VideoImage));
        SendInput(WireProtocol.BuildMouseMove(_vm.SelectedSender.AgentId, x, y));
    }

    private void OnVideoMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var btn = e.ChangedButton switch
        {
            MouseButton.Left   => InputEventKind.BtnLeft,
            MouseButton.Right  => InputEventKind.BtnRight,
            MouseButton.Middle => InputEventKind.BtnMiddle,
            _                  => InputEventKind.BtnLeft,
        };
        var (x, y) = NormalizePosition(e.GetPosition(VideoImage));
        SendInput(WireProtocol.BuildMouseButton(_vm.SelectedSender.AgentId, btn, InputEventKind.StateDown, x, y));
        VideoImage.Focus();
    }

    private void OnVideoMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var btn = e.ChangedButton switch
        {
            MouseButton.Left   => InputEventKind.BtnLeft,
            MouseButton.Right  => InputEventKind.BtnRight,
            MouseButton.Middle => InputEventKind.BtnMiddle,
            _                  => InputEventKind.BtnLeft,
        };
        var (x, y) = NormalizePosition(e.GetPosition(VideoImage));
        SendInput(WireProtocol.BuildMouseButton(_vm.SelectedSender.AgentId, btn, InputEventKind.StateUp, x, y));
    }

    private void OnVideoMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var (x, y) = NormalizePosition(e.GetPosition(VideoImage));
        var payload = new byte[6];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(payload, (short)(e.Delta / 120));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), x);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), y);
        SendInput(WireProtocol.BuildInputEvent(InputEventKind.MouseWheel, _vm.SelectedSender.AgentId, payload));
    }

    private void OnVideoKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var vk   = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var mods = GetMods();
        SendInput(WireProtocol.BuildKey(_vm.SelectedSender.AgentId, vk, InputEventKind.StateDown, mods));
        e.Handled = true;
    }

    private void OnVideoKeyUp(object sender, KeyEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var vk   = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var mods = GetMods();
        SendInput(WireProtocol.BuildKey(_vm.SelectedSender.AgentId, vk, InputEventKind.StateUp, mods));
        e.Handled = true;
    }

    private static byte GetMods()
    {
        byte mods = 0;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))   mods |= InputEventKind.ModShift;
        if (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl))    mods |= InputEventKind.ModCtrl;
        if (Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt))     mods |= InputEventKind.ModAlt;
        if (Keyboard.IsKeyDown(Key.LWin)      || Keyboard.IsKeyDown(Key.RWin))         mods |= InputEventKind.ModWin;
        return mods;
    }
}

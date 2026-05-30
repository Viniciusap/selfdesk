using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SelfDesk.Viewer.Protocol;
using SelfDesk.Viewer.ViewModels;
using SelfDesk.Viewer.WakeOnLan;

namespace SelfDesk.Viewer.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;

    public MainWindow(MainWindowViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;

        VideoImage.MouseMove  += OnVideoMouseMove;
        VideoImage.MouseDown  += OnVideoMouseDown;
        VideoImage.MouseUp    += OnVideoMouseUp;
        VideoImage.MouseWheel += OnVideoMouseWheel;
        VideoImage.Focusable  = true;
        VideoImage.PreviewKeyDown += OnVideoKeyDown;
        VideoImage.PreviewKeyUp   += OnVideoKeyUp;

        VideoBorder.DragOver += OnDragOver;
        VideoBorder.Drop     += OnDrop;
    }

    public event Action<byte[]>? InputSend;
    public event Action<string[]>? FileDropped;

    private (ushort x, ushort y) NormalizePosition(System.Windows.Point p)
    {
        var w = VideoImage.ActualWidth;
        var h = VideoImage.ActualHeight;
        if (w <= 0 || h <= 0) return (0, 0);
        var nx = (ushort)Math.Clamp((int)(p.X / w * 65535), 0, 65535);
        var ny = (ushort)Math.Clamp((int)(p.Y / h * 65535), 0, 65535);
        return (nx, ny);
    }

    private void Send(byte[] msg)
    {
        if (_vm.SelectedSender is null) return;
        InputSend?.Invoke(msg);
    }

    private void OnVideoMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var (x, y) = NormalizePosition(e.GetPosition(VideoImage));
        Send(WireProtocol.BuildMouseMove(_vm.SelectedSender.AgentId, x, y));
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
        Send(WireProtocol.BuildMouseButton(_vm.SelectedSender.AgentId, btn, InputEventKind.StateDown, x, y));
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
        Send(WireProtocol.BuildMouseButton(_vm.SelectedSender.AgentId, btn, InputEventKind.StateUp, x, y));
    }

    private void OnVideoMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var (x, y) = NormalizePosition(e.GetPosition(VideoImage));
        var payload = new byte[6];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(payload, (short)(e.Delta / 120));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), x);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), y);
        Send(WireProtocol.BuildInputEvent(InputEventKind.MouseWheel, _vm.SelectedSender.AgentId, payload));
    }

    private void OnVideoKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var vk   = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var mods = GetMods();
        Send(WireProtocol.BuildKey(_vm.SelectedSender.AgentId, vk, InputEventKind.StateDown, mods));
        e.Handled = true;
    }

    private void OnVideoKeyUp(object sender, KeyEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var vk   = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var mods = GetMods();
        Send(WireProtocol.BuildKey(_vm.SelectedSender.AgentId, vk, InputEventKind.StateUp, mods));
        e.Handled = true;
    }

    private async void OnWakeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SenderViewModel vm && !string.IsNullOrEmpty(vm.MacAddress))
        {
            try { await WakeOnLanService.SendMagicPacketAsync(vm.MacAddress); }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao enviar magic packet: {ex.Message}", "Wake-on-LAN",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _vm.SelectedSender is not null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            FileDropped?.Invoke(files);
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

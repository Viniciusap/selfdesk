using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ViewerWire = SelfDesk.Viewer.Protocol.WireProtocol;
using SelfDesk.Viewer.ViewModels;
using SelfDesk.Viewer.WakeOnLan;

namespace SelfDesk.Viewer.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private byte _modsTracked;
    private bool _isFullscreen;
    private WindowState _prevWindowState;

    public MainWindow(MainWindowViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;

        var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/src/Views/app.ico"));
        if (iconStream is not null)
            Icon = BitmapFrame.Create(iconStream.Stream);

        VideoImage.MouseMove  += OnVideoMouseMove;
        VideoImage.MouseDown  += OnVideoMouseDown;
        VideoImage.MouseUp    += OnVideoMouseUp;
        VideoImage.MouseWheel += OnVideoMouseWheel;

        // handledEventsToo=true: captura mesmo que um filho já tenha marcado handled
        AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnVideoKeyDown), handledEventsToo: true);
        AddHandler(UIElement.PreviewKeyUpEvent,   new KeyEventHandler(OnVideoKeyUp),   handledEventsToo: true);

        Deactivated += OnWindowDeactivated;

        VideoImage.DragOver += OnDragOver;
        VideoImage.Drop     += OnDrop;
    }

    public event Action<byte[]>? InputSend;
    public event Action<string[]>? FileDropped;

    private (ushort x, ushort y) NormalizePosition(System.Windows.Point p)
    {
        var controlW = VideoImage.ActualWidth;
        var controlH = VideoImage.ActualHeight;
        if (controlW <= 0 || controlH <= 0) return (0, 0);

        // Compensa letterbox do Stretch="Uniform"
        var frameW = (double)(_vm.VideoFrame?.PixelWidth  ?? (int)controlW);
        var frameH = (double)(_vm.VideoFrame?.PixelHeight ?? (int)controlH);
        var scale    = Math.Min(controlW / frameW, controlH / frameH);
        var offsetX  = (controlW - frameW * scale) / 2;
        var offsetY  = (controlH - frameH * scale) / 2;
        var renderedW = frameW * scale;
        var renderedH = frameH * scale;

        var rx = Math.Clamp((p.X - offsetX) / renderedW, 0, 1);
        var ry = Math.Clamp((p.Y - offsetY) / renderedH, 0, 1);
        return ((ushort)(rx * 65535), (ushort)(ry * 65535));
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
        Send(ViewerWire.BuildMouseMove(_vm.SelectedSender.AgentId, x, y));
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
        Send(ViewerWire.BuildMouseButton(_vm.SelectedSender.AgentId, btn, InputEventKind.StateDown, x, y));
        Focus();
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
        Send(ViewerWire.BuildMouseButton(_vm.SelectedSender.AgentId, btn, InputEventKind.StateUp, x, y));
    }

    private void OnVideoMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        var (x, y) = NormalizePosition(e.GetPosition(VideoImage));
        var payload = new byte[6];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(payload, (short)(e.Delta / 120));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), x);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), y);
        Send(ViewerWire.BuildInputEvent(InputEventKind.MouseWheel, _vm.SelectedSender.AgentId, payload));
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _prevWindowState = WindowState;
            // WPF bug: WindowStyle=None AFTER Maximized leaves the taskbar visible.
            // Reset to Normal first, then apply fullscreen.
            WindowState  = WindowState.Normal;
            WindowStyle  = WindowStyle.None;
            ResizeMode   = ResizeMode.NoResize;
            WindowState  = WindowState.Maximized;
            ContentGrid.ColumnDefinitions[0].Width = new GridLength(0);
            ContentGrid.ColumnDefinitions[1].Width = new GridLength(0);
            RootGrid.RowDefinitions[0].Height = new GridLength(0);
            RootGrid.RowDefinitions[2].Height = new GridLength(0);
            _isFullscreen = true;
        }
        else
        {
            WindowStyle  = WindowStyle.SingleBorderWindow;
            ResizeMode   = ResizeMode.CanResize;
            WindowState  = _prevWindowState;
            ContentGrid.ColumnDefinitions[0].Width = new GridLength(200);
            ContentGrid.ColumnDefinitions[1].Width = GridLength.Auto;
            RootGrid.RowDefinitions[0].Height = new GridLength(32);
            RootGrid.RowDefinitions[2].Height = new GridLength(24);
            _isFullscreen = false;
        }
    }

    private void OnVideoKeyDown(object sender, KeyEventArgs e)
    {
        // e.Key == Key.System for Alt/AltGr — actual key is in e.SystemKey
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.F11) { ToggleFullscreen(); e.Handled = true; return; }
        if (key == Key.Escape && _isFullscreen) { ToggleFullscreen(); e.Handled = true; return; }

        var vk  = (ushort)KeyInterop.VirtualKeyFromKey(key);
        if (_vm.SelectedSender is null) { e.Handled = true; return; }
        if (vk == 0) { e.Handled = true; return; }
        var modBit = GetModBit(vk);
        if (modBit != 0) _modsTracked |= modBit;
        Send(ViewerWire.BuildKey(_vm.SelectedSender.AgentId, vk, InputEventKind.StateDown, _modsTracked));
        e.Handled = true;
    }

    private void OnVideoKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk  = (ushort)KeyInterop.VirtualKeyFromKey(key);
        if (_vm.SelectedSender is null) { e.Handled = true; return; }
        if (vk == 0) { e.Handled = true; return; }
        // send with mods still including this modifier (modifier is still held at UP moment)
        Send(ViewerWire.BuildKey(_vm.SelectedSender.AgentId, vk, InputEventKind.StateUp, _modsTracked));
        var modBit = GetModBit(vk);
        if (modBit != 0) _modsTracked &= (byte)~modBit;
        e.Handled = true;
    }

    private static byte GetModBit(ushort vk) => vk switch
    {
        0xA0 or 0xA1 => InputEventKind.ModShift,
        0xA2 or 0xA3 => InputEventKind.ModCtrl,
        0xA4 or 0xA5 => InputEventKind.ModAlt,
        0x5B or 0x5C => InputEventKind.ModWin,
        _ => 0,
    };

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

    private static readonly ushort[] ModifierVKs = [0xA0,0xA1,0xA2,0xA3,0xA4,0xA5,0x5B,0x5C];

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_vm.SelectedSender is null) return;
        foreach (var vk in ModifierVKs)
            InputSend?.Invoke(ViewerWire.BuildKey(_vm.SelectedSender.AgentId, vk, InputEventKind.StateUp, 0));
        _modsTracked = 0;
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

}

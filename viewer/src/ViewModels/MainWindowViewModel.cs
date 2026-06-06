using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SelfDesk.Viewer.Audio;

namespace SelfDesk.Viewer.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IAudioPlayer _audioPlayer;

    public MainWindowViewModel(IAudioPlayer audioPlayer)
    {
        _audioPlayer = audioPlayer;
    }

    [ObservableProperty] private SenderViewModel? _selectedSender;
    [ObservableProperty] private string           _connectionStatus = "Disconnected";
    [ObservableProperty] private bool             _isConnected;
    [ObservableProperty] private WriteableBitmap? _videoFrame;
    [ObservableProperty] private int              _transferProgress = -1;
    [ObservableProperty] private string           _transferStatus   = string.Empty;
    [ObservableProperty] private bool             _isMuted          = false;

    partial void OnIsMutedChanged(bool value) => _audioPlayer.IsMuted = value;

    public ObservableCollection<SenderViewModel> Senders { get; } = [];

    public string StatusBarText =>
        TransferProgress >= 0
            ? TransferStatus
            : SelectedSender is null
                ? "Waiting for connection..."
                : $"{SelectedSender.AgentId} · {SelectedSender.ResolutionDisplay}" +
                  $" · {SelectedSender.Codec}" +
                  (SelectedSender.LastRttMs >= 0 ? $" · RTT {SelectedSender.LastRttMs}ms" : string.Empty);

    partial void OnSelectedSenderChanged(SenderViewModel? value) =>
        OnPropertyChanged(nameof(StatusBarText));

    public void NotifyStatusBarChanged() => OnPropertyChanged(nameof(StatusBarText));

    public void AddSender(string agentId, string? mac = null, string? version = null)
    {
        var existing = Senders.FirstOrDefault(s => s.AgentId == agentId);
        if (existing is not null)
        {
            existing.IsConnected = true;
            if (!string.IsNullOrEmpty(mac))     existing.MacAddress    = mac;
            if (!string.IsNullOrEmpty(version)) existing.SenderVersion = version;
            return;
        }
        var vm = new SenderViewModel
        {
            AgentId       = agentId,
            IsConnected   = true,
            MacAddress    = mac     ?? string.Empty,
            SenderVersion = version ?? string.Empty,
        };
        Senders.Add(vm);
        SelectedSender ??= vm;
    }

    public void RemoveSender(string agentId)
    {
        var vm = Senders.FirstOrDefault(s => s.AgentId == agentId);
        if (vm is null) return;
        vm.IsConnected = false;

        if (SelectedSender?.AgentId == agentId)
            SelectedSender = Senders.FirstOrDefault(s => s.IsConnected);
    }
}

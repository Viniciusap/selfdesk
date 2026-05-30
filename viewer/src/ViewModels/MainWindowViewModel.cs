using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SelfDesk.Viewer.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private SenderViewModel? _selectedSender;
    [ObservableProperty] private string           _connectionStatus = "Desconectado";
    [ObservableProperty] private bool             _isConnected;
    [ObservableProperty] private WriteableBitmap? _videoFrame;
    [ObservableProperty] private int              _transferProgress = -1;
    [ObservableProperty] private string           _transferStatus   = string.Empty;

    public ObservableCollection<SenderViewModel> Senders { get; } = [];

    public string StatusBarText =>
        TransferProgress >= 0
            ? TransferStatus
            : SelectedSender is null
                ? "Aguardando conexão..."
                : $"{SelectedSender.AgentId} · {SelectedSender.ResolutionDisplay}" +
                  $" · {SelectedSender.Codec}" +
                  (SelectedSender.LastRttMs >= 0 ? $" · RTT {SelectedSender.LastRttMs}ms" : string.Empty);

    partial void OnSelectedSenderChanged(SenderViewModel? value) =>
        OnPropertyChanged(nameof(StatusBarText));

    public void NotifyStatusBarChanged() => OnPropertyChanged(nameof(StatusBarText));

    public void AddSender(string agentId)
    {
        var existing = Senders.FirstOrDefault(s => s.AgentId == agentId);
        if (existing is not null)
        {
            existing.IsConnected = true;
            return;
        }
        var vm = new SenderViewModel { AgentId = agentId, IsConnected = true };
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

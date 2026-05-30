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

    public ObservableCollection<SenderViewModel> Senders { get; } = [];

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

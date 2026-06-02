using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SelfDesk.Viewer.ViewModels;

public sealed partial class SenderViewModel : ObservableObject
{
    [ObservableProperty] private string           _agentId = string.Empty;
    [ObservableProperty] private bool             _isConnected;
    [ObservableProperty] private long             _lastRttMs = -1;
    [ObservableProperty] private int              _frameWidth;
    [ObservableProperty] private int              _frameHeight;
    [ObservableProperty] private string           _codec = string.Empty;
    [ObservableProperty] private string           _macAddress = string.Empty;
    [ObservableProperty] private string           _senderVersion = string.Empty;
    [ObservableProperty] private MonitorViewModel? _selectedMonitor;

    public ObservableCollection<MonitorViewModel> Monitors { get; }

    public SenderViewModel()
    {
        Monitors = [];
        Monitors.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMultipleMonitors));
    }

    public bool CanWake => !IsConnected && !string.IsNullOrEmpty(MacAddress);

    public string DisplayName => AgentId;

    public string RttDisplay => LastRttMs >= 0 ? $"{LastRttMs} ms" : "--";

    public string ResolutionDisplay =>
        FrameWidth > 0 ? $"{FrameWidth}×{FrameHeight}" : string.Empty;

    public string VersionDisplay =>
        string.IsNullOrEmpty(SenderVersion) ? string.Empty : $"v{SenderVersion}";

    public bool HasMultipleMonitors => Monitors.Count > 1;
}

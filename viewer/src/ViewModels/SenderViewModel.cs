using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SelfDesk.Viewer.ViewModels;

public sealed partial class SenderViewModel : ObservableObject
{
    // Computed display properties are bound in the XAML; without NotifyPropertyChangedFor
    // the generated setters raise only the backing property, so the UI would show a stale
    // resolution/RTT/version and never re-evaluate the Wake button when a sender's state changes.
    [ObservableProperty][NotifyPropertyChangedFor(nameof(DisplayName))] private string _agentId = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanWake))] private bool _isConnected;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(RttDisplay))] private long _lastRttMs = -1;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ResolutionDisplay))] private int _frameWidth;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ResolutionDisplay))] private int _frameHeight;
    [ObservableProperty] private string           _codec = string.Empty;
    [ObservableProperty] private string           _codecMismatch = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanWake))] private string _macAddress = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(VersionDisplay))] private string _senderVersion = string.Empty;
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

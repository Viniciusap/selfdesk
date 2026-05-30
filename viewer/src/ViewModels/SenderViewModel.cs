using CommunityToolkit.Mvvm.ComponentModel;

namespace SelfDesk.Viewer.ViewModels;

public sealed partial class SenderViewModel : ObservableObject
{
    [ObservableProperty] private string _agentId = string.Empty;
    [ObservableProperty] private bool   _isConnected;
    [ObservableProperty] private long   _lastRttMs = -1;
    [ObservableProperty] private int    _frameWidth;
    [ObservableProperty] private int    _frameHeight;
    [ObservableProperty] private string _codec = string.Empty;

    public string DisplayName => AgentId;

    public string RttDisplay => LastRttMs >= 0 ? $"{LastRttMs} ms" : "--";

    public string ResolutionDisplay =>
        FrameWidth > 0 ? $"{FrameWidth}×{FrameHeight}" : string.Empty;
}

using System.Collections.Generic;
using System.ComponentModel;
using SelfDesk.Viewer.ViewModels;
using Xunit;

namespace SelfDesk.Viewer.Tests;

public class SenderViewModelTests
{
    private static List<string> TrackChanges(INotifyPropertyChanged vm)
    {
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
        return changed;
    }

    [Fact]
    public void IsConnected_RaisesCanWake()
    {
        var vm = new SenderViewModel { MacAddress = "AA:BB:CC:DD:EE:FF" };
        var changed = TrackChanges(vm);

        vm.IsConnected = true;

        Assert.Contains(nameof(SenderViewModel.CanWake), changed);
        Assert.False(vm.CanWake); // connected -> cannot wake
    }

    [Fact]
    public void MacAddress_RaisesCanWake()
    {
        var vm = new SenderViewModel();
        var changed = TrackChanges(vm);

        vm.MacAddress = "AA:BB:CC:DD:EE:FF";

        Assert.Contains(nameof(SenderViewModel.CanWake), changed);
        Assert.True(vm.CanWake); // disconnected + has MAC -> can wake
    }

    [Fact]
    public void LastRttMs_RaisesRttDisplay()
    {
        var vm = new SenderViewModel();
        var changed = TrackChanges(vm);

        vm.LastRttMs = 42;

        Assert.Contains(nameof(SenderViewModel.RttDisplay), changed);
        Assert.Equal("42 ms", vm.RttDisplay);
    }

    [Fact]
    public void FrameDimensions_RaiseResolutionDisplay()
    {
        var vm = new SenderViewModel();
        var changed = TrackChanges(vm);

        vm.FrameWidth = 1920;
        vm.FrameHeight = 1080;

        Assert.Contains(nameof(SenderViewModel.ResolutionDisplay), changed);
        Assert.Equal("1920×1080", vm.ResolutionDisplay);
    }

    [Fact]
    public void SenderVersion_RaisesVersionDisplay()
    {
        var vm = new SenderViewModel();
        var changed = TrackChanges(vm);

        vm.SenderVersion = "0.8.3";

        Assert.Contains(nameof(SenderViewModel.VersionDisplay), changed);
        Assert.Equal("v0.8.3", vm.VersionDisplay);
    }

    [Fact]
    public void AgentId_RaisesDisplayName()
    {
        var vm = new SenderViewModel();
        var changed = TrackChanges(vm);

        vm.AgentId = "laptop-01";

        Assert.Contains(nameof(SenderViewModel.DisplayName), changed);
        Assert.Equal("laptop-01", vm.DisplayName);
    }

    [Fact]
    public void HasMultipleMonitors_RaisedWhenMonitorsChange()
    {
        var vm = new SenderViewModel();
        var changed = TrackChanges(vm);

        vm.Monitors.Add(new MonitorViewModel());
        vm.Monitors.Add(new MonitorViewModel());

        Assert.Contains(nameof(SenderViewModel.HasMultipleMonitors), changed);
        Assert.True(vm.HasMultipleMonitors);
    }
}

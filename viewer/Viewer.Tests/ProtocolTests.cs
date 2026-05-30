using System.Buffers.Binary;
using System.Text;
using SelfDesk.Viewer.Protocol;
using Xunit;

namespace SelfDesk.Viewer.Tests;

public class WireProtocolTests
{
    [Fact]
    public void BuildEnvelope_VersionByte_Is01()
    {
        var msg = WireProtocol.BuildEnvelope(MessageType.Hello, "x", []);
        Assert.Equal(ProtocolVersion.Current, msg[0]);
    }

    [Fact]
    public void BuildEnvelope_Length_BigEndianUInt32()
    {
        var payload = new byte[256];
        var msg     = WireProtocol.BuildEnvelope(MessageType.VideoFrame, "", payload);
        var length  = BinaryPrimitives.ReadUInt32BigEndian(msg.AsSpan(ProtocolSizes.LengthOffset));
        Assert.Equal(256u, length);
    }

    [Fact]
    public void ParseHeader_ReturnsCorrectFields()
    {
        var payload = Encoding.UTF8.GetBytes("abc");
        var msg     = WireProtocol.BuildEnvelope(MessageType.SenderUp, "receiver", payload);
        var (type, peerId, length) = WireProtocol.ParseHeader(msg);
        Assert.Equal(MessageType.SenderUp, type);
        Assert.Equal("receiver", peerId);
        Assert.Equal((uint)payload.Length, length);
    }

    [Fact]
    public void BuildMouseMove_NormalizedCoordinates_BigEndian()
    {
        var msg     = WireProtocol.BuildMouseMove("laptop-01", 0xFFFF, 0x8000);
        var payload = msg.AsSpan(ProtocolSizes.HeaderSize);

        Assert.Equal(InputEventKind.MouseMove, payload[0]);
        var x = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
        var y = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
        Assert.Equal(0xFFFF, x);
        Assert.Equal(0x8000, y);
    }

    [Fact]
    public void BuildKey_Layout_BigEndian()
    {
        var msg     = WireProtocol.BuildKey("laptop-01", 0x0041, InputEventKind.StateDown, InputEventKind.ModShift);
        var payload = msg.AsSpan(ProtocolSizes.HeaderSize);

        Assert.Equal(InputEventKind.Key, payload[0]);
        var vk = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
        Assert.Equal(0x0041u, (uint)vk);
        Assert.Equal(InputEventKind.StateDown, payload[3]);
        Assert.Equal(InputEventKind.ModShift,  payload[4]);
    }

    [Fact]
    public void BuildMouseButton_HasCorrectKind()
    {
        var msg     = WireProtocol.BuildMouseButton("x", InputEventKind.BtnRight, InputEventKind.StateDown, 100, 200);
        var payload = msg.AsSpan(ProtocolSizes.HeaderSize);
        Assert.Equal(InputEventKind.MouseButton, payload[0]);
        Assert.Equal(InputEventKind.BtnRight,    payload[1]);
        Assert.Equal(InputEventKind.StateDown,   payload[2]);
    }
}

public class ViewModelTests
{
    [Fact]
    public void AddSender_AddsToCollection()
    {
        var vm = new ViewModels.MainWindowViewModel();
        vm.AddSender("laptop-01");
        Assert.Single(vm.Senders);
        Assert.Equal("laptop-01", vm.Senders[0].AgentId);
        Assert.True(vm.Senders[0].IsConnected);
    }

    [Fact]
    public void AddSender_FirstSender_BecomesSelected()
    {
        var vm = new ViewModels.MainWindowViewModel();
        vm.AddSender("laptop-01");
        Assert.Equal("laptop-01", vm.SelectedSender?.AgentId);
    }

    [Fact]
    public void AddSender_DuplicateId_MarksConnected()
    {
        var vm = new ViewModels.MainWindowViewModel();
        vm.AddSender("laptop-01");
        vm.RemoveSender("laptop-01");
        Assert.False(vm.Senders[0].IsConnected);
        vm.AddSender("laptop-01");
        Assert.True(vm.Senders[0].IsConnected);
        Assert.Single(vm.Senders);
    }

    [Fact]
    public void RemoveSender_MarksDisconnected()
    {
        var vm = new ViewModels.MainWindowViewModel();
        vm.AddSender("laptop-01");
        vm.RemoveSender("laptop-01");
        Assert.False(vm.Senders[0].IsConnected);
    }

    [Fact]
    public void RemoveSender_SelectedSender_ChangesToOtherConnected()
    {
        var vm = new ViewModels.MainWindowViewModel();
        vm.AddSender("laptop-01");
        vm.AddSender("laptop-02");
        vm.SelectedSender = vm.Senders[0];
        vm.RemoveSender("laptop-01");
        Assert.Equal("laptop-02", vm.SelectedSender?.AgentId);
    }
}

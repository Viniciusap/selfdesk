namespace SelfDesk.Sender.Inject;

public sealed class FakeInputInjector : IInputInjector
{
    public List<byte[]> Received { get; } = [];

    public void Inject(ReadOnlySpan<byte> inputEventPayload)
    {
        Received.Add(inputEventPayload.ToArray());
    }

    public int  MonitorIndex { get; set; }
    public void ReleaseAllModifiers() { }
}

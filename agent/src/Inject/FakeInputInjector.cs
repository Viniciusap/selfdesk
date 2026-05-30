namespace SelfDesk.Agent.Inject;

public sealed class FakeInputInjector : IInputInjector
{
    public List<byte[]> Received { get; } = [];

    public void Inject(ReadOnlySpan<byte> inputEventPayload)
    {
        Received.Add(inputEventPayload.ToArray());
    }
}

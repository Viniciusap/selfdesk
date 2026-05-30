namespace SelfDesk.Agent.Inject;

public interface IInputInjector
{
    void Inject(ReadOnlySpan<byte> inputEventPayload);
}

namespace SelfDesk.Sender.Inject;

public interface IInputInjector
{
    void Inject(ReadOnlySpan<byte> inputEventPayload);
    void ReleaseAllModifiers();
}

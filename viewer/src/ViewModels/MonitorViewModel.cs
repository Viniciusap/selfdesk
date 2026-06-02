namespace SelfDesk.Viewer.ViewModels;

public sealed class MonitorViewModel
{
    public int    Index     { get; init; }
    public string Name      { get; init; } = string.Empty;
    public bool   IsPrimary { get; init; }

    public override string ToString() => Name;
}

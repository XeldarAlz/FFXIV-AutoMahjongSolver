namespace Mahjong.Plugin.Game;

/// <summary>Null-object capture; Health is permanently Offline and the event never fires.</summary>
public sealed class InertDiscardCapture : IDiscardCapture
{
    public const string Name = "inert";

    public HookHealth Health => HookHealth.Offline;
    public string StrategyName => Name;
    public ulong TotalCaptured => 0;
    public int LastTileId => -1;

    public event Action<DiscardEvent>? DiscardObserved
    {
        add { _ = value; }
        remove { _ = value; }
    }

    public void Dispose() { }
}

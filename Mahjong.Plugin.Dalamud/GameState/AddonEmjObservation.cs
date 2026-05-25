namespace Mahjong.Plugin.Dalamud.GameState;

public sealed record AddonEmjObservation(
    bool Present,
    bool IsVisible,
    nint Address,
    ushort Width,
    ushort Height,
    long LastSeenUtcTicks,
    string? LastLifecycleEvent)
{
    public static AddonEmjObservation Empty { get; } =
        new(false, false, 0, 0, 0, 0, null);
}

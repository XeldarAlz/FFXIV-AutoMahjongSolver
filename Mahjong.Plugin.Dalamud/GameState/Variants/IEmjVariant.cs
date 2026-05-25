using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Engine;
using Mahjong.Plugin.Game.Variants;

namespace Mahjong.Plugin.Dalamud.GameState.Variants;

internal interface IEmjVariant
{
    string Name { get; }

    /// <summary>Tiebreaker when multiple probes match (empty-hand fingerprint is inconclusive).</summary>
    string PreferredAddonName { get; }

    LayoutProfile Profile { get; }

    /// <summary>Must not allocate, log, or mutate state — called every tick.</summary>
    unsafe bool Probe(AtkUnitBase* unit);

    unsafe StateSnapshot? TryBuildSnapshot(AtkUnitBase* unit, VariantReadContext ctx);
}

internal readonly record struct VariantReadContext(
    MeldTracker MeldTracker,
    InputEventLogger EventLogger);

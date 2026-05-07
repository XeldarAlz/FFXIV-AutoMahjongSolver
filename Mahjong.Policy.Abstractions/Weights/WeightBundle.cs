namespace Mahjong.Policy.Abstractions.Weights;

/// <summary>
/// Top-level container for every tunable parameter in the Policy stack. The
/// Tuner emits one of these as JSON; <see cref="IWeightProvider"/> loads it
/// at startup; every policy component reads from this single source of truth.
///
/// <see cref="SchemaVersion"/> bumps whenever the shape changes — providers
/// reject mismatched JSON rather than silently mis-applying old fields to a
/// new layout.
/// </summary>
public sealed record WeightBundle(
    DiscardWeights Discard,
    OpponentWeights Opponent,
    PlacementWeights Placement,
    RolloutWeights Rollout,
    int SchemaVersion = WeightBundle.CurrentSchemaVersion)
{
    public const int CurrentSchemaVersion = 1;

    public static WeightBundle Default { get; } = new(
        Discard: DiscardWeights.Default,
        Opponent: OpponentWeights.Default,
        Placement: PlacementWeights.Default,
        Rollout: RolloutWeights.Default);
}

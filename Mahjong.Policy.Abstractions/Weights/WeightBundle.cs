namespace Mahjong.Policy.Abstractions.Weights;

/// <summary>
/// Tunable parameters for the Policy stack. Bump <see cref="SchemaVersion"/> on shape changes —
/// providers reject mismatched JSON rather than silently mis-apply old fields.
/// </summary>
public sealed record WeightBundle(
    DiscardWeights Discard,
    OpponentWeights Opponent,
    PlacementWeights Placement,
    int SchemaVersion = WeightBundle.CurrentSchemaVersion)
{
    public const int CurrentSchemaVersion = 2;

    public static WeightBundle Default { get; } = new(
        Discard: DiscardWeights.Default,
        Opponent: OpponentWeights.Default,
        Placement: PlacementWeights.Default);
}

namespace Mahjong.Policy.Abstractions;

/// <summary>
/// One row of placement-aware multipliers — the discard scorer multiplies its
/// terms by these to bias play toward the right rank-context behavior:
/// rank 1 plays defensively, rank 4 chases, etc.
/// </summary>
public readonly record struct PlacementMultipliers(
    double Danger,
    double Ukeire,
    double HandValue)
{
    public static PlacementMultipliers Neutral => new(1.0, 1.0, 1.0);
}

/// <summary>
/// Resolves the (rank, last-hand, score-gap) state into placement multipliers.
/// Phase 4 will compose this into the discard scorer.
/// </summary>
public interface IPlacementPolicy
{
    PlacementMultipliers ComputeFor(StateSnapshot state);
}

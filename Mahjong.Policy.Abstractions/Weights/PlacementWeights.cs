namespace Mahjong.Policy.Abstractions.Weights;

/// <summary>
/// Per-rank multipliers used by the placement policy. Each entry maps to a
/// (DangerMultiplier, UkeireMultiplier, HandValueMultiplier) triple and the
/// scorer multiplies its terms by these to bias play toward the right
/// rank-context behavior:
///   * Rank 1 plays defensively (high danger, low ukeire/value).
///   * Rank 4 chases (low danger, high ukeire/value).
///   * Last-hand bonus amplifies the bias.
///
/// The "huge lead" override fires for rank 1 on the last hand with score gap
/// to 2nd > <see cref="Rank1HugeLeadGap"/>: full lock-down mode.
/// </summary>
public sealed record PlacementWeights(
    PlacementMultipliers Rank1,
    PlacementMultipliers Rank1LastHand,
    PlacementMultipliers Rank1HugeLead,
    PlacementMultipliers Rank2Or3,
    PlacementMultipliers Rank2Or3LastHand,
    PlacementMultipliers Rank4,
    PlacementMultipliers Rank4LastHand,
    int Rank1HugeLeadGap)
{
    public static PlacementWeights Default { get; } = new(
        Rank1: new(Danger: 1.3, Ukeire: 0.9, HandValue: 0.9),
        Rank1LastHand: new(Danger: 1.3, Ukeire: 0.9, HandValue: 0.9),  // baseline; huge-lead override below
        Rank1HugeLead: new(Danger: 2.0, Ukeire: 0.5, HandValue: 0.5),
        Rank2Or3: PlacementMultipliers.Neutral,
        Rank2Or3LastHand: new(Danger: 1.1, Ukeire: 1.0, HandValue: 1.2),
        Rank4: new(Danger: 0.7, Ukeire: 1.1, HandValue: 1.3),
        Rank4LastHand: new(Danger: 0.4, Ukeire: 1.3, HandValue: 1.8),
        Rank1HugeLeadGap: 8000);
}

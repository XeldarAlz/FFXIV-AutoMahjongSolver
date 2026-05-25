namespace Mahjong.Policy.Abstractions.Weights;

/// <summary>
/// <see cref="Rank1HugeLead"/> overrides last-hand rank-1 when score gap to 2nd exceeds
/// <see cref="Rank1HugeLeadGap"/> — full lockdown mode.
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
        Rank1LastHand: new(Danger: 1.3, Ukeire: 0.9, HandValue: 0.9),
        Rank1HugeLead: new(Danger: 2.0, Ukeire: 0.5, HandValue: 0.5),
        Rank2Or3: PlacementMultipliers.Neutral,
        Rank2Or3LastHand: new(Danger: 1.1, Ukeire: 1.0, HandValue: 1.2),
        Rank4: new(Danger: 0.7, Ukeire: 1.1, HandValue: 1.3),
        Rank4LastHand: new(Danger: 0.4, Ukeire: 1.3, HandValue: 1.8),
        Rank1HugeLeadGap: 8000);
}

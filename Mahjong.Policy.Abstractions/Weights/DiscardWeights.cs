namespace Mahjong.Policy.Abstractions.Weights;

/// <summary>
/// Weights for the discard scorer's linear combination.
///
/// Tuned via two passes (see comments on <see cref="Default"/>):
///   1) Evolutionary tuner found a coarse set beating hand-picked by +175 pts/hand.
///   2) Coordinate descent refined further; out-of-sample +227 vs ES at 2000 hands.
///
/// <see cref="Shanten"/> dominates so the policy still rejects shanten regressions
/// regardless of the other weights.
/// </summary>
public sealed record DiscardWeights(
    double Shanten,
    double UkeireKinds,
    double UkeireWeighted,
    double Dora,
    double Yakuhai,
    double IsolatedTerminal,
    double DealInCost,
    double YakuPotential = 60.0,
    double YakulessTenpaiPenalty = 120.0)
{
    public static DiscardWeights Default { get; } = new(
        Shanten: 100.0,
        UkeireKinds: 0.1954,
        UkeireWeighted: 0.5027,
        Dora: 36.9499,
        Yakuhai: 19.0784,
        IsolatedTerminal: 54.5092,
        DealInCost: 0.019662,
        YakuPotential: 60.0,
        YakulessTenpaiPenalty: 120.0);
}

namespace Mahjong.Policy.Abstractions.Weights;

/// <summary><see cref="Shanten"/> dominates so the policy always rejects shanten regressions.</summary>
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

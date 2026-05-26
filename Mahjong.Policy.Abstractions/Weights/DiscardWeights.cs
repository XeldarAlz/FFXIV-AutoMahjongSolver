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
        UkeireKinds: 0.1235,
        UkeireWeighted: 0.4068,
        Dora: 265.7649,
        Yakuhai: 163.2471,
        IsolatedTerminal: 840.2072,
        DealInCost: 0.0626,
        YakuPotential: 60.0,
        YakulessTenpaiPenalty: 120.0);
}

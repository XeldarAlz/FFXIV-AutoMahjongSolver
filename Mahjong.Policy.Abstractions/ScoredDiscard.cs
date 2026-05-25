namespace Mahjong.Policy.Abstractions;

public readonly record struct ScoredDiscard(
    Tile Discard,
    double Score,
    int ShantenAfter,
    int UkeireKinds,
    int UkeireWeighted,
    int DoraRetained,
    int YakuhaiRetained,
    double DealInCost,
    double YakuPotential = 0.0);

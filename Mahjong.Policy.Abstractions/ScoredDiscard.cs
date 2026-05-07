namespace Mahjong.Policy.Abstractions;

/// <summary>
/// One row from a <see cref="IDiscardPolicy"/>'s output: a candidate tile to
/// discard, the resulting score, and the diagnostic features that fed the
/// score (so the UI / debug overlay can explain why).
/// </summary>
public readonly record struct ScoredDiscard(
    Tile Discard,
    double Score,
    int ShantenAfter,
    int UkeireKinds,
    int UkeireWeighted,
    int DoraRetained,
    int YakuhaiRetained,
    double DealInCost);

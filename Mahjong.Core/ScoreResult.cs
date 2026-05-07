namespace Mahjong.Core;

public readonly record struct Payments(
    int DealerPay,       // 0 if the dealer is the winner, or if the win was ron
    int NonDealerPay,    // per non-dealer; 0 for ron
    int RonTotal,        // total paid by the ron target; 0 for tsumo
    int Total)           // sum across all payers
{ }

/// <summary>
/// The outcome of scoring one decomposition of a winning hand.
/// Defensive-copies the yaku list at construction.
/// </summary>
public sealed record ScoreResult(
    Decomposition Decomposition,
    IReadOnlyList<YakuHit> Yaku,
    int Han,
    int Fu,
    int BasePoints,
    Payments Payments,
    string TierName)    // "", "mangan", "haneman", "baiman", "sanbaiman", "yakuman"
{
    public IReadOnlyList<YakuHit> Yaku { get; init; } = [.. Yaku];

    public bool IsYakuman => Yaku.Any(y => y.IsYakuman);
}

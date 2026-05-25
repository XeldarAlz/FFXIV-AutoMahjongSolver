namespace Mahjong.Core;

/// <summary>DealerPay/NonDealerPay are 0 for ron; RonTotal is 0 for tsumo.</summary>
public readonly record struct Payments(
    int DealerPay,
    int NonDealerPay,
    int RonTotal,
    int Total)
{ }

/// <param name="TierName">"", "mangan", "haneman", "baiman", "sanbaiman", "yakuman".</param>
public sealed record ScoreResult(
    Decomposition Decomposition,
    IReadOnlyList<YakuHit> Yaku,
    int Han,
    int Fu,
    int BasePoints,
    Payments Payments,
    string TierName)
{
    public IReadOnlyList<YakuHit> Yaku { get; init; } = [.. Yaku];

    public bool IsYakuman => Yaku.Any(y => y.IsYakuman);
}

namespace Mahjong.Policy.Abstractions.Weights;

/// <summary>
/// Tenpai logistic: <c>p = sigmoid(intercept + a*discardCount + b*meldCount + c*turnsElapsed + d*lateTedashi)</c>.
/// Danger multipliers stack: suji × kabe × no-chance × kanchan-block.
/// </summary>
public sealed record OpponentWeights(
    double TenpaiIntercept,
    double TenpaiDiscardCount,
    double TenpaiMeldCount,
    double TenpaiTurnsElapsed,
    double TenpaiLateTedashi,
    double ExpectedHandValue,
    double ExpectedHandValuePerVisibleDora,
    double SujiDiscount,
    double KabeDiscount,
    double NoChanceDiscount,
    double KanchanBlockDiscount,
    double TenpaiBaseDealInRate)
{
    public static OpponentWeights Default { get; } = new(
        TenpaiIntercept: -2.0,
        TenpaiDiscardCount: 0.08,
        TenpaiMeldCount: 0.35,
        TenpaiTurnsElapsed: 0.02,
        TenpaiLateTedashi: 0.20,
        ExpectedHandValue: 4000.0,
        ExpectedHandValuePerVisibleDora: 1500.0,
        SujiDiscount: 0.6,
        KabeDiscount: 0.55,
        NoChanceDiscount: 0.35,
        KanchanBlockDiscount: 0.85,
        TenpaiBaseDealInRate: 0.125);
}

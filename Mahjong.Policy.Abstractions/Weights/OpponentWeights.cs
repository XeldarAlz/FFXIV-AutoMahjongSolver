namespace Mahjong.Policy.Abstractions.Weights;

/// <summary>
/// Tenpai logistic: <c>p = sigmoid(intercept + a*discardCount + b*meldCount + c*turnsElapsed)</c>.
/// <see cref="SujiDiscount"/> multiplies baseline deal-in for suji-blocked tiles.
/// <see cref="TenpaiBaseDealInRate"/> is P(specific kind is a wait) given tenpai.
/// </summary>
public sealed record OpponentWeights(
    double TenpaiIntercept,
    double TenpaiDiscardCount,
    double TenpaiMeldCount,
    double TenpaiTurnsElapsed,
    double ExpectedHandValue,
    double SujiDiscount,
    double TenpaiBaseDealInRate)
{
    public static OpponentWeights Default { get; } = new(
        TenpaiIntercept: -2.0,
        TenpaiDiscardCount: 0.08,
        TenpaiMeldCount: 0.35,
        TenpaiTurnsElapsed: 0.02,
        ExpectedHandValue: 4000.0,
        SujiDiscount: 0.6,
        TenpaiBaseDealInRate: 0.125);
}

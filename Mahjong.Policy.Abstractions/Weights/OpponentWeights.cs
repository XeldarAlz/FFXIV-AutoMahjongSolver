namespace Mahjong.Policy.Abstractions.Weights;

/// <summary>
/// Coefficients for the opponent model's belief updates. Previously hand-coded
/// in <c>OpponentModel.Update</c>; now tunable.
///
/// Tenpai logistic: <c>p = sigmoid(intercept + a*discardCount + b*meldCount + c*turnsElapsed)</c>.
///
/// Defaults match the pre-Phase-3 hand-tuned values; calibration against a Tenhou
/// corpus is the open work item (docs/ruleset.md gates).
/// </summary>
public sealed record OpponentWeights(
    double TenpaiIntercept,             // -2.0
    double TenpaiDiscardCount,          // +0.08
    double TenpaiMeldCount,             // +0.35
    double TenpaiTurnsElapsed,          // +0.02
    double ExpectedHandValue,           // 4000.0 (mangan-ish default)
    double SujiDiscount,                // 0.6 — multiplier on baseline deal-in for suji-blocked tiles
    double TenpaiBaseDealInRate)        // 0.125 — P(specific kind is a wait) given tenpai
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

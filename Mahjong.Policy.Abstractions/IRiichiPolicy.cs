namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Decides whether to declare riichi alongside a planned discard. The Decision's
/// payload is true iff we should declare; the <see cref="Reason"/> encodes the
/// trigger (shanten, ukeire, score gap, etc.) for UI display.
///
/// Riichi is conditional on the discard you intend to make — the same hand is a
/// declarable riichi on one cut and not on another. The discard policy picks the
/// cut first; this evaluator gates whether riichi rides on top.
/// </summary>
public interface IRiichiPolicy
{
    Decision<bool> Evaluate(StateSnapshot state, ScoredDiscard plannedDiscard);
}

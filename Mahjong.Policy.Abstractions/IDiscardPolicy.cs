namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Picks the best tile to discard from a 14-tile hand. Returns every legal
/// candidate scored and ordered, so callers can show alternatives in the UI
/// or thread the second-best into MCTS as a "close decision" check.
///
/// Phase 4 decomposes <c>EfficiencyPolicy.Choose</c> over this interface
/// (and <see cref="ICallPolicy"/>, <see cref="IRiichiPolicy"/>, etc.).
/// </summary>
public interface IDiscardPolicy
{
    ScoredDiscard[] Score(StateSnapshot state);
}

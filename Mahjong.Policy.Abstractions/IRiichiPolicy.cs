namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Riichi declarability depends on the planned cut — the discard policy chooses first,
/// this gates whether riichi rides on top.
/// </summary>
public interface IRiichiPolicy
{
    Decision<bool> Evaluate(StateSnapshot state, ScoredDiscard plannedDiscard);
}

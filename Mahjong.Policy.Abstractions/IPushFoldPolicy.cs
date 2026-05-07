namespace Mahjong.Policy.Abstractions;

public enum PushFoldStance
{
    Push,
    Fold,
    Neutral,
}

/// <summary>
/// Decides whether to push (play for the win) or fold (defend) on the current
/// turn given the opponent threat picture and the discard the discard-policy is
/// planning to make. The composing policy uses this to swap the discard pick
/// to a safer cut when the recommendation is fold.
/// </summary>
public interface IPushFoldPolicy
{
    Decision<PushFoldStance> Evaluate(
        StateSnapshot state,
        IOpponentModel opponentModel,
        ScoredDiscard plannedDiscard);
}

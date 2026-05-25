namespace Mahjong.Policy.Abstractions;

public enum PushFoldStance
{
    Push,
    Fold,
    Neutral,
}

public interface IPushFoldPolicy
{
    Decision<PushFoldStance> Evaluate(
        StateSnapshot state,
        IOpponentModel opponentModel,
        ScoredDiscard plannedDiscard);
}

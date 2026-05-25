namespace Mahjong.Policy.Abstractions;

/// <summary>Returns every legal discard scored and ordered for UI display.</summary>
public interface IDiscardPolicy
{
    ScoredDiscard[] Score(StateSnapshot state);
}

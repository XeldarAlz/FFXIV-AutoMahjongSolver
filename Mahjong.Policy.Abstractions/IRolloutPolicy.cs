namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Evaluates a leaf state during MCTS rollouts. Returns a scalar value where
/// higher = better. Implementations might use a fast heuristic, a learned
/// value function, or an actual short rollout — the search tree treats them
/// the same.
/// </summary>
public interface IRolloutPolicy
{
    /// <summary>Roll out from <paramref name="state"/> and return its value.</summary>
    double Run(StateSnapshot state, IOpponentModel opponentModel);
}

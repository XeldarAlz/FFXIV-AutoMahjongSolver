namespace Mahjong.Policy.Abstractions.Weights;

/// <summary>
/// Leaf-evaluation coefficients for MCTS rollout. The previous hardcoded
/// <c>-100 * shanten + 1.0 * ukeire</c> is now a tunable bundle.
///
/// Aligning these with <see cref="DiscardWeights"/> is a Phase 4 follow-up:
/// today MCTS rollout and the discard scorer optimize different objectives,
/// so MCTS can override the fast policy in a way the heuristic disagrees with.
/// </summary>
public sealed record RolloutWeights(
    double ShantenPenalty,        // -100 (negative — higher shanten = worse)
    double UkeireBonus)           // +1
{
    public static RolloutWeights Default { get; } = new(
        ShantenPenalty: -100.0,
        UkeireBonus: 1.0);
}

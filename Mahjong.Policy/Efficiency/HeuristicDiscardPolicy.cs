using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

/// <summary>
/// Heuristic <see cref="IDiscardPolicy"/> built on top of <see cref="DiscardScorer"/>.
/// Pulls discard weights from the injected <see cref="IWeightProvider"/> on
/// every call so weight reloads (Phase 5+) take effect without reconstruction.
///
/// Does not call <see cref="IOpponentModel.Update"/> itself — the composing
/// policy updates the model once per turn so every sub-policy reads consistent
/// state.
/// </summary>
public sealed class HeuristicDiscardPolicy : IDiscardPolicy
{
    private readonly IWeightProvider weightProvider;
    private readonly IOpponentModel opponentModel;
    private readonly IPlacementPolicy placementPolicy;

    public HeuristicDiscardPolicy(
        IWeightProvider weightProvider,
        IOpponentModel opponentModel,
        IPlacementPolicy placementPolicy)
    {
        ArgumentNullException.ThrowIfNull(weightProvider);
        ArgumentNullException.ThrowIfNull(opponentModel);
        ArgumentNullException.ThrowIfNull(placementPolicy);
        this.weightProvider = weightProvider;
        this.opponentModel = opponentModel;
        this.placementPolicy = placementPolicy;
    }

    public ScoredDiscard[] Score(StateSnapshot state)
    {
        var bundle = weightProvider.Current;
        var multipliers = placementPolicy.ComputeFor(state);
        return DiscardScorer.Score(
            state,
            weights: bundle.Discard,
            placement: multipliers,
            opponentModel: opponentModel);
    }
}

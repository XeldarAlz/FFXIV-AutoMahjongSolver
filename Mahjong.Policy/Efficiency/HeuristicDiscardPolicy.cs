using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

/// <summary>
/// Re-reads weights every call so live reloads land without reconstruction. Does NOT call
/// <see cref="IOpponentModel.Update"/> — the composing policy does that once per turn.
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

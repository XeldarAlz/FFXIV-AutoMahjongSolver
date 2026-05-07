using Mahjong.Engine;
using Mahjong.Policy.Efficiency;
using Mahjong.Policy.Opponents;

namespace Mahjong.Policy.Mcts;

/// <summary>
/// Information-Set MCTS policy. Falls through to <see cref="EfficiencyPolicy"/>
/// for non-close decisions and non-discard actions. On close discard decisions
/// (top-2 heuristic gap &lt; ε), runs <see cref="MctsSearch"/> for a bounded budget
/// and picks the action with highest mean rollout value.
///
/// Every stochastic component (<see cref="Determinizer"/>, <see cref="Rollout"/>)
/// shares the injected <see cref="IRandomSource"/> — pin a seed for reproducible
/// runs in tests and tuning, leave it open for live play.
/// </summary>
public sealed class IsmctsPolicy : IPolicy
{
    private readonly EfficiencyPolicy fastPolicy;
    private readonly OpponentModel opponentModel;
    private readonly MctsSearch search;
    private readonly double closeDecisionEpsilon;

    public IsmctsPolicy(
        IRandomSource? rng = null,
        EfficiencyPolicy? fastPolicy = null,
        OpponentModel? opponentModel = null,
        int determinizations = 8,
        int simsPerDeterminization = 50,
        int topK = 4,
        int rolloutDepth = 3,
        double closeDecisionEpsilon = 5.0)
    {
        var random = rng ?? new SeededRandomSource();
        this.fastPolicy = fastPolicy ?? new EfficiencyPolicy();
        this.opponentModel = opponentModel ?? new OpponentModel();
        search = new MctsSearch(
            new Determinizer(random),
            new Rollout(random, maxDepth: rolloutDepth),
            determinizations,
            simsPerDeterminization,
            topK);
        this.closeDecisionEpsilon = closeDecisionEpsilon;
    }

    public ActionChoice Choose(StateSnapshot state)
    {
        var fast = fastPolicy.Choose(state);

        if (fast.Kind != ActionKind.Discard)
            return fast;
        if (!IsCloseDecision(state))
            return fast;

        opponentModel.Update(state);
        var results = search.Run(state, opponentModel);
        if (results.Length == 0)
            return fast;

        var best = results[0];
        return ActionChoice.Discard(
            best.Discard,
            $"mcts pick={best.Discard} mean={best.MeanValue:F1} visits={best.Visits}");
    }

    private bool IsCloseDecision(StateSnapshot state)
    {
        if (!state.Legal.Can(ActionFlags.Discard))
            return false;

        opponentModel.Update(state);
        var scored = DiscardScorer.Score(state, opponentModel: opponentModel);
        if (scored.Length < 2)
            return false;

        double gap = scored[0].Score - scored[1].Score;
        return gap < closeDecisionEpsilon;
    }
}

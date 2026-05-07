using Mahjong.Engine;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Policy.Mcts;

/// <summary>
/// Simplified rollout: from a post-discard 13-tile state, simulate up to
/// <see cref="maxDepth"/> "pseudo turns" where our virtual self keeps drawing
/// and discarding using the fast heuristic.
///
/// With <see cref="simulateOpponents"/>, each step also consumes 3 additional
/// tiles from the unseen pool to represent opponent draws/discards.
///
/// Leaf evaluation: a linear blend of shanten and weighted ukeire whose
/// coefficients come from <see cref="RolloutWeights"/>. Higher = better;
/// positive infinity if the rollout reaches agari (shanten -1).
/// </summary>
public sealed class Rollout : IRolloutPolicy
{
    private readonly IRandomSource rng;
    private readonly RolloutWeights weights;
    private readonly int maxDepth;
    private readonly bool simulateOpponents;

    public Rollout(
        IRandomSource rng,
        RolloutWeights? weights = null,
        int maxDepth = 3,
        bool simulateOpponents = true)
    {
        ArgumentNullException.ThrowIfNull(rng);
        this.rng = rng;
        this.weights = weights ?? RolloutWeights.Default;
        this.maxDepth = maxDepth;
        this.simulateOpponents = simulateOpponents;
    }

    public double Run(StateSnapshot afterDiscard, IOpponentModel opponentModel)
    {
        var state = afterDiscard;
        var seen = BuildSeenCounts(state);

        for (int step = 0; step < maxDepth; step++)
        {
            var drawn = rng.DrawFromLive(LiveOf(seen));
            if (drawn is null)
                break;
            seen[drawn.Value.Id]++;

            // Add drawn tile to hand → 14 tiles.
            var handAfterDraw = new Tile[state.Hand.Count + 1];
            for (int i = 0; i < state.Hand.Count; i++)
                handAfterDraw[i] = state.Hand[i];
            handAfterDraw[^1] = drawn.Value;
            state = state with
            {
                Hand = handAfterDraw,
                Legal = new LegalActions(ActionFlags.Discard, [], [], [], []),
            };

            if (IsAgari(state))
                return double.PositiveInfinity;

            // Fast-policy pick: top discard from the scorer.
            var scored = DiscardScorer.Score(state, opponentModel: opponentModel);
            if (scored.Length == 0)
                break;
            var pick = scored[0].Discard;

            // Remove the chosen tile — back to 13 tiles.
            var handAfterDiscard = new Tile[state.Hand.Count - 1];
            int w = 0;
            bool removed = false;
            foreach (var t in state.Hand)
            {
                if (!removed && t.Id == pick.Id)
                {
                    removed = true;
                    continue;
                }
                handAfterDiscard[w++] = t;
            }
            state = state with { Hand = handAfterDiscard };

            // Each of 3 opponents draws+discards one tile, all from the same pool.
            if (simulateOpponents)
            {
                for (int opp = 0; opp < 3; opp++)
                {
                    var oppTile = rng.DrawFromLive(LiveOf(seen));
                    if (oppTile is null)
                        break;
                    seen[oppTile.Value.Id]++;
                }
            }
        }

        return EvaluateLeaf(state);
    }

    private static int[] BuildSeenCounts(StateSnapshot state)
    {
        var seen = new int[Tile.Count34];
        foreach (var t in state.Hand)
            seen[t.Id]++;
        foreach (var m in state.OurMelds)
            foreach (var t in m.Tiles)
                seen[t.Id]++;
        foreach (var seat in state.Seats)
        {
            foreach (var t in seat.Discards)
                seen[t.Id]++;
            foreach (var m in seat.Melds)
                foreach (var t in m.Tiles)
                    seen[t.Id]++;
        }
        foreach (var t in state.DoraIndicators)
            seen[t.Id]++;
        return seen;
    }

    private static int[] LiveOf(int[] seen)
    {
        var live = new int[seen.Length];
        for (int k = 0; k < seen.Length; k++)
            live[k] = Math.Max(0, Tile.CopiesPerKind - seen[k]);
        return live;
    }

    private static bool IsAgari(StateSnapshot state)
    {
        var counts = new int[Tile.Count34];
        foreach (var t in state.Hand)
            counts[t.Id]++;
        int shanten = ShantenCalculator.Standard(counts, state.OurMelds.Count);
        if (state.OurMelds.Count == 0)
        {
            shanten = Math.Min(shanten, ShantenCalculator.Chiitoitsu(counts));
            shanten = Math.Min(shanten, ShantenCalculator.Kokushi(counts));
        }
        return shanten < 0;
    }

    private double EvaluateLeaf(StateSnapshot state)
    {
        var counts = new int[Tile.Count34];
        foreach (var t in state.Hand)
            counts[t.Id]++;
        int shanten = ShantenCalculator.Standard(counts, state.OurMelds.Count);
        if (state.OurMelds.Count == 0)
        {
            shanten = Math.Min(shanten, ShantenCalculator.Chiitoitsu(counts));
            shanten = Math.Min(shanten, ShantenCalculator.Kokushi(counts));
        }

        if (shanten < 0)
            return double.PositiveInfinity;

        int ukeireWeight = ComputeUkeireWeight(state, counts, shanten);
        return weights.ShantenPenalty * shanten + weights.UkeireBonus * ukeireWeight;
    }

    private static int ComputeUkeireWeight(StateSnapshot state, int[] counts, int shanten)
    {
        if (shanten > 1 || state.Hand.Count < 13)
            return 0;

        int total = 0;
        for (int k = 0; k < Tile.Count34; k++)
        {
            if (counts[k] >= Tile.CopiesPerKind)
                continue;
            counts[k]++;
            int newShanten = ShantenCalculator.Standard(counts, state.OurMelds.Count);
            if (state.OurMelds.Count == 0)
            {
                newShanten = Math.Min(newShanten, ShantenCalculator.Chiitoitsu(counts));
                newShanten = Math.Min(newShanten, ShantenCalculator.Kokushi(counts));
            }
            counts[k]--;

            if (newShanten < shanten)
                total += Tile.CopiesPerKind - counts[k];
        }
        return total;
    }
}

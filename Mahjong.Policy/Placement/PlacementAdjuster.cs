using Mahjong.Engine;

namespace Mahjong.Policy.Placement;

/// <summary>
/// Placement-aware multiplier resolver. Mahjong ratings reward final rank,
/// not raw score — a bot that maximises EV(score) loses to one that maximises
/// EV(rank). Behavior shifts most aggressively on the last hand of the match,
/// but is active throughout the hanchan.
///
/// Maps the (rank, last-hand, score-gap) state to a triple of multipliers that
/// the discard scorer uses to bias its terms:
///   * Danger     — higher = fold harder (avoid deal-ins)
///   * Ukeire     — lower during fold mode, higher during push mode
///   * HandValue  — encourage big hands when we need big hands (4th seat, last-round)
///
/// Concrete values come from <see cref="PlacementWeights"/> (tunable),
/// previously hard-coded into the switch body.
/// </summary>
public sealed class PlacementAdjuster : IPlacementPolicy
{
    private readonly PlacementWeights weights;

    public PlacementAdjuster() : this(PlacementWeights.Default) { }

    public PlacementAdjuster(PlacementWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        this.weights = weights;
    }

    public PlacementMultipliers ComputeFor(StateSnapshot state)
    {
        var rank = RankOf(state, state.OurSeat);
        bool lastHand = IsLastHand(state);
        int scoreGapBelow = ScoreGapToLowerRank(state, rank);

        return rank switch
        {
            1 => lastHand && scoreGapBelow > weights.Rank1HugeLeadGap
                    ? weights.Rank1HugeLead
                    : (lastHand ? weights.Rank1LastHand : weights.Rank1),

            2 or 3 => lastHand ? weights.Rank2Or3LastHand : weights.Rank2Or3,

            4 => lastHand ? weights.Rank4LastHand : weights.Rank4,

            _ => PlacementMultipliers.Neutral,
        };
    }

    /// <summary>1-indexed rank (1 = highest score) of the given seat in this snapshot.</summary>
    public static int RankOf(StateSnapshot state, int seat)
    {
        int ourScore = state.Scores[seat];
        int rank = 1;
        for (int i = 0; i < state.Scores.Count; i++)
            if (i != seat && state.Scores[i] > ourScore)
                rank++;
        return rank;
    }

    private static int ScoreGapToLowerRank(StateSnapshot state, int ourRank)
    {
        if (ourRank >= 4)
            return int.MaxValue;
        int ourScore = state.Scores[state.OurSeat];
        int minGap = int.MaxValue;
        foreach (var s in state.Scores)
            if (s < ourScore && ourScore - s < minGap)
                minGap = ourScore - s;
        return minGap;
    }

    /// <summary>
    /// Heuristic: last hand of the hanchan. We don't have a reliable "final hand"
    /// field yet (M4 owes round + honba extraction); approximate by wall-remaining
    /// ≤ 10 combined with round wind = South.
    /// </summary>
    public static bool IsLastHand(StateSnapshot state)
        => state.RoundWind == 1 && state.WallRemaining <= 10;
}

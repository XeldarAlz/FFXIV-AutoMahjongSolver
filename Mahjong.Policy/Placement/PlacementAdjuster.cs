using Mahjong.Engine;

namespace Mahjong.Policy.Placement;

/// <summary>Mahjong rewards rank, not raw score — bias the discard scorer accordingly.</summary>
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

    /// <summary>1-indexed rank (1 = highest score).</summary>
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

    /// <summary>Approximate; we don't yet have a reliable final-hand flag.</summary>
    public static bool IsLastHand(StateSnapshot state)
        => state.RoundWind == 1 && state.WallRemaining <= 10;
}

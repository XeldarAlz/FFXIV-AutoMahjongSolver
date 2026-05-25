using Mahjong.Policy.Placement;

namespace Mahjong.Policy.Tests;

public class PlacementAdjusterTests
{
    private static readonly PlacementAdjuster Adjuster = new();

    private static StateSnapshot State(int[] scores, int wall = 40, int roundWind = 0, int ourSeat = 0)
    {
        var seats = new SeatView[4];
        for (int i = 0; i < 4; i++)
            seats[i] = new SeatView([], [], [], false, -1, false, false);
        return StateSnapshot.Empty with
        {
            Scores = scores,
            WallRemaining = wall,
            RoundWind = roundWind,
            OurSeat = ourSeat,
            Seats = seats,
        };
    }

    [Fact]
    public void RankOf_reports_1_for_highest_score()
    {
        var s = State([40000, 25000, 20000, 15000]);
        Assert.Equal(1, PlacementAdjuster.RankOf(s, 0));
        Assert.Equal(2, PlacementAdjuster.RankOf(s, 1));
        Assert.Equal(3, PlacementAdjuster.RankOf(s, 2));
        Assert.Equal(4, PlacementAdjuster.RankOf(s, 3));
    }

    [Fact]
    public void Rank1_mid_hanchan_leans_conservative()
    {
        var s = State([40000, 25000, 20000, 15000], wall: 50, roundWind: 0);
        var w = Adjuster.ComputeFor(s);
        Assert.True(w.Danger > 1.0);
        Assert.True(w.Ukeire <= 1.0);
    }

    [Fact]
    public void Rank4_mid_hanchan_leans_aggressive()
    {
        var s = State([15000, 25000, 30000, 30000], wall: 50, roundWind: 0);
        var w = Adjuster.ComputeFor(s);
        Assert.True(w.Danger < 1.0);
        Assert.True(w.Ukeire >= 1.0);
        Assert.True(w.HandValue > 1.0);
    }

    [Fact]
    public void Last_hand_rank4_goes_max_aggressive()
    {
        var s = State([15000, 25000, 30000, 30000], wall: 5, roundWind: 1);
        var w = Adjuster.ComputeFor(s);
        Assert.True(w.Danger < 0.5);
        Assert.True(w.HandValue > 1.5);
    }

    [Fact]
    public void Last_hand_rank1_locked_first_goes_max_conservative()
    {
        var s = State([50000, 20000, 15000, 15000], wall: 5, roundWind: 1);
        var w = Adjuster.ComputeFor(s);
        Assert.True(w.Danger >= 2.0);
        Assert.True(w.Ukeire < 1.0);
    }

    [Fact]
    public void Middle_ranks_stay_near_neutral_mid_hanchan()
    {
        // Seat 0 has 25000, which is rank 2 (seat 1 has 28000, others ≤ 25000).
        var s = State([25000, 28000, 25000, 22000], wall: 40, roundWind: 0, ourSeat: 0);
        var w = Adjuster.ComputeFor(s);
        Assert.Equal(PlacementMultipliers.Neutral, w);
    }
}

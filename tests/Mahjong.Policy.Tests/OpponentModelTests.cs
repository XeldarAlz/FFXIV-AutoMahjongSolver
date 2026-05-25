using Mahjong.Engine;
using Mahjong.Policy.Opponents;
using Xunit;

namespace Mahjong.Policy.Tests;

public class OpponentModelTests
{
    private static StateSnapshot BaseState(
        int wallRemaining = 40,
        int ourSeat = 0,
        SeatView[]? seats = null)
    {
        seats ??= new[]
        {
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        };
        return StateSnapshot.Empty with
        {
            OurSeat = ourSeat,
            WallRemaining = wallRemaining,
            Seats = seats,
        };
    }

    [Fact]
    public void Riichi_seat_gets_tenpai_prob_one()
    {
        var seats = new[]
        {
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], true, 5, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        };
        var s = BaseState(seats: seats);
        var m = new OpponentModel();
        m.Update(s);
        Assert.Equal(1.0, m.TenpaiProb[0]);
    }

    [Fact]
    public void Early_game_low_tenpai_prob()
    {
        var s = BaseState(wallRemaining: 68);
        var m = new OpponentModel();
        m.Update(s);
        foreach (var p in m.TenpaiProb)
            Assert.True(p < 0.3, $"early tenpai prob should be low, got {p}");
    }

    [Fact]
    public void Late_game_higher_tenpai_prob()
    {
        var s = BaseState(wallRemaining: 10);
        var m = new OpponentModel();
        m.Update(s);
        foreach (var p in m.TenpaiProb)
            Assert.True(p > 0.2, $"late-game tenpai prob should be non-trivial, got {p}");
    }

    [Fact]
    public void Opponent_discarded_tile_has_zero_danger_genbutsu()
    {
        var discards = new[] { Tile.FromId(5) };
        var seats = new[]
        {
            new SeatView([], [], [], false, -1, false, false),
            new SeatView(discards, [false], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        };
        var s = BaseState(seats: seats);
        var m = new OpponentModel();
        m.Update(s);
        Assert.Equal(0.0, m.DangerMap[0][5]);
    }

    [Fact]
    public void Kabe_zero_live_tiles_means_zero_danger()
    {
        var hand = new[]
        {
            Tile.FromId(31), Tile.FromId(31), Tile.FromId(31), Tile.FromId(31),
        };
        var s = BaseState() with { Hand = hand };
        var m = new OpponentModel();
        m.Update(s);
        for (int opp = 0; opp < 3; opp++)
            Assert.Equal(0.0, m.DangerMap[opp][31]);
    }

    [Fact]
    public void HandMarginal_zero_for_tiles_the_opponent_discarded()
    {
        var discards = new[] { Tile.FromId(0) };
        var seats = new[]
        {
            new SeatView([], [], [], false, -1, false, false),
            new SeatView(discards, [false], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        };
        var s = BaseState(seats: seats);
        var m = new OpponentModel();
        m.Update(s);
        Assert.Equal(0.0, m.HandMarginal[0][0]);
    }

    [Fact]
    public void ExpectedDealInCost_is_weighted_sum()
    {
        var s = BaseState();
        var m = new OpponentModel();
        m.Update(s);
        double cost = m.ExpectedDealInCost(0);
        Assert.True(cost >= 0.0);
        Assert.True(cost <= 3 * 6000);
    }

    [Fact]
    public void Kabe_discounts_neighbor_danger()
    {
        // 4m fully visible in our hand → 3m and 5m get the KabeDiscount.
        var hand = new[]
        {
            Tile.FromId(3), Tile.FromId(3), Tile.FromId(3), Tile.FromId(3),
        };
        var seats = new[]
        {
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false, DiscardCount: 6),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        };
        var bare = BaseState(seats: seats);
        var withKabe = bare with { Hand = hand };

        var mBare = new OpponentModel();
        mBare.Update(bare);
        var mKabe = new OpponentModel();
        mKabe.Update(withKabe);

        Assert.True(mKabe.DangerMap[0][2] < mBare.DangerMap[0][2],
            $"kabe at 4m should discount 3m: kabe={mKabe.DangerMap[0][2]} bare={mBare.DangerMap[0][2]}");
        Assert.True(mKabe.DangerMap[0][4] < mBare.DangerMap[0][4],
            $"kabe at 4m should discount 5m: kabe={mKabe.DangerMap[0][4]} bare={mBare.DangerMap[0][4]}");
    }

    [Fact]
    public void NoChance_zeroes_ryanmen_so_middle_tile_safer()
    {
        // Both 1m and 7m fully visible → 5m has no ryanmen route (needs 3m+4m or 6m+7m;
        // 7m is dead). Both 1m (low side dead via boundary) and 7m → no-chance triggers.
        // Better: 3m and 7m fully visible → ryanmen on 5m needs (3m,4m) or (6m,7m), both dead.
        var hand = new[]
        {
            Tile.FromId(2), Tile.FromId(2), Tile.FromId(2), Tile.FromId(2),
            Tile.FromId(6), Tile.FromId(6), Tile.FromId(6), Tile.FromId(6),
        };
        var seats = new[]
        {
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false, DiscardCount: 6),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        };
        var s = BaseState(seats: seats) with { Hand = hand };
        var m = new OpponentModel();
        m.Update(s);
        var bare = new OpponentModel();
        bare.Update(BaseState(seats: seats));
        Assert.True(m.DangerMap[0][4] < bare.DangerMap[0][4] * 0.5,
            $"no-chance 5m should be discounted significantly: noChance={m.DangerMap[0][4]} bare={bare.DangerMap[0][4]}");
    }

    [Fact]
    public void LateTedashi_raises_tenpai_prob()
    {
        var discards = new Tile[10];
        for (int i = 0; i < discards.Length; i++) discards[i] = Tile.FromId(i % 27);

        // All tsumogiri.
        var quietTedashi = new bool[10];
        var quietSeats = new[]
        {
            new SeatView([], [], [], false, -1, false, false),
            new SeatView(discards, quietTedashi, [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        };

        // Tsumogiri until turn 6, then tedashi.
        var activeTedashi = new bool[10];
        for (int i = 6; i < 10; i++) activeTedashi[i] = true;
        var activeSeats = new[]
        {
            new SeatView([], [], [], false, -1, false, false),
            new SeatView(discards, activeTedashi, [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        };

        var mQuiet = new OpponentModel();
        mQuiet.Update(BaseState(seats: quietSeats));
        var mActive = new OpponentModel();
        mActive.Update(BaseState(seats: activeSeats));
        Assert.True(mActive.TenpaiProb[0] > mQuiet.TenpaiProb[0],
            $"late tedashi should raise tenpai prob: active={mActive.TenpaiProb[0]} quiet={mQuiet.TenpaiProb[0]}");
    }

    [Fact]
    public void Visible_dora_in_meld_raises_opponents_expected_value()
    {
        // Indicator 2m → dora 3m. Opponent has pon of 3m.
        var doraInd = new[] { Tile.FromId(1) };
        var meld = Meld.Pon(Tile.FromId(2), Tile.FromId(2), fromSeat: 0);
        var seats = new[]
        {
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], new[] { meld }, false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        };
        var s = BaseState(seats: seats) with { DoraIndicators = doraInd };
        var m = new OpponentModel();
        m.Update(s);
        Assert.True(m.ExpectedHandValue[0] > OpponentWeights.Default.ExpectedHandValue,
            $"3 visible dora in meld should bump expected value, got {m.ExpectedHandValue[0]}");
    }
}

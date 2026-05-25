using Mahjong.Engine;
using Mahjong.Policy.Efficiency;
using Xunit;

namespace Mahjong.Policy.Tests;

public class YakuPotentialTests
{
    private static StateSnapshot SeatKnownState(int seat = 0, int round = 0) =>
        StateSnapshot.Empty with { OurSeat = seat, RoundWind = round, SeatInfoKnown = true };

    [Fact]
    public void Empty_hand_scores_zero()
    {
        var hand = new Hand(new int[Tile.Count34]);
        Assert.Equal(0.0, YakuPotential.Score(hand, null, StateSnapshot.Empty));
    }

    [Fact]
    public void All_simples_hand_scores_at_least_half_han()
    {
        var hand = Hand.FromNotation("234m567m234p567p8m");
        Assert.Equal(13, hand.ClosedTileCount);
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.True(score >= 0.5, $"expected score >= 0.5 (got {score:F3})");
    }

    [Fact]
    public void Dragon_triplet_locks_yakuhai()
    {
        var hand = Hand.FromNotation("555z123m456p789s2p");
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        // Dragon triplet (1 han) + closed riichi cert (~0.9 han) against TargetHan=4.
        Assert.InRange(score, 0.4, 0.6);
    }

    [Fact]
    public void Seat_wind_pair_only_counts_when_seat_info_known()
    {
        var hand = Hand.FromNotation("11z123m456p789s5p2p");
        var unknown = StateSnapshot.Empty;
        var known = SeatKnownState(seat: 0, round: 0);

        double withoutInfo = YakuPotential.Score(hand, null, unknown);
        double withInfo = YakuPotential.Score(hand, null, known);
        Assert.True(withInfo > withoutInfo,
            $"expected the seat-wind pair to score higher when seat info is known (got {withoutInfo:F3} vs {withInfo:F3})");
    }

    [Fact]
    public void Pure_one_suit_hand_scores_high_for_honitsu()
    {
        var hand = Hand.FromNotation("1234567899m11m9m");
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Six_pairs_close_to_chiitoitsu()
    {
        var hand = Hand.FromNotation("11m22m33p44p55s66s7z");
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Removed_tile_arg_simulates_post_discard_view()
    {
        var hand = Hand.FromNotation("11m22m33p44p55s66s7z3m");
        double withoutRemoval = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        double afterDiscard3m = YakuPotential.Score(hand, Tile.FromId(2), StateSnapshot.Empty);
        Assert.True(afterDiscard3m >= withoutRemoval);
    }

    [Fact]
    public void Score_is_clamped_to_one()
    {
        var hand = Hand.FromNotation("111z2345m6m7m8m99m");
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Discard_scorer_surfaces_yaku_potential_field()
    {
        var s = Snapshots.Closed14("222m555p888s23456p");
        var scored = DiscardScorer.Score(s);
        Assert.NotEmpty(scored);
        Assert.Contains(scored, sd => sd.YakuPotential > 0);
    }

    [Fact]
    public void Open_pons_lift_score_via_toitoi_route()
    {
        // Hold the open-state constant on both sides so the comparison isolates toitoi value
        // from the closed→open swing. A neutral chi on both hands strips the closed-riichi
        // bonus uniformly; pons add the toitoi route only on the right side.
        int[] closed = Hand.FromNotation("147m369p5s").CloneCounts();
        var pon2m = Meld.Pon(Tile.FromId(1), Tile.FromId(1), fromSeat: 1);
        var pon5p = Meld.Pon(Tile.FromId(13), Tile.FromId(13), fromSeat: 2);
        var chi456s = Meld.Chi(Tile.FromId(21), Tile.FromId(23), fromSeat: 3);

        double withChiOnly = YakuPotential.Score(
            new Hand(closed, [chi456s]), null, StateSnapshot.Empty);
        double withPons = YakuPotential.Score(
            new Hand(closed, [pon2m, pon5p]), null, StateSnapshot.Empty);

        Assert.True(withPons > withChiOnly + 0.1,
            $"two open pons should add toitoi-route value (chi-only={withChiOnly:F3}, with-pons={withPons:F3})");
    }

    [Fact]
    public void Open_chi_zeroes_the_toitoi_route()
    {
        int[] closed = Hand.FromNotation("147m369p5s").CloneCounts();
        var pon2m = Meld.Pon(Tile.FromId(1), Tile.FromId(1), fromSeat: 1);
        var pon5p = Meld.Pon(Tile.FromId(13), Tile.FromId(13), fromSeat: 2);
        var chi234p = Meld.Chi(Tile.FromId(10), Tile.FromId(12), fromSeat: 3);

        double withTwoPons = YakuPotential.Score(
            new Hand(closed, [pon2m, pon5p]), null, StateSnapshot.Empty);
        double withPonAndChi = YakuPotential.Score(
            new Hand(closed, [pon2m, chi234p]), null, StateSnapshot.Empty);

        Assert.True(withTwoPons > withPonAndChi,
            $"replacing a pon with a chi should kill toitoi (pons={withTwoPons:F3}, mixed={withPonAndChi:F3})");
    }

    [Fact]
    public void Three_closed_triplets_drive_toitoi_above_half()
    {
        var hand = Hand.FromNotation("111m9m333p1p555s7s7z");
        Assert.Equal(13, hand.ClosedTileCount);
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.True(score >= 0.7,
            $"three closed triplets should drive toitoi to >=0.7 final score (got {score:F3})");
    }

    [Fact]
    public void Two_closed_pairs_alone_do_not_signal_toitoi()
    {
        // Pins the toitoi gate (needs >=1 locked or >=3 pairs). The hand still earns chiitoitsu
        // progress + closed-riichi cert + a sliver of chanta, so the assertion only fences out
        // the toitoi-driven swing. A locked triplet hand would clear ~0.85 once toitoi fires.
        var hand = Hand.FromNotation("11m99p23456m78s1z3z");
        Assert.Equal(13, hand.ClosedTileCount);
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.True(score < 0.75,
            $"two pairs with no locked triplet should stay below the toitoi-fired ceiling (got {score:F3})");
    }

    [Fact]
    public void Three_suit_parallel_runs_saturate_sanshoku()
    {
        // Full sanshoku-doujun (2 han closed) + closed riichi cert (~0.9 han) ≈ 2.9 han / 4 ≈ 0.73.
        // We assert a wide floor instead of saturation since TargetHan=4 no longer caps at 2 han.
        var hand = Hand.FromNotation("123m5m7p123p123s5s8s");
        Assert.Equal(13, hand.ClosedTileCount);
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.True(score >= 0.7, $"sanshoku-doujun + riichi cert should clear 0.7 (got {score:F3})");
    }

    [Fact]
    public void Breaking_third_suit_drops_sanshoku_contribution()
    {
        // Drop tightened from +0.25 to +0.1: TargetHan=4 compresses the same ~1.3-han sanshoku
        // delta into roughly half the normalised swing it used to produce.
        var full = Hand.FromNotation("123m5m7p123p123s5s8s");
        var broken = Hand.FromNotation("123m5m7p123p23s5s8s4z");

        double scoreFull = YakuPotential.Score(full, null, StateSnapshot.Empty);
        double scoreBroken = YakuPotential.Score(broken, null, StateSnapshot.Empty);
        Assert.True(scoreFull > scoreBroken + 0.1,
            $"breaking the third suit should drop sanshoku materially " +
            $"(full={scoreFull:F3}, broken={scoreBroken:F3})");
    }

    [Fact]
    public void Open_chi_locks_its_suit_in_sanshoku_readiness()
    {
        var chi234m = Meld.Chi(Tile.FromId(1), Tile.FromId(3), fromSeat: 3);
        int[] withParallel = Hand.FromNotation("234p234s1m9m9p9s").CloneCounts();
        int[] noSouAtAll = Hand.FromNotation("2p3p5p7p9p1z3z5z6z7z").CloneCounts();

        double scoreWithParallel = YakuPotential.Score(
            new Hand(withParallel, [chi234m]), null, StateSnapshot.Empty);
        double scoreControl = YakuPotential.Score(
            new Hand(noSouAtAll, [chi234m]), null, StateSnapshot.Empty);

        Assert.True(scoreWithParallel > scoreControl + 0.04,
            $"chi locking a fully-supported sanshoku offset should lift score " +
            $"(parallel={scoreWithParallel:F3}, control={scoreControl:F3})");
    }

    [Fact]
    public void Full_ittsu_in_one_suit_saturates_score()
    {
        // Closed ittsu (2 han) + riichi cert (~0.9 han) + junchan-route sliver lands around
        // 0.85+. No longer saturates with TargetHan=4 since a single yaku alone can't.
        var hand = Hand.FromNotation("123456789m1p5p1s9s");
        Assert.Equal(13, hand.ClosedTileCount);
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.True(score >= 0.8, $"full closed ittsu + riichi should clear 0.8 (got {score:F3})");
    }

    [Fact]
    public void Breaking_a_middle_subrun_drops_ittsu_contribution()
    {
        var chi234s = Meld.Chi(Tile.FromId(19), Tile.FromId(21), fromSeat: 3);
        int[] withFull = Hand.FromNotation("123456789m5p").CloneCounts();
        int[] broken = Hand.FromNotation("12346789m5p7p").CloneCounts();

        double sFull = YakuPotential.Score(new Hand(withFull, [chi234s]), null, StateSnapshot.Empty);
        double sBroken = YakuPotential.Score(new Hand(broken, [chi234s]), null, StateSnapshot.Empty);
        Assert.True(sFull > sBroken,
            $"breaking ittsu subrun should drop score (full={sFull:F3}, broken={sBroken:F3})");
    }

    [Fact]
    public void Open_chi_at_ittsu_subrun_locks_its_position()
    {
        var chi789m = Meld.Chi(Tile.FromId(6), Tile.FromId(8), fromSeat: 3);
        int[] withRest = Hand.FromNotation("123456m1p5p1s9s").CloneCounts();
        int[] noManMaterial = Hand.FromNotation("123456p1s9s1z2z").CloneCounts();

        double sWith = YakuPotential.Score(new Hand(withRest, [chi789m]), null, StateSnapshot.Empty);
        double sControl = YakuPotential.Score(new Hand(noManMaterial, [chi789m]), null, StateSnapshot.Empty);
        Assert.True(sWith > sControl + 0.15,
            $"chi locking a subrun with closed support for the rest should fire ittsu " +
            $"(with={sWith:F3}, control={sControl:F3})");
    }
}

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
        // 13 tiles, no terminals / honors → tanyao locked (1 han) → score ≈ 0.5
        // (chiitoitsu doesn't fire — triplets aren't pairs).
        var hand = Hand.FromNotation("222m555p888s2345p");
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.InRange(score, 0.5, 0.55);
    }

    [Fact]
    public void Dragon_triplet_locks_yakuhai()
    {
        // 13 tiles, three haku → yakuhai certainty 1.0 → 1 han / 2 = 0.5.
        // Three honors block tanyao (3 t/h tiles → tanyao cert 0).
        var hand = Hand.FromNotation("555z123m456p789s2p");
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.InRange(score, 0.5, 0.55);
    }

    [Fact]
    public void Seat_wind_pair_only_counts_when_seat_info_known()
    {
        // Two East tiles. East wind = id 27. With seat info unknown the policy
        // can't tell if East is our seat wind, so it shouldn't bias toward the
        // pair (the snapshot's default OurSeat=0 would otherwise leak).
        var hand = Hand.FromNotation("11z123m456p789s5p2p");
        var unknown = StateSnapshot.Empty;                       // SeatInfoKnown = false
        var known = SeatKnownState(seat: 0, round: 0);           // East seat, East round

        double withoutInfo = YakuPotential.Score(hand, null, unknown);
        double withInfo = YakuPotential.Score(hand, null, known);
        Assert.True(withInfo > withoutInfo,
            $"expected the seat-wind pair to score higher when seat info is known (got {withoutInfo:F3} vs {withInfo:F3})");
    }

    [Fact]
    public void Pure_one_suit_hand_scores_high_for_honitsu()
    {
        // 13 manzu, no off-suit, fully concealed → closed honitsu (3 han) at full
        // certainty saturates the 0..1 score.
        var hand = Hand.FromNotation("1234567899m11m9m");
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Six_pairs_close_to_chiitoitsu()
    {
        // 6 distinct pairs + 1 single. Closed chiitoitsu cert = pairs / 6 = 1.0
        // → 2 han / 2 = full score.
        var hand = Hand.FromNotation("11m22m33p44p55s66s7z");
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Removed_tile_arg_simulates_post_discard_view()
    {
        // 14-tile hand with 6 pairs + 1 random + 1 random. Discarding the
        // off-suit single should leave the chiitoitsu route saturated.
        var hand = Hand.FromNotation("11m22m33p44p55s66s7z3m");
        double withoutRemoval = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        double afterDiscard3m = YakuPotential.Score(hand, Tile.FromId(2), StateSnapshot.Empty);
        Assert.True(afterDiscard3m >= withoutRemoval);
    }

    [Fact]
    public void Score_is_clamped_to_one()
    {
        // Closed honitsu (3 han weighted) + yakuhai dragon triplet (1 han
        // weighted) would over-saturate without the cap. Cap pegs to 1.0.
        var hand = Hand.FromNotation("111z2345m6m7m8m99m");
        double score = YakuPotential.Score(hand, null, StateSnapshot.Empty);
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Discard_scorer_surfaces_yaku_potential_field()
    {
        // Sanity: yaku potential should be exposed on ScoredDiscard so the
        // overlay can render it. Using a non-trivial hand so at least one
        // candidate has nonzero potential.
        var s = Snapshots.Closed14("222m555p888s23456p");
        var scored = DiscardScorer.Score(s);
        Assert.NotEmpty(scored);
        Assert.Contains(scored, sd => sd.YakuPotential > 0);
    }
}

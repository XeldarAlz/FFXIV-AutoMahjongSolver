using Mahjong.Core;
using Mahjong.Plugin.Dalamud.GameState;

namespace Mahjong.Plugin.Dalamud.Tests;

public class MeldTrackerTests
{
    [Fact]
    public void Starts_empty()
    {
        var tracker = new MeldTracker();
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void Record_appends_in_call_order()
    {
        var tracker = new MeldTracker();
        var pon = Meld.Pon(Tile.FromId(5), Tile.FromId(5), fromSeat: 1);
        var chi = Meld.Chi(Tile.FromId(0), Tile.FromId(2), fromSeat: 3);

        tracker.Record(pon);
        tracker.Record(chi);

        Assert.Equal(2, tracker.Melds.Count);
        Assert.Equal(pon, tracker.Melds[0]);
        Assert.Equal(chi, tracker.Melds[1]);
    }

    [Fact]
    public void Clear_drops_every_meld()
    {
        var tracker = new MeldTracker();
        tracker.Record(Meld.Pon(Tile.FromId(5), Tile.FromId(5), fromSeat: 1));
        tracker.Record(Meld.AnKan(Tile.FromId(7)));

        tracker.Clear();
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void ObserveWall_clears_on_wall_jump_up()
    {
        var tracker = new MeldTracker();
        tracker.Record(Meld.Pon(Tile.FromId(5), Tile.FromId(5), fromSeat: 1));
        tracker.ObserveWall(20);
        tracker.ObserveWall(70);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void ObserveWall_does_not_clear_within_a_hand()
    {
        var tracker = new MeldTracker();
        tracker.Record(Meld.Pon(Tile.FromId(5), Tile.FromId(5), fromSeat: 1));
        tracker.ObserveWall(70);
        tracker.ObserveWall(60);
        tracker.ObserveWall(40);
        tracker.ObserveWall(10);
        Assert.Single(tracker.Melds);
    }

    [Fact]
    public void ObserveWall_tolerates_minor_jitter()
    {
        var tracker = new MeldTracker();
        tracker.Record(Meld.Pon(Tile.FromId(5), Tile.FromId(5), fromSeat: 1));
        tracker.ObserveWall(20);
        tracker.ObserveWall(24);
        tracker.ObserveWall(22);
        Assert.Single(tracker.Melds);
    }

    [Fact]
    public void ObserveWall_is_a_noop_when_already_empty()
    {
        var tracker = new MeldTracker();
        tracker.ObserveWall(20);
        tracker.ObserveWall(70);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void Melds_property_reflects_live_state()
    {
        var tracker = new MeldTracker();
        var snapshot1 = tracker.Melds;
        Assert.Empty(snapshot1);

        tracker.Record(Meld.AnKan(Tile.FromId(0)));
        // Tracker returns the underlying list as IReadOnlyList; pin this so a future "return a copy" change is intentional.
        Assert.Single(snapshot1);
    }

    private static Tile[] Hand(string s) => Tiles.Parse(s);

    [Fact]
    public void ObserveSnapshot_first_call_returns_null()
    {
        var tracker = new MeldTracker();
        var inferred = tracker.ObserveSnapshot(Hand("123m45p67s11z"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(inferred);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_infers_pon_when_pair_disappears_and_opp_discarded()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        var inferred = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);

        Assert.NotNull(inferred);
        Assert.Equal(MeldKind.Pon, inferred!.Value.Kind);
        Assert.Equal(4, inferred.Value.ClaimedTile!.Value.Id);
        Assert.Equal(2, inferred.Value.ClaimedFromSeat);
        Assert.Single(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_infers_chi_with_adjacent_pair_low_extension()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("245m12345p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        var inferred = tracker.ObserveSnapshot(Hand("2m12345p1234567z"), [0, 0, 0, 1], ourSeat: 0);

        Assert.NotNull(inferred);
        Assert.Equal(MeldKind.Chi, inferred!.Value.Kind);
        Assert.Equal(2, inferred.Value.Tiles[0].Id);
        Assert.Equal(3, inferred.Value.Tiles[1].Id);
        Assert.Equal(4, inferred.Value.Tiles[2].Id);
        Assert.Equal(2, inferred.Value.ClaimedTile!.Value.Id);
        Assert.Equal(3, inferred.Value.ClaimedFromSeat);
    }

    [Fact]
    public void ObserveSnapshot_infers_chi_with_gapped_pair_middle_call()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("246m12345p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        var inferred = tracker.ObserveSnapshot(Hand("2m12345p1234567z"), [0, 0, 0, 1], ourSeat: 0);

        Assert.NotNull(inferred);
        Assert.Equal(MeldKind.Chi, inferred!.Value.Kind);
        Assert.Equal(3, inferred.Value.Tiles[0].Id);
        Assert.Equal(4, inferred.Value.ClaimedTile!.Value.Id);
    }

    [Fact]
    public void ObserveSnapshot_chi_respects_suit_boundary_at_1m_2m()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("12m1234p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        var inferred = tracker.ObserveSnapshot(Hand("1234p1234567z"), [0, 0, 0, 1], ourSeat: 0);

        Assert.NotNull(inferred);
        Assert.Equal(MeldKind.Chi, inferred!.Value.Kind);
        Assert.Equal(0, inferred.Value.Tiles[0].Id);
        Assert.Equal(2, inferred.Value.ClaimedTile!.Value.Id);
    }

    [Fact]
    public void ObserveSnapshot_infers_minkan_when_triplet_disappears_and_opp_discarded()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m777p123s1234z"), [0, 0, 0, 0], ourSeat: 0);
        var inferred = tracker.ObserveSnapshot(Hand("123m123s1234z"), [0, 1, 0, 0], ourSeat: 0);

        Assert.NotNull(inferred);
        Assert.Equal(MeldKind.MinKan, inferred!.Value.Kind);
        Assert.Equal(15, inferred.Value.ClaimedTile!.Value.Id);
        Assert.Equal(1, inferred.Value.ClaimedFromSeat);
        Assert.Equal(4, inferred.Value.TileCount);
    }

    [Fact]
    public void ObserveSnapshot_returns_null_when_only_we_discarded()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m45p67s11z123m"), [0, 0, 0, 0], ourSeat: 0);
        var inferred = tracker.ObserveSnapshot(Hand("123m45p67s11z23m"), [1, 0, 0, 0], ourSeat: 0);
        Assert.Null(inferred);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_returns_null_when_hand_shrunk_but_no_opp_discarded()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        var inferred = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(inferred);
    }

    [Fact]
    public void ObserveSnapshot_returns_null_when_removed_tiles_form_invalid_chi()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("13m45p67s11z123m9p"), [0, 0, 0, 0], ourSeat: 0);
        var inferred = tracker.ObserveSnapshot(Hand("45p67s11z123m"), [0, 0, 1, 0], ourSeat: 0);
        Assert.Null(inferred);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_does_not_double_record_after_wall_reset()
    {
        var tracker = new MeldTracker();
        tracker.ObserveWall(40);
        tracker.ObserveSnapshot(Hand("245m12345p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        tracker.ObserveSnapshot(Hand("2m12345p1234567z"), [0, 0, 0, 1], ourSeat: 0);
        Assert.Single(tracker.Melds);

        tracker.ObserveWall(70);
        Assert.Empty(tracker.Melds);

        var inferred = tracker.ObserveSnapshot(Hand("123m45p67s11z123m"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(inferred);
    }

    [Fact]
    public void ObserveSnapshot_latches_opp_discard_across_call_prompt_window()
    {
        // Pins that the tracker latches the most-recent opp discarder across the call-prompt window.
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);
        var inferred = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);

        Assert.NotNull(inferred);
        Assert.Equal(MeldKind.Pon, inferred!.Value.Kind);
        Assert.Equal(2, inferred.Value.ClaimedFromSeat);
        Assert.Single(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_defers_inference_when_shrink_lands_before_discard_count_updates()
    {
        // Regression: closed-hand and discard-count bytes can land on different ticks; the tracker must hold the pre-shrink baseline so a later opp signal can resolve inference.
        var tracker = new MeldTracker();

        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);

        var t0 = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(t0);

        var t1 = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);
        Assert.NotNull(t1);
        Assert.Equal(MeldKind.Pon, t1!.Value.Kind);
        Assert.Equal(4, t1.Value.ClaimedTile!.Value.Id);
        Assert.Equal(2, t1.Value.ClaimedFromSeat);
        Assert.Single(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_deferral_resolves_with_minkan_shape()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m777p123s1234z"), [0, 0, 0, 0], ourSeat: 0);
        var t0 = tracker.ObserveSnapshot(Hand("123m123s1234z"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(t0);

        var t1 = tracker.ObserveSnapshot(Hand("123m123s1234z"), [0, 1, 0, 0], ourSeat: 0);
        Assert.NotNull(t1);
        Assert.Equal(MeldKind.MinKan, t1!.Value.Kind);
        Assert.Equal(1, t1.Value.ClaimedFromSeat);
        Assert.Equal(4, t1.Value.TileCount);
    }

    [Fact]
    public void ObserveSnapshot_deferral_abandoned_when_we_discard_during_window()
    {
        // Pins that our own discard during deferral abandons the stale baseline so a later opp discard cannot synthesize a phantom meld.
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);

        var t0 = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(t0);

        var t1 = tracker.ObserveSnapshot(Hand("23m789p1234567z"), [1, 0, 0, 0], ourSeat: 0);
        Assert.Null(t1);

        var t2 = tracker.ObserveSnapshot(Hand("23m789p1234567z"), [1, 0, 1, 0], ourSeat: 0);
        Assert.Null(t2);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_deferral_capped_at_max_ticks()
    {
        // Deferral caps at MaxDeferralTicks (30) so a permanently-stuck race cannot strand the tracker between hands.
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);

        for (int i = 0; i < 30; i++)
        {
            var r = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
            Assert.Null(r);
        }
        var capped = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(capped);
        Assert.Empty(tracker.Melds);

        var afterCap = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);
        Assert.Null(afterCap);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_deferral_does_not_block_first_observation()
    {
        var tracker = new MeldTracker();
        var r = tracker.ObserveSnapshot(Hand("123m45p67s11z"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(r);

        var r2 = tracker.ObserveSnapshot(Hand("123m45p67s11z4m"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(r2);
    }

    [Fact]
    public void ObserveSnapshot_deferral_clears_on_hand_boundary()
    {
        // Pins that ObserveWall resets deferred state so a stale baseline cannot misfire on the new hand.
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        var t0 = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(t0);

        tracker.ObserveWall(40);
        tracker.ObserveWall(70);

        var fresh = tracker.ObserveSnapshot(Hand("123m45p67s11z123m"), [0, 0, 0, 0], ourSeat: 0);
        Assert.Null(fresh);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_consumes_pending_discarder_so_second_call_needs_fresh_discard()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0);
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);
        var firstPon = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);
        Assert.NotNull(firstPon);

        var stale = tracker.ObserveSnapshot(Hand("3m789p1234567z"), [0, 0, 1, 0], ourSeat: 0);
        Assert.Null(stale);
    }

    [Fact]
    public void ObserveSnapshot_respects_ourSeat_when_classifying_discard_owner()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 2);
        var inferred = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 2);
        Assert.Null(inferred);
    }

    [Fact]
    public void ObserveSnapshot_with_unknown_seat_minus_one_skips_inference()
    {
        // ourSeat = -1 is the "unknown" sentinel; skip inference rather than risk wrong fromSeat attribution.
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: -1);
        var inferred = tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: -1);
        Assert.Null(inferred);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void ObserveSnapshot_rejects_out_of_range_seat()
    {
        var tracker = new MeldTracker();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tracker.ObserveSnapshot(Hand("123m"), [0, 0, 0, 0], ourSeat: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tracker.ObserveSnapshot(Hand("123m"), [0, 0, 0, 0], ourSeat: -2));
    }

    [Fact]
    public void MeldAkadora_starts_zero_and_stays_zero_without_meld()
    {
        var tracker = new MeldTracker();
        Assert.Equal(0, tracker.MeldAkadora);

        tracker.ObserveSnapshot(Hand("123m45p67s11z123m"), [0, 0, 0, 0], ourSeat: 0, currentAkadora: 1);
        tracker.ObserveSnapshot(Hand("123m45p67s11z23m"), [1, 0, 0, 0], ourSeat: 0, currentAkadora: 1);
        Assert.Equal(0, tracker.MeldAkadora);
    }

    [Fact]
    public void MeldAkadora_increments_when_pon_consumes_red_pair()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0, currentAkadora: 1);
        var inferred = tracker.ObserveSnapshot(
            Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 0, currentAkadora: 0);

        Assert.NotNull(inferred);
        Assert.Equal(MeldKind.Pon, inferred!.Value.Kind);
        Assert.Equal(1, tracker.MeldAkadora);
    }

    [Fact]
    public void MeldAkadora_increments_by_two_when_chi_consumes_two_reds()
    {
        // Tracker diffs the akadora count rather than checking tile identity, so a delta of 2 must credit 2.
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("234m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0, currentAkadora: 2);
        var inferred = tracker.ObserveSnapshot(
            Hand("234m789p1234567z"), [0, 0, 1, 0], ourSeat: 0, currentAkadora: 0);

        Assert.NotNull(inferred);
        Assert.Equal(2, tracker.MeldAkadora);
    }

    [Fact]
    public void MeldAkadora_does_not_credit_when_no_red_was_consumed()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0, currentAkadora: 1);
        var inferred = tracker.ObserveSnapshot(
            Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 0, currentAkadora: 1);

        Assert.NotNull(inferred);
        Assert.Equal(0, tracker.MeldAkadora);
    }

    [Fact]
    public void MeldAkadora_resets_on_hand_boundary()
    {
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0, currentAkadora: 1);
        tracker.ObserveSnapshot(Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 0, currentAkadora: 0);
        Assert.Equal(1, tracker.MeldAkadora);

        tracker.ObserveWall(40);
        tracker.ObserveWall(70);
        Assert.Equal(0, tracker.MeldAkadora);
    }

    [Fact]
    public void MeldAkadora_clamps_at_zero_when_count_jumps_up()
    {
        // Pins the defensive clamp: a count increase between observations must not produce a negative delta.
        var tracker = new MeldTracker();
        tracker.ObserveSnapshot(Hand("123m55m789p1234567z"), [0, 0, 0, 0], ourSeat: 0, currentAkadora: 0);
        var inferred = tracker.ObserveSnapshot(
            Hand("123m789p1234567z"), [0, 0, 1, 0], ourSeat: 0, currentAkadora: 2);

        Assert.NotNull(inferred);
        Assert.Equal(0, tracker.MeldAkadora);
    }

    [Fact]
    public void ObserveSnapshot_rejects_negative_akadora()
    {
        var tracker = new MeldTracker();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tracker.ObserveSnapshot(Hand("123m"), [0, 0, 0, 0], ourSeat: 0, currentAkadora: -1));
    }
}

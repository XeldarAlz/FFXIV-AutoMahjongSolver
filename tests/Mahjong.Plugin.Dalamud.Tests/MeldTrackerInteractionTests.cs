using Mahjong.Core;
using Mahjong.Plugin.Dalamud.GameState;

namespace Mahjong.Plugin.Dalamud.Tests;

/// <summary>
/// Sequence-of-actions tests for <see cref="MeldTracker"/> — pin behavior
/// across hand transitions, repeated calls, and clear/reset interactions.
/// </summary>
public class MeldTrackerInteractionTests
{
    [Fact]
    public void Round_lifecycle_records_then_resets_at_next_full_hand()
    {
        var tracker = new MeldTracker();
        // Hand started — wall at full.
        tracker.ObserveWall(70);

        // Round in progress: player calls pon then chi. Wall has been ticking down.
        tracker.ObserveWall(45);
        tracker.Record(Meld.Pon(Tile.FromId(5), Tile.FromId(5), fromSeat: 1));
        tracker.ObserveWall(30);
        tracker.Record(Meld.Chi(Tile.FromId(0), Tile.FromId(2), fromSeat: 3));
        Assert.Equal(2, tracker.Melds.Count);

        // Mid-round wall continues to drop — tracker preserved.
        tracker.ObserveWall(15);
        Assert.Equal(2, tracker.Melds.Count);

        // New hand begins — wall jumps back up.
        tracker.ObserveWall(70);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void Multiple_kans_in_one_round_all_recorded()
    {
        var tracker = new MeldTracker();
        tracker.Record(Meld.AnKan(Tile.FromId(7)));
        tracker.Record(Meld.MinKan(Tile.FromId(13), Tile.FromId(13), fromSeat: 2));

        Assert.Equal(2, tracker.Melds.Count);
        Assert.True(tracker.Melds[0].IsKan);
        Assert.True(tracker.Melds[1].IsKan);
    }

    [Fact]
    public void ShouMinKan_is_a_kan()
    {
        var tracker = new MeldTracker();
        tracker.Record(Meld.ShouMinKan(Tile.FromId(20), Tile.FromId(20), originalFromSeat: 1));
        Assert.Single(tracker.Melds);
        Assert.True(tracker.Melds[0].IsKan);
    }

    [Fact]
    public void AnKan_is_closed_open_kans_are_open()
    {
        var tracker = new MeldTracker();
        tracker.Record(Meld.AnKan(Tile.FromId(0)));
        tracker.Record(Meld.MinKan(Tile.FromId(1), Tile.FromId(1), fromSeat: 0));

        Assert.False(tracker.Melds[0].IsOpen);
        Assert.True(tracker.Melds[1].IsOpen);
    }

    [Fact]
    public void Clear_then_record_starts_fresh_indexing()
    {
        var tracker = new MeldTracker();
        tracker.Record(Meld.Pon(Tile.FromId(5), Tile.FromId(5), fromSeat: 1));
        tracker.Clear();
        tracker.Record(Meld.AnKan(Tile.FromId(7)));

        Assert.Single(tracker.Melds);
        Assert.True(tracker.Melds[0].IsKan);
    }

    [Fact]
    public void ObserveWall_after_call_does_not_clear_during_addon_settling_window()
    {
        // Regression for the "DiscardScorer requires a 14-tile hand (closed=11
        // melds=0)" freeze observed 2026-05-09 10:06: the addon's hand-array
        // briefly still reads 13 for ~5 ms after the call accept, before
        // dropping to 11. Old reset-on-closed-hand-13 logic erased the meld
        // we'd just recorded during that window. Wall has stopped at the
        // pre-call value by then (no draw yet) so a wall-based reset stays
        // quiet — exactly the behavior we want.
        var tracker = new MeldTracker();
        tracker.ObserveWall(20);
        tracker.Record(Meld.Chi(Tile.FromId(24), Tile.FromId(26), fromSeat: 3));

        // Subsequent ticks immediately after the call — wall hasn't moved.
        tracker.ObserveWall(20);
        tracker.ObserveWall(20);
        tracker.ObserveWall(20);
        Assert.Single(tracker.Melds);

        // The eventual draw drops the wall by 1 — meld preserved.
        tracker.ObserveWall(19);
        Assert.Single(tracker.Melds);
    }
}

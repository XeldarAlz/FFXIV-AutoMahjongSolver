using Mahjong.Core;
using Mahjong.Plugin.Dalamud.GameState;

namespace Mahjong.Plugin.Dalamud.Tests;

public class MeldTrackerInteractionTests
{
    [Fact]
    public void Round_lifecycle_records_then_resets_at_next_full_hand()
    {
        var tracker = new MeldTracker();
        tracker.ObserveWall(70);

        tracker.ObserveWall(45);
        tracker.Record(Meld.Pon(Tile.FromId(5), Tile.FromId(5), fromSeat: 1));
        tracker.ObserveWall(30);
        tracker.Record(Meld.Chi(Tile.FromId(0), Tile.FromId(2), fromSeat: 3));
        Assert.Equal(2, tracker.Melds.Count);

        tracker.ObserveWall(15);
        Assert.Equal(2, tracker.Melds.Count);

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
        // Regression: a closed-hand-13 reset must not erase a freshly-recorded meld while the addon hand-array is still settling after a call.
        var tracker = new MeldTracker();
        tracker.ObserveWall(20);
        tracker.Record(Meld.Chi(Tile.FromId(24), Tile.FromId(26), fromSeat: 3));

        tracker.ObserveWall(20);
        tracker.ObserveWall(20);
        tracker.ObserveWall(20);
        Assert.Single(tracker.Melds);

        tracker.ObserveWall(19);
        Assert.Single(tracker.Melds);
    }
}

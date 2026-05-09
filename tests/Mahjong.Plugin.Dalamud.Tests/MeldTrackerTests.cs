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
        // ±5 read-glitch tolerance — same threshold GameLogger.MaybeRollHand uses.
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
        // The tracker exposes the underlying list as IReadOnlyList — the
        // pre-write snapshot reflects subsequent writes. Pin this so a
        // future change to "return a copy" is intentional.
        Assert.Single(snapshot1);
    }
}

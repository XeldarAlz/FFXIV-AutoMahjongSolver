using Xunit;

namespace Mahjong.Engine.Tests;

public class UkeireTests
{
    [Fact]
    public void Tenpai_hand_has_entry_with_shanten_zero_and_accepted_tiles()
    {
        var hand = Hand.FromNotation("123m456p789s11z22z3z");
        Assert.Equal(14, hand.ClosedTileCount);

        var ukeire = UkeireEnumerator.Enumerate(hand);

        var dropZ3 = ukeire.Single(e => e.Discard.Id == 29);
        Assert.Equal(0, dropZ3.ShantenAfter);
        Assert.Contains(dropZ3.AcceptedKinds, t => t.Id == 27);
        Assert.Contains(dropZ3.AcceptedKinds, t => t.Id == 28);
    }

    [Fact]
    public void Weighted_ukeire_honors_wall_visibility()
    {
        var hand = Hand.FromNotation("123m456p789s11z22z3z");
        var wall = new Wall();
        for (int i = 0; i < 3; i++)
            wall.Observe(Tile.FromId(27));

        var ukeire = UkeireEnumerator.Enumerate(hand, wall);
        var dropZ3 = ukeire.Single(e => e.Discard.Id == 29);

        Assert.Contains(dropZ3.AcceptedKinds, t => t.Id == 27);
        var unwalled = UkeireEnumerator.Enumerate(hand).Single(e => e.Discard.Id == 29);
        Assert.True(dropZ3.WeightedCount < unwalled.WeightedCount);
    }

    [Fact]
    public void Requires_fourteen_tile_hand()
    {
        var hand = Hand.FromNotation("123m456p789s11z");
        Assert.Throws<ArgumentException>(() => UkeireEnumerator.Enumerate(hand));
    }
}

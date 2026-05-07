namespace Mahjong.Core.Tests;

public class HandTests
{
    [Fact]
    public void Constructor_rejects_wrong_length_count_array()
    {
        Assert.Throws<ArgumentException>(() => new Hand(new int[33]));
        Assert.Throws<ArgumentException>(() => new Hand(new int[35]));
    }

    [Fact]
    public void Constructor_rejects_negative_or_overflow_counts()
    {
        var bad = new int[Tile.Count34];
        bad[0] = -1;
        Assert.Throws<ArgumentException>(() => new Hand(bad));

        bad[0] = Tile.CopiesPerKind + 1;
        Assert.Throws<ArgumentException>(() => new Hand(bad));
    }

    [Fact]
    public void Constructor_defensive_copies_count_array()
    {
        var counts = Tiles.ToCounts(Tiles.Parse("123m"));
        var hand = new Hand(counts);

        // Mutate the source array — the hand should be unaffected.
        counts[0] = 0;

        Assert.Equal(1, hand.ClosedCounts[0]);  // 1m still present
    }

    [Fact]
    public void Constructor_defensive_copies_open_melds()
    {
        var melds = new List<Meld> { Meld.Pon(Tile.FromId(0), Tile.FromId(0), 1) };
        var hand = new Hand(Tiles.ToCounts(Tiles.Parse("234m")), melds);

        melds.Clear();   // mutate caller's list

        Assert.Single(hand.OpenMelds);
    }

    [Fact]
    public void WithTileAdded_returns_new_instance_with_increment()
    {
        var hand = Hand.FromNotation("123m");
        var bigger = hand.WithTileAdded(Tile.FromId(0));  // add another 1m

        Assert.Equal(3, hand.ClosedTileCount);
        Assert.Equal(4, bigger.ClosedTileCount);
        Assert.Equal(1, hand.ClosedCounts[0]);
        Assert.Equal(2, bigger.ClosedCounts[0]);
    }

    [Fact]
    public void WithTileRemoved_throws_when_tile_absent()
    {
        var hand = Hand.FromNotation("123m");
        Assert.Throws<InvalidOperationException>(() => hand.WithTileRemoved(Tile.FromId(8)));
    }

    [Fact]
    public void TotalShantenTileCount_treats_kans_as_three()
    {
        var hand = new Hand(
            Tiles.ToCounts(Tiles.Parse("123m")),
            [Meld.AnKan(Tile.FromId(0))]);

        Assert.Equal(6, hand.TotalShantenTileCount);  // 3 closed + 3 (kan)
    }

    [Fact]
    public void FromNotation_round_trips()
    {
        var hand = Hand.FromNotation("123m456p789s11z");
        Assert.Equal(11, hand.ClosedTileCount);
        Assert.Empty(hand.OpenMelds);
    }
}

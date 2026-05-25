namespace Mahjong.Core.Tests;

public class WallTests
{
    [Fact]
    public void Fresh_wall_has_full_live_count_for_every_tile()
    {
        var wall = new Wall();

        foreach (var t in Tile.All34())
        {
            Assert.Equal(0, wall.SeenOf(t));
            Assert.Equal(Tile.CopiesPerKind, wall.LiveOf(t));
        }
    }

    [Fact]
    public void Observe_decrements_live_count()
    {
        var wall = new Wall();
        var tile = Tile.FromId(0);

        wall.Observe(tile);
        wall.Observe(tile);

        Assert.Equal(2, wall.SeenOf(tile));
        Assert.Equal(2, wall.LiveOf(tile));
    }

    [Fact]
    public void Observe_with_negative_delta_undoes_a_sighting()
    {
        var wall = new Wall();
        var tile = Tile.FromId(5);

        wall.Observe(tile);
        wall.Observe(tile, -1);

        Assert.Equal(0, wall.SeenOf(tile));
    }

    [Fact]
    public void Observe_rejects_overflow_above_four_copies()
    {
        var wall = new Wall();
        var tile = Tile.FromId(0);

        for (int i = 0; i < Tile.CopiesPerKind; i++)
            wall.Observe(tile);

        Assert.Throws<InvalidOperationException>(() => wall.Observe(tile));
    }

    [Fact]
    public void Observe_rejects_underflow_below_zero()
    {
        var wall = new Wall();
        Assert.Throws<InvalidOperationException>(() => wall.Observe(Tile.FromId(0), -1));
    }

    [Fact]
    public void ObserveCounts_validates_input_length()
    {
        var wall = new Wall();
        Assert.Throws<ArgumentException>(() => wall.ObserveCounts(new int[33]));
    }

    [Fact]
    public void Clear_resets_all_counts_to_zero()
    {
        var wall = new Wall();
        wall.Observe(Tile.FromId(0));
        wall.Observe(Tile.FromId(33));

        wall.Clear();

        foreach (var t in Tile.All34())
            Assert.Equal(0, wall.SeenOf(t));
    }

    [Fact]
    public void LiveSnapshot_returns_a_fresh_array_each_time()
    {
        var wall = new Wall();
        wall.Observe(Tile.FromId(0));

        int[] snap1 = wall.LiveSnapshot();
        int[] snap2 = wall.LiveSnapshot();

        Assert.NotSame(snap1, snap2);
        Assert.Equal(snap1, snap2);

        snap1[0] = 99;
        int[] snap3 = wall.LiveSnapshot();
        Assert.Equal(Tile.CopiesPerKind - 1, snap3[0]);
    }
}

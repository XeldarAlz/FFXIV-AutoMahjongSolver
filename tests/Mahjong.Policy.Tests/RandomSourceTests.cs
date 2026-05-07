namespace Mahjong.Policy.Tests;

public class RandomSourceTests
{
    [Fact]
    public void SeededRandomSource_is_deterministic_per_seed()
    {
        var a = new SeededRandomSource(42);
        var b = new SeededRandomSource(42);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.Next(1000), b.Next(1000));
        }
    }

    [Fact]
    public void Different_seeds_produce_different_sequences()
    {
        var a = new SeededRandomSource(1);
        var b = new SeededRandomSource(2);
        bool diverged = false;
        for (int i = 0; i < 20 && !diverged; i++)
        {
            if (a.Next(1000) != b.Next(1000))
                diverged = true;
        }
        Assert.True(diverged);
    }

    [Fact]
    public void Next_max_returns_in_range()
    {
        var rng = new SeededRandomSource(123);
        for (int i = 0; i < 1000; i++)
        {
            int v = rng.Next(50);
            Assert.InRange(v, 0, 49);
        }
    }

    [Fact]
    public void Shuffle_extension_permutes_in_place_deterministically()
    {
        var a = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var b = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        new SeededRandomSource(42).Shuffle(a);
        new SeededRandomSource(42).Shuffle(b);

        Assert.Equal(a, b);                     // same seed → same permutation
        Assert.NotEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, a);   // and it was actually shuffled
    }

    [Fact]
    public void DrawFromLive_returns_null_when_pool_empty()
    {
        var rng = new SeededRandomSource(7);
        var live = new int[Tile.Count34];   // all zeros
        Assert.Null(rng.DrawFromLive(live));
    }

    [Fact]
    public void DrawFromLive_only_returns_tiles_with_nonzero_count()
    {
        var rng = new SeededRandomSource(7);
        var live = new int[Tile.Count34];
        live[5] = 4;
        live[20] = 1;

        for (int i = 0; i < 100; i++)
        {
            var t = rng.DrawFromLive(live);
            Assert.NotNull(t);
            Assert.True(t!.Value.Id == 5 || t.Value.Id == 20);
        }
    }
}

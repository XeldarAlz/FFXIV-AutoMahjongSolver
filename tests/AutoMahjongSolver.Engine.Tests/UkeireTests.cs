using AutoMahjongSolver.Engine;
using Xunit;

namespace AutoMahjongSolver.Engine.Tests;

public class UkeireTests
{
    [Fact]
    public void TankiTenpai_AcceptsOnlyTheHeadTile()
    {
        // 123m 456m 789m 123p + lone 5z(white) : pure tanki wait on 5z. One copy held -> 3 remain.
        var counts = Tiles.ParseCounts("123456789m123p5z");
        Acceptance acc = UkeireCalculator.Accept(counts);

        Assert.Equal(0, acc.Shanten);
        Assert.Equal(1, acc.Types);
        AcceptTile only = Assert.Single(acc.Tiles);
        Assert.Equal(Tiles.Index("5z"), only.Tile);
        Assert.Equal(3, only.Remaining);
    }

    [Fact]
    public void RyanmenTenpai_AcceptsBothEnds()
    {
        // 123m 456m 789m 99p + 23s : ryanmen waiting 1s / 4s
        var counts = Tiles.ParseCounts("123456789m99p23s");
        Acceptance acc = UkeireCalculator.Accept(counts);

        Assert.Equal(0, acc.Shanten);
        Assert.Equal(2, acc.Types);
        Assert.Contains(acc.Tiles, t => t.Tile == Tiles.Index("1s"));
        Assert.Contains(acc.Tiles, t => t.Tile == Tiles.Index("4s"));
        Assert.Equal(8, acc.Count); // four each of 1s and 4s unseen
    }

    [Fact]
    public void BestDiscards_DropsTheUselessFloater_ToReachTenpai()
    {
        // 13-tile tanki tenpai + a drawn red dragon (7z). Best play discards 7z and stays tenpai.
        var counts14 = Tiles.ParseCounts("123456789m1231p7z");
        var options = UkeireCalculator.BestDiscards(counts14);

        DiscardOption best = options[0];
        Assert.Equal(Tiles.Index("7z"), best.Tile);
        Assert.Equal(0, best.Shanten);
        Assert.True(best.UkeireCount > 0);
    }

    [Fact]
    public void Seen_ReducesRemainingCount()
    {
        // Same pure tanki on 5z, but mark all four 5z as already seen on the table -> zero useful copies.
        var counts = Tiles.ParseCounts("123456789m123p5z");
        var seen = new int[Tiles.TypeCount];
        seen[Tiles.Index("5z")] = 4;

        Acceptance acc = UkeireCalculator.Accept(counts, meldCount: 0, seen: seen);

        Assert.Equal(0, acc.Shanten);
        Assert.Equal(0, acc.Count); // the only accepting tile has no copies left
    }
}

using AutoMahjongSolver.Engine;
using Xunit;

namespace AutoMahjongSolver.Engine.Tests;

public class MeldTests
{
    private static int Tile(string single) => Tiles.Index(single);

    [Fact]
    public void TripletOfHonors_IsPon()
    {
        Assert.True(Meld.TryClassify([Tile("5z"), Tile("5z"), Tile("5z")], out Meld meld));
        Assert.Equal(MeldKind.Pon, meld.Kind);
    }

    [Fact]
    public void ConsecutiveSameSuit_IsChii()
    {
        Assert.True(Meld.TryClassify([Tile("7s"), Tile("5s"), Tile("6s")], out Meld meld));
        Assert.Equal(MeldKind.Chii, meld.Kind);
    }

    [Fact]
    public void FourIdentical_IsKan()
    {
        Assert.True(Meld.TryClassify([Tile("2p"), Tile("2p"), Tile("2p"), Tile("2p")], out Meld meld));
        Assert.Equal(MeldKind.Kan, meld.Kind);
    }

    [Fact]
    public void RunAcrossSuits_IsRejected()
    {
        Assert.False(Meld.TryClassify([Tile("9m"), Tile("1p"), Tile("2p")], out _));
    }

    [Fact]
    public void HonorRun_IsRejected()
    {
        Assert.False(Meld.TryClassify([Tile("5z"), Tile("6z"), Tile("7z")], out _));
    }

    [Fact]
    public void FourNonIdentical_IsRejected()
    {
        Assert.False(Meld.TryClassify([Tile("2p"), Tile("2p"), Tile("2p"), Tile("3p")], out _));
    }

    [Fact]
    public void WrongSize_IsRejected()
    {
        Assert.False(Meld.TryClassify([Tile("1m"), Tile("1m")], out _));
    }

    [Fact]
    public void OpenPon_CountsAsOneBlockForShanten()
    {
        // Concealed 11 tiles (after a pon): 123m 456m 789m + 1p tanki. One meld -> tenpai.
        int shanten = ShantenCalculator.Calculate(Tiles.ParseCounts("123456789m1p"), meldCount: 1);
        Assert.Equal(0, shanten);
    }
}

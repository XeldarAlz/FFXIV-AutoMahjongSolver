using AutoMahjongSolver.Engine;
using Xunit;

namespace AutoMahjongSolver.Engine.Tests;

public class ShantenTests
{
    private static int Shanten(string notation, int melds = 0)
        => ShantenCalculator.Calculate(Tiles.ParseCounts(notation), melds);

    [Fact]
    public void CompleteRegularHand_IsMinusOne()
    {
        // 123m 456m 789m 123p + 11p head = 4 sets + pair
        Assert.Equal(-1, Shanten("123456789m12311p"));
    }

    [Fact]
    public void FourSetsPlusFloater_IsTenpai_TankiWait()
    {
        // 123m 456m 789m 123p + lone 1p = tanki wait on the head
        Assert.Equal(0, Shanten("123456789m1231p"));
    }

    [Fact]
    public void ThreeSetsTwoPairs_IsTenpai_Shanpon()
    {
        // 123m 99m 456p 789s + 11z(east) head = shanpon tenpai on 9m / east
        Assert.Equal(0, Shanten("12399m456p789s11z"));
    }

    [Fact]
    public void TwoSetsTwoPartialsPair_IsOneShanten()
    {
        // 123m 456m + 11p(head) + 24p(kanchan) + 13s(kanchan) + 7z floater
        Assert.Equal(1, Shanten("123456m11p24p13s7z"));
    }

    [Fact]
    public void Kokushi_ThirteenWayTenpai_IsZero()
    {
        // one of each terminal/honor, no pair = the 13-sided wait
        Assert.Equal(0, Shanten("19m19p19s1234567z"));
    }

    [Fact]
    public void Kokushi_Complete_IsMinusOne()
    {
        // pair of 1m completes the thirteen orphans
        Assert.Equal(-1, Shanten("119m19p19s1234567z"));
    }

    [Fact]
    public void SevenPairs_Complete_IsMinusOne()
    {
        Assert.Equal(-1, Shanten("1133557799m1133p"));
    }

    [Fact]
    public void SevenPairs_OneShanten()
    {
        // five pairs + three distinct singles -> 1-shanten by the chiitoitsu form
        Assert.Equal(1, Shanten("113355m1133p579s"));
    }

    [Fact]
    public void OpenHand_TwoMelds_TankiTenpai()
    {
        // 2 called melds + 123m 456m concealed + 9p floater = tanki tenpai
        Assert.Equal(0, ShantenCalculator.Calculate(Tiles.ParseCounts("123456m9p"), meldCount: 2));
    }

    [Fact]
    public void OpenHand_TwoMelds_Complete()
    {
        // 2 called melds + 123m 456m + 99p head = complete
        Assert.Equal(-1, ShantenCalculator.Calculate(Tiles.ParseCounts("123456m99p"), meldCount: 2));
    }
}

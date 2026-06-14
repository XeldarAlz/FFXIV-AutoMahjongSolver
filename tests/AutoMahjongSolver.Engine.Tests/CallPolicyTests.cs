using AutoMahjongSolver.Engine;
using Xunit;

namespace AutoMahjongSolver.Engine.Tests;

public class CallPolicyTests
{
    private static bool Pon(string hand13, string tile, EvalContext? ctx = null, bool riichi = false)
        => CallPolicy.ShouldPon(Tiles.ParseCounts(hand13), null, Tiles.Index(tile), ctx ?? new EvalContext(), riichi);

    [Fact]
    public void YakuhaiPon_ThatAdvancesTheHand_IsTaken()
    {
        // 22m 55m 8m + 345p 567s + 55z: 1-shanten; ponning haku reaches tenpai
        Assert.True(Pon("22558m345p567s55z", "5z"));
    }

    [Fact]
    public void YakuhaiPon_FromClosedTenpai_IsDeclined()
    {
        // 223344m 456p 79s + 55z is already tenpai; the pon would not advance it (and costs riichi)
        Assert.False(Pon("223344m456p79s55z", "5z"));
    }

    [Fact]
    public void JunkPon_WithoutValue_IsDeclined()
    {
        // ponning 2m reaches tenpai but the open hand has no yaku path worth it
        Assert.False(Pon("22558m345p567s55z", "2m"));
    }

    [Fact]
    public void DoraTanyaoPon_WithTwoDora_IsTaken()
    {
        // indicator 1m -> 2m is dora; ponning 2m reaches tenpai holding two dora, all simples
        var ctx = new EvalContext { DoraIndicators = new[] { Tiles.Index("1m") } };
        Assert.True(Pon("225588m345p4567s", "2m", ctx));
    }

    [Fact]
    public void NoPon_IntoADeclaredRiichi()
    {
        Assert.False(Pon("22558m345p567s55z", "5z", riichi: true));
    }

    [Fact]
    public void SeatWindCountsAsYakuhai()
    {
        // 1z is East; as the East seat the same shape pon is live
        var east = new EvalContext { SeatWind = 0, RoundWind = 0 };
        var south = new EvalContext { SeatWind = 1, RoundWind = 1 };
        Assert.True(Pon("22558m345p567s11z", "1z", east));
        Assert.False(Pon("22558m345p567s11z", "1z", south));
    }
}

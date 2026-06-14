using AutoMahjongSolver.Engine;
using Xunit;

namespace AutoMahjongSolver.Engine.Tests;

public class YakuTests
{
    private static WinResult Eval(string notation, WinContext ctx, IReadOnlyList<Meld>? melds = null)
    {
        int[] counts = Tiles.ParseCounts(notation, out int aka);
        ctx = ctx with { AkaCount = ctx.AkaCount + aka };
        WinResult? result = YakuCalculator.Evaluate(counts, melds, ctx);
        Assert.NotNull(result);
        return result;
    }

    private static bool Has(WinResult r, Yaku yaku) => r.Yaku.Any(y => y.Yaku == yaku);

    private static WinContext Ron(string tile) => new() { WinTile = Tiles.Index(tile), Tsumo = false };
    private static WinContext Tsumo(string tile) => new() { WinTile = Tiles.Index(tile), Tsumo = true };

    [Fact]
    public void PinfuTsumoTanyao()
    {
        // 234m 567m 234p 567s 88s, tsumo 7m on the 56m ryanmen
        WinResult r = Eval("234567m234p56788s", Tsumo("7m"));
        Assert.True(Has(r, Yaku.Pinfu));
        Assert.True(Has(r, Yaku.Tanyao));
        Assert.True(Has(r, Yaku.MenzenTsumo));
        Assert.Equal(3, r.YakuHanTotal);
    }

    [Fact]
    public void PenchanWait_DeniesPinfu_AndRonHandIsYakuless()
    {
        // 12m + 456m 789p 234s 55s, ron 3m: penchan, terminals present, no yaku at all
        WinResult r = Eval("123456m789p23455s", Ron("3m"));
        Assert.False(Has(r, Yaku.Pinfu));
        Assert.Equal(0, r.YakuHanTotal);
        Assert.False(r.HasYaku);
    }

    [Fact]
    public void YaklessRonHand_StillWinsByTsumo()
    {
        WinResult r = Eval("123456m789p23455s", Tsumo("3m"));
        Assert.True(Has(r, Yaku.MenzenTsumo));
        Assert.Equal(1, r.YakuHanTotal);
    }

    [Fact]
    public void DoubleEastWind_CountsTwice()
    {
        // 111z triplet, seat east + round east
        WinResult r = Eval("123m456p789s11122z", Ron("2z") with { SeatWind = 0, RoundWind = 0 });
        Assert.True(Has(r, Yaku.SeatWind));
        Assert.True(Has(r, Yaku.RoundWind));
        Assert.Equal(2, r.YakuHanTotal);
    }

    [Fact]
    public void Chiitoitsu_Detected()
    {
        WinResult r = Eval("1122m334455p66s77z", Ron("7z"));
        Assert.True(Has(r, Yaku.Chiitoitsu));
        Assert.Equal(2, r.YakuHanTotal);
    }

    [Fact]
    public void RyanpeikoBeatsChiitoi()
    {
        // 223344m 556677p 99s parses as both; ryanpeiko (3) must win over chiitoi (2)
        WinResult r = Eval("223344m556677p99s", Ron("9s"));
        Assert.True(Has(r, Yaku.Ryanpeiko));
        Assert.False(Has(r, Yaku.Chiitoitsu));
    }

    [Fact]
    public void Iipeiko_ClosedOnly()
    {
        WinResult r = Eval("223344m567p56799s", Ron("9s"));
        Assert.True(Has(r, Yaku.Iipeiko));
    }

    [Fact]
    public void ToitoiSanankou_RonOnFourthTripletDoesNotCountAsConcealed()
    {
        // 111m 222p 333s concealed; 444z completed by ron -> toitoi + sanankou, not suuankou
        WinResult r = Eval("111m222p333s44455z", Ron("4z"));
        Assert.True(Has(r, Yaku.Toitoi));
        Assert.True(Has(r, Yaku.Sanankou));
        Assert.False(r.IsYakuman);
        Assert.Equal(4, r.YakuHanTotal);
    }

    [Fact]
    public void Suuankou_ByTsumo()
    {
        WinResult r = Eval("111m222p333s44455z", Tsumo("4z"));
        Assert.True(Has(r, Yaku.Suuankou));
        Assert.True(r.IsYakuman);
        Assert.Equal(13, r.YakuHanTotal);
    }

    [Fact]
    public void Kokushi_IsYakuman()
    {
        WinResult r = Eval("119m19p19s1234567z", Ron("1m"));
        Assert.True(Has(r, Yaku.KokushiMusou));
        Assert.True(r.IsYakuman);
    }

    [Fact]
    public void Ittsu_Closed()
    {
        WinResult r = Eval("123456789m234p11z", Ron("4p"));
        Assert.True(Has(r, Yaku.Ittsu));
        Assert.Equal(2, r.YakuHanTotal);
    }

    [Fact]
    public void SanshokuDoujun_Closed()
    {
        WinResult r = Eval("234m234p234s567m11z", Ron("4s"));
        Assert.True(Has(r, Yaku.SanshokuDoujun));
    }

    [Fact]
    public void Chanta_WithHonors()
    {
        // 123m 789p 999s 222z + 33z head; winds set so 2z is not yakuhai
        WinResult r = Eval("123m789p999s22233z", Ron("3z") with { SeatWind = 0, RoundWind = 0 });
        Assert.True(Has(r, Yaku.Chanta));
    }

    [Fact]
    public void Junchan_NoHonors()
    {
        WinResult r = Eval("123789m123p78999s", Ron("9s"));
        Assert.True(Has(r, Yaku.Junchan));
        Assert.False(Has(r, Yaku.Chanta));
    }

    [Fact]
    public void Chinitsu_Closed()
    {
        WinResult r = Eval("11223344556677m", Ron("1m") with { SeatWind = 1 });
        Assert.True(Has(r, Yaku.Chinitsu));
        Assert.True(r.YakuHanTotal >= 6);
    }

    [Fact]
    public void Honitsu_OpenIsTwoHan()
    {
        // open pon of 1z (not yakuhai: winds are south); concealed 234m 567m 999m + 22z
        var melds = new[] { new Meld(MeldKind.Pon, new[] { Tiles.Index("1z"), Tiles.Index("1z"), Tiles.Index("1z") }) };
        WinResult r = Eval("234567999m22z", Ron("7m") with { SeatWind = 1, RoundWind = 1 }, melds);
        Assert.True(Has(r, Yaku.Honitsu));
        Assert.Contains(r.Yaku, y => y.Yaku == Yaku.Honitsu && y.Han == 2);
    }

    [Fact]
    public void OpenPonOfDragon_IsYakuhai()
    {
        var melds = new[] { new Meld(MeldKind.Pon, new[] { Tiles.Index("5z"), Tiles.Index("5z"), Tiles.Index("5z") }) };
        WinResult r = Eval("234m567p23488s", Ron("4s"), melds);
        Assert.True(Has(r, Yaku.Haku));
    }

    [Fact]
    public void Ankan_KeepsHandClosed_ForMenzenTsumo()
    {
        var melds = new[] { new Meld(MeldKind.Kan, new[] { Tiles.Index("1z"), Tiles.Index("1z"), Tiles.Index("1z"), Tiles.Index("1z") }, IsOpen: false) };
        WinResult r = Eval("234m567p23488s", Tsumo("4s") with { SeatWind = 1, RoundWind = 1 }, melds);
        Assert.True(Has(r, Yaku.MenzenTsumo));
    }

    [Fact]
    public void RiichiIppatsuTsumo_Stack()
    {
        WinResult r = Eval("234567m234p56788s", Tsumo("7m") with { Riichi = true, Ippatsu = true });
        Assert.True(Has(r, Yaku.Riichi));
        Assert.True(Has(r, Yaku.Ippatsu));
        Assert.True(Has(r, Yaku.MenzenTsumo));
    }

    [Fact]
    public void Dora_CountsCopiesAndAka()
    {
        // indicator 4m -> dora 5m; hand holds 555m, one of them red (0m); 789p kills tanyao
        WinResult r = Eval("234055m789p23488s", Ron("4s") with
        {
            DoraIndicators = new[] { Tiles.Index("4m") },
        });
        Assert.Equal(4, r.DoraHan); // 3 copies of 5m + 1 aka
        Assert.Equal(0, r.YakuHanTotal); // dora alone never qualify
        Assert.False(r.HasYaku);
    }

    [Fact]
    public void UraDora_OnlyWithRiichi()
    {
        int[] indicator = [Tiles.Index("4m")];
        WinResult noRiichi = Eval("234555m789p23488s", Ron("4s") with { UraIndicators = indicator });
        WinResult riichi = Eval("234555m789p23488s", Ron("4s") with { UraIndicators = indicator, Riichi = true });
        Assert.Equal(0, noRiichi.DoraHan);
        Assert.Equal(3, riichi.DoraHan);
    }

    [Fact]
    public void DoraIndicator_WrapsWithinSuitAndHonors()
    {
        Assert.Equal(Tiles.Index("1m"), YakuCalculator.DoraFromIndicator(Tiles.Index("9m")));
        Assert.Equal(Tiles.Index("1z"), YakuCalculator.DoraFromIndicator(Tiles.Index("4z"))); // N -> E
        Assert.Equal(Tiles.Index("5z"), YakuCalculator.DoraFromIndicator(Tiles.Index("7z"))); // Chun -> Haku
    }

    [Fact]
    public void NotAWinningHand_ReturnsNull()
    {
        Assert.Null(YakuCalculator.Evaluate(Tiles.ParseCounts("123456m789p2345s9s"), null, Ron("5s")));
    }
}

public class DomanScoringTests
{
    [Fact]
    public void NonDealerTable()
    {
        Assert.Equal(1000, DomanScoring.Points(1, dealer: false));
        Assert.Equal(2000, DomanScoring.Points(2, dealer: false));
        Assert.Equal(8000, DomanScoring.Points(4, dealer: false));
        Assert.Equal(12000, DomanScoring.Points(6, dealer: false));
        Assert.Equal(16000, DomanScoring.Points(8, dealer: false));
        Assert.Equal(24000, DomanScoring.Points(11, dealer: false));
        Assert.Equal(32000, DomanScoring.Points(13, dealer: false));
    }

    [Fact]
    public void DealerGetsHalfAgain()
    {
        Assert.Equal(1500, DomanScoring.Points(1, dealer: true));
        Assert.Equal(48000, DomanScoring.Points(13, dealer: true));
    }

    [Fact]
    public void YakulessResultScoresZero()
    {
        var result = new WinResult { Yaku = [], YakuHanTotal = 0, DoraHan = 3, IsYakuman = false };
        Assert.Equal(0, DomanScoring.Points(result, dealer: false));
    }
}

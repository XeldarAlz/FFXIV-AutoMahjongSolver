namespace Mahjong.Engine.Tests;

public class YakumanTests
{
    private static WinContext Tsumo(string winTile) =>
        new(Tiles.Parse(winTile)[0], WinKind.Tsumo);

    private static IReadOnlyList<YakuHit> DetectAny(string notation, WinContext ctx,
                                                    IReadOnlyList<Meld>? melds = null)
    {
        var hand = Hand.FromNotation(notation, melds);
        return HandDecomposer.Enumerate(hand, ctx)
            .SelectMany(d => TestRules.Scorer.DetectYaku(d, ctx))
            .ToList();
    }

    [Fact]
    public void Shousuushii_three_wind_triplets_plus_one_wind_pair()
    {
        var hits = DetectAny("111m111z222z333z44z", Tsumo("4z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Shousuushii && h.IsYakuman);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Daisuushii);
    }

    [Fact]
    public void Daisuushii_four_wind_triplets_is_double_yakuman()
    {
        var hits = DetectAny("11m111z222z333z444z", Tsumo("1m"));
        var hit = hits.Single(h => h.Yaku == Yaku.Daisuushii);
        Assert.True(hit.IsYakuman);
        Assert.Equal(26, hit.Han);
    }

    [Fact]
    public void Chinroutou_all_terminals_is_yakuman()
    {
        var hits = DetectAny("111999m111999p11s", Tsumo("1s"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Chinroutou && h.IsYakuman);
    }

    [Fact]
    public void Ryuuiisou_only_green_tiles_is_yakuman()
    {
        var hits = DetectAny("222333444s888s66z", Tsumo("6z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Ryuuiisou && h.IsYakuman);
    }

    [Fact]
    public void Suukantsu_four_kans_is_yakuman()
    {
        var k1 = Meld.AnKan(Tile.FromId(0));
        var k2 = Meld.AnKan(Tile.FromId(9));
        var k3 = Meld.AnKan(Tile.FromId(18));
        var k4 = Meld.AnKan(Tile.FromId(27));
        var hand = Hand.FromNotation("44m", [k1, k2, k3, k4]);
        var hits = HandDecomposer.Enumerate(hand, Tsumo("4m"))
            .SelectMany(d => TestRules.Scorer.DetectYaku(d, Tsumo("4m")))
            .ToList();
        Assert.Contains(hits, h => h.Yaku == Yaku.Suukantsu && h.IsYakuman);
    }

    [Fact]
    public void Chuuren_poutou_is_yakuman()
    {
        var hits = DetectAny("11123455678999m", Tsumo("5m"));
        Assert.Contains(hits, h => h.Yaku == Yaku.ChuurenPoutou && h.IsYakuman);
    }

    [Fact]
    public void Pure_chuuren_is_double_yakuman()
    {
        var hand = Hand.FromNotation("11112345678999m");
        var ctx = Tsumo("1m");
        var result = TestRules.Scorer.Evaluate(hand, ctx);
        Assert.NotNull(result);
        var chuuren = result!.Yaku.First(h => h.Yaku == Yaku.ChuurenPoutou);
        Assert.True(chuuren.IsYakuman);
        Assert.Equal(26, chuuren.Han);
    }

    [Fact]
    public void Tenhou_dealer_first_draw_is_yakuman()
    {
        var ctx = new WinContext(
            Tiles.Parse("5z")[0], WinKind.Tsumo,
            IsTenhou: true, IsDealer: true);
        var hits = DetectAny("234m456p678s234s55z", ctx);
        Assert.Contains(hits, h => h.Yaku == Yaku.Tenhou && h.IsYakuman);
    }

    [Fact]
    public void Chihou_non_dealer_first_draw_is_yakuman()
    {
        var ctx = new WinContext(
            Tiles.Parse("5z")[0], WinKind.Tsumo,
            IsChihou: true);
        var hits = DetectAny("234m456p678s234s55z", ctx);
        Assert.Contains(hits, h => h.Yaku == Yaku.Chihou && h.IsYakuman);
    }

    [Fact]
    public void Kazoe_yakuman_thirteen_plus_han_from_normal_yaku()
    {
        var tier = TestRules.Tier(13, 30);
        Assert.Equal(8000, tier.BasePoints);
        Assert.Equal("yakuman", tier.Name);
    }
}

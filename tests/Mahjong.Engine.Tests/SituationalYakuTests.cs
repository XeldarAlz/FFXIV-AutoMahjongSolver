namespace Mahjong.Engine.Tests;

public class SituationalYakuTests
{
    private static IReadOnlyList<YakuHit> DetectBest(string notation, WinContext ctx,
                                                    IReadOnlyList<Meld>? melds = null)
    {
        var hand = Hand.FromNotation(notation, melds);
        var decomps = HandDecomposer.Enumerate(hand, ctx);
        return decomps
            .Select(d => TestRules.Scorer.DetectYaku(d, ctx))
            .Where(list => list.Count > 0)
            .OrderByDescending(list => list.Any(h => h.IsYakuman) ? 999 : list.Sum(h => h.Han))
            .FirstOrDefault() ?? [];
    }

    private static WinContext Base(string winTile, WinKind kind = WinKind.Tsumo) =>
        new(Tiles.Parse(winTile)[0], kind);

    [Fact]
    public void Double_riichi_reports_as_double_and_suppresses_single_riichi()
    {
        var ctx = Base("2m") with { IsRiichi = true, IsDoubleRiichi = true };
        var hits = DetectBest("234m456p789s234s99m", ctx);
        Assert.Contains(hits, h => h.Yaku == Yaku.DoubleRiichi && h.Han == 2);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Riichi);
    }

    [Fact]
    public void Ippatsu_requires_riichi_or_double_riichi()
    {
        var withRiichi = Base("2m") with { IsRiichi = true, IsIppatsu = true };
        var hits1 = DetectBest("234m456p789s234s99m", withRiichi);
        Assert.Contains(hits1, h => h.Yaku == Yaku.Ippatsu);

        var noRiichi = Base("2m") with { IsIppatsu = true };
        var hits2 = DetectBest("234m456p789s234s99m", noRiichi);
        Assert.DoesNotContain(hits2, h => h.Yaku == Yaku.Ippatsu);
    }

    [Fact]
    public void Rinshan_kaihou()
    {
        var ctx = Base("2m") with { IsRinshan = true };
        var hits = DetectBest("234m456p789s234s99m", ctx);
        Assert.Contains(hits, h => h.Yaku == Yaku.Rinshan);
    }

    [Fact]
    public void Chankan()
    {
        var ctx = Base("2m", WinKind.Ron) with { IsChankan = true };
        var hits = DetectBest("234m456p789s234s99m", ctx);
        Assert.Contains(hits, h => h.Yaku == Yaku.Chankan);
    }

    [Fact]
    public void Haitei_tsumo_last_wall_tile()
    {
        var ctx = Base("2m") with { IsHaitei = true };
        var hits = DetectBest("234m456p789s234s99m", ctx);
        Assert.Contains(hits, h => h.Yaku == Yaku.Haitei);
    }

    [Fact]
    public void Houtei_ron_last_discard()
    {
        var ctx = Base("2m", WinKind.Ron) with { IsHoutei = true };
        var hits = DetectBest("234m456p789s234s99m", ctx);
        Assert.Contains(hits, h => h.Yaku == Yaku.Houtei);
    }

    [Fact]
    public void Sanankou_three_concealed_triplets()
    {
        var hits = DetectBest("111m222p333s456s55z", Base("5z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Sanankou);
        Assert.DoesNotContain(hits, h => h.IsYakuman);
    }

    [Fact]
    public void Shanpon_ron_downgrades_suuankou_to_sanankou()
    {
        // Pins that a shanpon ron leaves the completed triplet open, so suuankou downgrades to sanankou.
        var hand = Hand.FromNotation("111m222p555z333s55s");
        var ctx = new WinContext(Tiles.Parse("3s")[0], WinKind.Ron);
        var result = TestRules.Scorer.Evaluate(hand, ctx);
        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Yaku, h => h.Yaku == Yaku.Suuankou);
        Assert.Contains(result.Yaku, h => h.Yaku == Yaku.Sanankou);
        Assert.Contains(result.Yaku, h => h.Yaku == Yaku.Toitoi);
    }

    [Fact]
    public void Sanshoku_doukou_three_triplets_same_number()
    {
        var hits = DetectBest("222m222p222s456p55z", Base("5z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.SanshokuDoukou);
    }

    [Fact]
    public void Shousangen_two_dragon_triplets_plus_dragon_pair()
    {
        var hits = DetectBest("123m456p555z666z77z", Base("7z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Shousangen);
        Assert.Contains(hits, h => h.Yaku == Yaku.YakuhaiHaku);
        Assert.Contains(hits, h => h.Yaku == Yaku.YakuhaiHatsu);
    }

    [Fact]
    public void Sankantsu_three_kans()
    {
        var k1 = Meld.AnKan(Tile.FromId(0));
        var k2 = Meld.AnKan(Tile.FromId(9));
        var k3 = Meld.AnKan(Tile.FromId(18));
        var hand = Hand.FromNotation("234s55m", [k1, k2, k3]);
        var hits = HandDecomposer.Enumerate(hand, Base("2s"))
            .SelectMany(d => TestRules.Scorer.DetectYaku(d, Base("2s"))).ToList();
        Assert.Contains(hits, h => h.Yaku == Yaku.Sankantsu);
    }

    [Fact]
    public void Honroutou_with_toitoi_all_terminal_honor_triplets_open_hand()
    {
        var pon = Meld.Pon(Tile.FromId(0), Tile.FromId(0), 2);
        var hand = Hand.FromNotation("999m111z222z99p", [pon]);
        var hits = HandDecomposer.Enumerate(hand, Base("9p"))
            .SelectMany(d => TestRules.Scorer.DetectYaku(d, Base("9p"))).ToList();
        Assert.Contains(hits, h => h.Yaku == Yaku.Honroutou);
        Assert.Contains(hits, h => h.Yaku == Yaku.Toitoi);
        Assert.DoesNotContain(hits, h => h.IsYakuman);
    }

    [Fact]
    public void Open_honitsu_is_two_han_not_three()
    {
        var pon = Meld.Pon(Tile.FromId(0), Tile.FromId(0), 2);
        var hand = Hand.FromNotation("234m567m111z22z", [pon]);
        var hits = HandDecomposer.Enumerate(hand, Base("2z"))
            .SelectMany(d => TestRules.Scorer.DetectYaku(d, Base("2z"))).ToList();
        var honitsu = hits.First(h => h.Yaku == Yaku.Honitsu);
        Assert.Equal(2, honitsu.Han);
    }

    [Fact]
    public void Open_chinitsu_is_five_han_not_six()
    {
        var pon = Meld.Pon(Tile.FromId(0), Tile.FromId(0), 2);
        var hand = Hand.FromNotation("234m567m789m22m", [pon]);
        var hits = HandDecomposer.Enumerate(hand, Base("2m"))
            .SelectMany(d => TestRules.Scorer.DetectYaku(d, Base("2m"))).ToList();
        var chinitsu = hits.First(h => h.Yaku == Yaku.Chinitsu);
        Assert.Equal(5, chinitsu.Han);
    }

    [Fact]
    public void Open_ittsu_is_one_han_not_two()
    {
        var chi = Meld.Chi(Tile.FromId(0), Tile.FromId(2), 3);
        var hand = Hand.FromNotation("456m789m345p55z", [chi]);
        var hits = HandDecomposer.Enumerate(hand, Base("5z"))
            .SelectMany(d => TestRules.Scorer.DetectYaku(d, Base("5z"))).ToList();
        var ittsu = hits.First(h => h.Yaku == Yaku.Ittsu);
        Assert.Equal(1, ittsu.Han);
    }
}

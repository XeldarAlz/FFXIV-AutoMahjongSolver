namespace Mahjong.Engine.Tests;

public class YakuTests
{
    private static IReadOnlyList<YakuHit> DetectBest(string notation, WinContext ctx, IReadOnlyList<Meld>? melds = null)
    {
        var hand = Hand.FromNotation(notation, melds);
        var decomps = HandDecomposer.Enumerate(hand, ctx);
        var best = decomps
            .Select(d => TestRules.Scorer.DetectYaku(d, ctx))
            .Where(list => list.Count > 0)
            .OrderByDescending(list => list.Any(h => h.IsYakuman) ? 999 : list.Sum(h => h.Han))
            .FirstOrDefault();
        return best ?? [];
    }

    private static WinContext Tsumo(string winTile, bool dealer = false) =>
        new(Tiles.Parse(winTile)[0], WinKind.Tsumo, IsDealer: dealer);

    private static WinContext Ron(string winTile, bool dealer = false) =>
        new(Tiles.Parse(winTile)[0], WinKind.Ron, IsDealer: dealer);

    [Fact]
    public void Pinfu_closed_all_runs_ryanmen_wait_menzen_tsumo()
    {
        var hits = DetectBest("234m456p678s234s55m", Tsumo("2m"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Pinfu);
        Assert.Contains(hits, h => h.Yaku == Yaku.MenzenTsumo);
    }

    [Fact]
    public void Pinfu_rejected_on_kanchan_wait()
    {
        var hits = DetectBest("123m456p678s234s55z", Tsumo("2m"));
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Pinfu);
    }

    [Fact]
    public void Tanyao_no_terminals_or_honors()
    {
        var hits = DetectBest("234m456p678s234s55s", Tsumo("2m"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Tanyao);
    }

    [Fact]
    public void Yakuhai_haku_triplet()
    {
        var hits = DetectBest("123m456p789s555z44m", Tsumo("4m"));
        Assert.Contains(hits, h => h.Yaku == Yaku.YakuhaiHaku);
    }

    [Fact]
    public void Yakuhai_round_wind_triplet()
    {
        var ctx = Tsumo("4m") with { RoundWindTileId = 27, SeatWindTileId = 28 };
        var hits = DetectBest("123m456p789s111z44m", ctx);
        Assert.Contains(hits, h => h.Yaku == Yaku.YakuhaiRound);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.YakuhaiSeat);
    }

    [Fact]
    public void Toitoi_all_triplets_open_hand()
    {
        var pon = Meld.Pon(Tile.FromId(0), Tile.FromId(0), fromSeat: 2);
        var hand = Hand.FromNotation("444p777s222z33z", [pon]);
        var decomps = HandDecomposer.Enumerate(hand, Tsumo("3z"));
        var hits = decomps.SelectMany(d => TestRules.Scorer.DetectYaku(d, Tsumo("3z"))).ToList();
        Assert.Contains(hits, h => h.Yaku == Yaku.Toitoi);
        Assert.DoesNotContain(hits, h => h.IsYakuman);
    }

    [Fact]
    public void Iipeiko_two_identical_runs()
    {
        var hits = DetectBest("112233m456p789s55z", Tsumo("5z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Iipeiko);
    }

    [Fact]
    public void Ryanpeikou_two_pairs_of_identical_runs()
    {
        var hits = DetectBest("112233m445566p55z", Tsumo("5z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Ryanpeikou);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Iipeiko);
    }

    [Fact]
    public void Chiitoitsu_seven_pairs()
    {
        var hits = DetectBest("1122m3344p5566s77z", Tsumo("7z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Chiitoitsu);
    }

    [Fact]
    public void Honitsu_one_suit_plus_honors()
    {
        var hits = DetectBest("123m456m789m111z22z", Tsumo("2z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Honitsu);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Chinitsu);
    }

    [Fact]
    public void Chinitsu_one_suit_no_honors()
    {
        var hits = DetectBest("123m456m789m11223m", Tsumo("3m"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Chinitsu);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Honitsu);
    }

    [Fact]
    public void Ittsu_one_to_nine_straight()
    {
        var hits = DetectBest("123456789m456p11z", Tsumo("1z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Ittsu);
    }

    [Fact]
    public void SanshokuDoujun_same_run_three_suits()
    {
        var hits = DetectBest("123m123p123s789s55z", Tsumo("5z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.SanshokuDoujun);
    }

    [Fact]
    public void Chanta_every_group_has_terminal_or_honor()
    {
        var hits = DetectBest("123m789p123s789s11z", Tsumo("1z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Chanta);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Junchan);
    }

    [Fact]
    public void Junchan_terminals_only_no_honors()
    {
        var hits = DetectBest("123m789p123s789s99m", Tsumo("9m"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Junchan);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Chanta);
    }

    [Fact]
    public void Kokushi_musou_is_yakuman()
    {
        var hits = DetectBest("19m19p19s1234567z1m", Tsumo("1m"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Kokushi && h.IsYakuman);
    }

    [Fact]
    public void Suuankou_four_concealed_triplets_is_yakuman()
    {
        var hits = DetectBest("111m222m333p444s55z", Tsumo("5z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Suuankou && h.IsYakuman);
    }

    [Fact]
    public void Daisangen_three_dragon_triplets()
    {
        var hits = DetectBest("123m44p555z666z777z", Tsumo("4p"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Daisangen && h.IsYakuman);
    }

    [Fact]
    public void Tsuuiisou_all_honors()
    {
        var hits = DetectBest("111z222z333z444z55z", Tsumo("5z"));
        Assert.Contains(hits, h => h.Yaku == Yaku.Tsuuiisou && h.IsYakuman);
    }

    [Fact]
    public void Yakuman_does_not_combine_with_normal_yaku()
    {
        var hits = DetectBest("19m19p19s1234567z1m", Tsumo("1m"));
        Assert.All(hits, h => Assert.True(h.IsYakuman));
    }

    [Fact]
    public void Riichi_plus_menzen_tsumo_plus_pinfu()
    {
        var ctx = Tsumo("2m") with { IsRiichi = true };
        var hits = DetectBest("234m456p678s234s55m", ctx);
        Assert.Contains(hits, h => h.Yaku == Yaku.Riichi);
        Assert.Contains(hits, h => h.Yaku == Yaku.MenzenTsumo);
        Assert.Contains(hits, h => h.Yaku == Yaku.Pinfu);
    }

    [Fact]
    public void Open_hand_breaks_menzen_yaku()
    {
        var pon = Meld.Pon(Tile.FromId(21), Tile.FromId(21), fromSeat: 2);
        var hand = Hand.FromNotation("234m456p678s55z", [pon]);
        var ctx = Tsumo("5z");
        var decomps = HandDecomposer.Enumerate(hand, ctx);
        var hits = decomps.SelectMany(d => TestRules.Scorer.DetectYaku(d, ctx)).ToList();

        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.MenzenTsumo);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Pinfu);
        Assert.DoesNotContain(hits, h => h.Yaku == Yaku.Iipeiko);
    }
}

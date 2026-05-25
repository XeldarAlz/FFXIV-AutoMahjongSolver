using Mahjong.Rules.YakuRules;
using Mahjong.Rules.YakuRules.Yakuman;

namespace Mahjong.Rules.Tests;

public class YakuRuleTests
{
    private static readonly Tile One = Tile.FromId(0);
    private static readonly Tile Two = Tile.FromId(1);
    private static readonly Tile Three = Tile.FromId(2);
    private static readonly Tile FiveZ = Tile.FromId(31);

    private static WinContext Tsumo(Tile w) => new(w, WinKind.Tsumo);

    private static Decomposition StandardDecomp(params Group[] groups)
        => new(DecompositionForm.Standard, groups, IsMenzen: true,
               WinningTile: One, WinningTileFromOpponent: false);

    [Fact]
    public void RiichiRule_fires_only_when_riichi_and_menzen()
    {
        var rule = new RiichiRule();
        var d = StandardDecomp();
        Assert.Empty(rule.Detect(d, Tsumo(One)));
        var ctxRiichi = Tsumo(One) with { IsRiichi = true };
        Assert.Single(rule.Detect(d, ctxRiichi));
    }

    [Fact]
    public void RiichiRule_silent_when_double_riichi_is_set()
    {
        var rule = new RiichiRule();
        var d = StandardDecomp();
        var ctx = Tsumo(One) with { IsRiichi = true, IsDoubleRiichi = true };
        Assert.Empty(rule.Detect(d, ctx));
    }

    [Fact]
    public void RiichiRule_definition_metadata()
    {
        var def = new RiichiRule().Definition;
        Assert.Equal(Yaku.Riichi, def.Id);
        Assert.Equal(1, def.ClosedHan);
        Assert.Equal(0, def.OpenHan);
        Assert.True(def.RequiresMenzen);
    }

    [Fact]
    public void TanyaoRule_rejects_terminal_in_any_group()
    {
        var rule = new TanyaoRule();
        var pair = new Group(GroupKind.Pair, FiveZ, IsOpen: false);
        var run234 = new Group(GroupKind.Run, Two, IsOpen: false);
        var d = StandardDecomp(pair, run234, run234, run234, run234);
        Assert.Empty(rule.Detect(d, Tsumo(One)));
    }

    [Fact]
    public void TanyaoRule_fires_on_all_simples()
    {
        var rule = new TanyaoRule();
        var pair = new Group(GroupKind.Pair, Two, IsOpen: false);
        var run = new Group(GroupKind.Run, Two, IsOpen: false);
        var d = StandardDecomp(pair, run, run, run, run);
        Assert.Single(rule.Detect(d, Tsumo(Two)));
    }

    [Fact]
    public void YakuhaiRule_emits_haku_hit_for_haku_triplet()
    {
        var rule = new YakuhaiRule();
        var triplet = new Group(GroupKind.Triplet, FiveZ, IsOpen: false);
        var pair = new Group(GroupKind.Pair, Two, IsOpen: false);
        var run = new Group(GroupKind.Run, Two, IsOpen: false);
        var d = StandardDecomp(triplet, run, run, run, pair);
        var hits = rule.Detect(d, Tsumo(One));
        Assert.Single(hits);
        Assert.Equal(Yaku.YakuhaiHaku, hits[0].Yaku);
    }

    [Fact]
    public void YakuhaiRule_double_east_emits_round_and_seat_when_both_match()
    {
        var rule = new YakuhaiRule();
        var east = Tile.FromId(27);
        var triplet = new Group(GroupKind.Triplet, east, IsOpen: false);
        var pair = new Group(GroupKind.Pair, Two, IsOpen: false);
        var run = new Group(GroupKind.Run, Two, IsOpen: false);
        var d = StandardDecomp(triplet, run, run, run, pair);

        var ctx = Tsumo(One) with { RoundWindTileId = 27, SeatWindTileId = 27 };
        var hits = rule.Detect(d, ctx);

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Yaku == Yaku.YakuhaiRound);
        Assert.Contains(hits, h => h.Yaku == Yaku.YakuhaiSeat);
    }

    [Fact]
    public void HaiteiRule_fires_on_context_flag_only()
    {
        var rule = new HaiteiRule();
        var d = StandardDecomp();
        Assert.Empty(rule.Detect(d, Tsumo(One)));

        var ctx = Tsumo(One) with { IsHaitei = true };
        Assert.Single(rule.Detect(d, ctx));
    }

    [Fact]
    public void TenhouRule_definition_marks_as_yakuman_and_menzen()
    {
        var def = new TenhouRule().Definition;
        Assert.True(def.IsYakuman);
        Assert.True(def.RequiresMenzen);
        Assert.Equal(13, def.ClosedHan);
    }

    [Fact]
    public void KokushiRule_fires_only_on_kokushi_form()
    {
        var rule = new KokushiRule();
        var d = StandardDecomp();
        Assert.Empty(rule.Detect(d, Tsumo(One)));

        var kokushi = new Decomposition(DecompositionForm.Kokushi, [], IsMenzen: true,
            WinningTile: One, WinningTileFromOpponent: false);
        var hits = rule.Detect(kokushi, Tsumo(One));
        Assert.Single(hits);
        Assert.True(hits[0].IsYakuman);
    }
}

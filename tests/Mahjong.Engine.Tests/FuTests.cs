namespace Mahjong.Engine.Tests;

public class FuTests
{
    private static Decomposition Decomp(string notation, WinContext ctx,
                                        IReadOnlyList<Meld>? melds = null)
    {
        var hand = Hand.FromNotation(notation, melds);
        return HandDecomposer.Enumerate(hand, ctx)
            .First(d => d.Form == DecompositionForm.Standard);
    }

    private static WinContext Tsumo(string winTile) => new(Tiles.Parse(winTile)[0], WinKind.Tsumo);
    private static WinContext Ron(string winTile) => new(Tiles.Parse(winTile)[0], WinKind.Ron);

    [Fact]
    public void Chiitoitsu_is_25_fu_flat()
    {
        var hand = Hand.FromNotation("1122m3344p5566s77z");
        var ctx = Ron("7z");
        var d = HandDecomposer.Enumerate(hand, ctx)
            .Single(x => x.Form == DecompositionForm.Chiitoitsu);
        Assert.Equal(25, TestRules.Fu(d, ctx));
    }

    [Fact]
    public void Pinfu_tsumo_is_20_fu_flat()
    {
        var ctx = Tsumo("2m");
        var d = Decomp("234m456p789s234s99m", ctx);
        Assert.Equal(20, TestRules.Fu(d, ctx, TestRules.WithPinfu()));
    }

    [Fact]
    public void Pinfu_ron_is_30_fu_flat()
    {
        var ctx = Ron("2m");
        var d = Decomp("234m456p789s234s99m", ctx);
        Assert.Equal(30, TestRules.Fu(d, ctx, TestRules.WithPinfu()));
    }

    [Fact]
    public void Menzen_ron_adds_ten_fu()
    {
        var ctx = Ron("2m");
        var d = Decomp("234m456p678s111z55p", ctx);
        int fu = TestRules.Fu(d, ctx);
        Assert.Equal(40, fu);
    }

    [Fact]
    public void Closed_triplet_simple_is_four_fu()
    {
        var ctx = Tsumo("5z");
        var d = Decomp("234m222p789s234s55z", ctx);
        int fu = TestRules.Fu(d, ctx);
        Assert.Equal(30, fu);
    }

    [Fact]
    public void Closed_kan_terminal_is_32_fu()
    {
        var ankan = Meld.AnKan(Tile.FromId(0));
        var ctx = Tsumo("4m");
        var hand = Hand.FromNotation("234m456p789s44m", [ankan]);
        var d = HandDecomposer.Enumerate(hand, ctx).First(x => x.Form == DecompositionForm.Standard);
        int fu = TestRules.Fu(d, ctx);
        Assert.Equal(60, fu);
    }

    [Fact]
    public void Tanki_wait_adds_two_fu()
    {
        var ctx = Tsumo("5z");
        var d = Decomp("234m456p789s111z55z", ctx);
        int fu = TestRules.Fu(d, ctx);
        Assert.Equal(40, fu);
    }

    [Fact]
    public void Kanchan_wait_adds_two_fu()
    {
        var ctx = Tsumo("5m");
        var d = Decomp("456m789m234p234s55z", ctx);
        int fu = TestRules.Fu(d, ctx);
        Assert.Equal(30, fu);
    }

    [Fact]
    public void Ryanmen_wait_no_bonus_fu()
    {
        var ctx = Tsumo("2m");
        var d = Decomp("234m456p789s234s55z", ctx);
        int fu = TestRules.Fu(d, ctx);
        Assert.Equal(30, fu);
    }

    [Fact]
    public void Double_wind_pair_doubles_yakuhai_fu()
    {
        var ctx = new WinContext(Tiles.Parse("2m")[0], WinKind.Tsumo,
                                  RoundWindTileId: 27, SeatWindTileId: 27);
        var hand = Hand.FromNotation("234m456p789s234s11z");
        var d = HandDecomposer.Enumerate(hand, ctx)
            .First(x => x.Form == DecompositionForm.Standard);
        int fu = TestRules.Fu(d, ctx);
        Assert.Equal(30, fu);
    }
}

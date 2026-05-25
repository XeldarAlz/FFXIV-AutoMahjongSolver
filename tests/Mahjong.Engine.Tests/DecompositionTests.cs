using Xunit;

namespace Mahjong.Engine.Tests;

public class DecompositionTests
{
    private static WinContext Ctx(string winTile, WinKind kind = WinKind.Tsumo, bool dealer = false)
        => new(Tiles.Parse(winTile)[0], kind, IsDealer: dealer);

    [Fact]
    public void Chiitoitsu_seven_pairs_produces_chitoi_decomposition()
    {
        var hand = Hand.FromNotation("1122m3344p5566s77z");
        var decomps = HandDecomposer.Enumerate(hand, Ctx("7z"));
        Assert.Contains(decomps, d => d.Form == DecompositionForm.Chiitoitsu);
        var chitoi = decomps.First(d => d.Form == DecompositionForm.Chiitoitsu);
        Assert.Equal(7, chitoi.Groups.Count);
        Assert.All(chitoi.Groups, g => Assert.Equal(GroupKind.Pair, g.Kind));
        Assert.True(chitoi.IsMenzen);
    }

    [Fact]
    public void Kokushi_14_tile_produces_kokushi_decomposition()
    {
        var hand = Hand.FromNotation("19m19p19s1234567z1m");
        var decomps = HandDecomposer.Enumerate(hand, Ctx("1m"));
        Assert.Contains(decomps, d => d.Form == DecompositionForm.Kokushi);
    }

    [Fact]
    public void Standard_four_runs_plus_pair()
    {
        var hand = Hand.FromNotation("123456789m11123p");
        var decomps = HandDecomposer.Enumerate(hand, Ctx("3p"));
        var std = decomps.Where(d => d.Form == DecompositionForm.Standard).ToList();
        Assert.NotEmpty(std);
        var first = std.First();
        Assert.Equal(5, first.Groups.Count);
        Assert.Equal(4, first.Groups.Count(g => g.Kind != GroupKind.Pair));
        Assert.Single(first.Groups, g => g.Kind == GroupKind.Pair);
        Assert.True(first.IsMenzen);
    }

    [Fact]
    public void Standard_with_triplet_and_runs_and_pair()
    {
        var hand = Hand.FromNotation("123m456p789s11122z");
        var decomps = HandDecomposer.Enumerate(hand, Ctx("1m"));
        var std = decomps.Single(d => d.Form == DecompositionForm.Standard);
        Assert.Equal(3, std.Groups.Count(g => g.Kind == GroupKind.Run));
        Assert.Equal(1, std.Groups.Count(g => g.Kind == GroupKind.Triplet));
        Assert.Equal(1, std.Groups.Count(g => g.Kind == GroupKind.Pair));
    }

    [Fact]
    public void Identical_triple_runs_yield_two_decompositions_triplet_vs_runs()
    {
        var hand = Hand.FromNotation("111222333m456p99s");
        var decomps = HandDecomposer.Enumerate(hand, Ctx("9s")).ToList();
        var std = decomps.Where(d => d.Form == DecompositionForm.Standard).ToList();

        Assert.True(std.Count >= 2,
            $"expected at least 2 standard decompositions, got {std.Count}");

        Assert.Contains(std, d => d.Groups.Count(g => g.Kind == GroupKind.Triplet) == 3);
        Assert.Contains(std, d => d.Groups.Count(g => g.Kind == GroupKind.Run) == 4);
    }

    [Fact]
    public void Winning_tile_attributed_to_a_group()
    {
        var hand = Hand.FromNotation("123m456p789s11122z");
        var ctx = Ctx("2z", WinKind.Ron);
        var std = HandDecomposer.Enumerate(hand, ctx).Single(d => d.Form == DecompositionForm.Standard);
        Assert.Contains(std.Groups, g => g.IsCompletedByWinningTile);
    }

    [Fact]
    public void Open_hand_has_only_standard_decomposition()
    {
        var pon = Meld.Pon(Tile.FromId(4), Tile.FromId(4), fromSeat: 2);
        var hand = Hand.FromNotation("123m456p789s11z", [pon]);
        var ctx = Ctx("1z");
        var decomps = HandDecomposer.Enumerate(hand, ctx);
        Assert.All(decomps, d => Assert.Equal(DecompositionForm.Standard, d.Form));
        Assert.NotEmpty(decomps);
        Assert.False(decomps[0].IsMenzen);
    }

    [Fact]
    public void Hand_with_only_ankan_is_still_menzen()
    {
        var ankan = Meld.AnKan(Tile.FromId(4));
        var hand = Hand.FromNotation("123m456p789s11z", [ankan]);
        var ctx = Ctx("1z");
        var decomps = HandDecomposer.Enumerate(hand, ctx);
        Assert.NotEmpty(decomps);
        Assert.All(decomps, d => Assert.True(d.IsMenzen));
    }

    [Fact]
    public void Group_contains_terminal_correctly()
    {
        var run123 = new Group(GroupKind.Run, Tile.FromId(0), false);
        var run789 = new Group(GroupKind.Run, Tile.FromId(6), false);
        var run456 = new Group(GroupKind.Run, Tile.FromId(3), false);
        var honor = new Group(GroupKind.Triplet, Tile.FromId(27), false);

        Assert.True(run123.ContainsTerminalOrHonor);
        Assert.True(run789.ContainsTerminalOrHonor);
        Assert.False(run456.ContainsTerminalOrHonor);
        Assert.True(honor.ContainsTerminalOrHonor);

        Assert.False(run123.AllTerminalOrHonor);
        Assert.True(honor.AllTerminalOrHonor);
    }
}

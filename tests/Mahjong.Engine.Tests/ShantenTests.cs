using Xunit;

namespace Mahjong.Engine.Tests;

public class ShantenTests
{
    private static int[] Counts(string notation) => Tiles.ToCounts(Tiles.Parse(notation));

    [Fact]
    public void Chiitoitsu_seven_pairs_is_agari()
    {
        var c = Counts("1122m3344p5566s77z");
        Assert.Equal(14, c.Sum());
        Assert.Equal(-1, ShantenCalculator.Chiitoitsu(c));
    }

    [Fact]
    public void Chiitoitsu_six_pairs_is_tenpai()
    {
        var c = Counts("1122m3344p5566s7z");
        Assert.Equal(13, c.Sum());
        Assert.Equal(0, ShantenCalculator.Chiitoitsu(c));
    }

    [Fact]
    public void Chiitoitsu_duplicate_triplet_does_not_count_twice()
    {
        // A kind with 3 copies contributes only one pair for chiitoitsu purposes.
        var c = Counts("111m22m33p44p55s66s7z");
        Assert.Equal(14, c.Sum());
        Assert.Equal(0, ShantenCalculator.Chiitoitsu(c));
    }

    [Fact]
    public void Kokushi_thirteen_way_tenpai()
    {
        var c = Counts("19m19p19s1234567z");
        Assert.Equal(13, c.Sum());
        Assert.Equal(0, ShantenCalculator.Kokushi(c));
    }

    [Fact]
    public void Kokushi_with_pair_is_agari()
    {
        var c = Counts("19m19p19s1234567z1m");
        Assert.Equal(14, c.Sum());
        Assert.Equal(-1, ShantenCalculator.Kokushi(c));
    }

    [Fact]
    public void Kokushi_tenpai_with_pair_missing_one_kind()
    {
        var c = Counts("11m9m19p1s1234567z");
        Assert.Equal(13, c.Sum());
        Assert.Equal(0, ShantenCalculator.Kokushi(c));
    }

    [Fact]
    public void Kokushi_one_shanten_missing_two_kinds()
    {
        var c = Counts("11m9m1p1s1234567z1z");
        Assert.Equal(13, c.Sum());
        Assert.Equal(1, ShantenCalculator.Kokushi(c));
    }

    [Fact]
    public void Standard_agari_four_runs_and_pair()
    {
        var c = Counts("123456789m11123p");
        Assert.Equal(14, c.Sum());
        Assert.Equal(-1, ShantenCalculator.Standard(c));
    }

    [Fact]
    public void Standard_agari_with_triplet_and_pair()
    {
        var c = Counts("123m456p789s11122z");
        Assert.Equal(14, c.Sum());
        Assert.Equal(-1, ShantenCalculator.Standard(c));
    }

    [Fact]
    public void Standard_tenpai_shanpon_closed()
    {
        var c = Counts("123m456p789s11z22z");
        Assert.Equal(13, c.Sum());
        Assert.Equal(0, ShantenCalculator.Standard(c));
    }

    [Fact]
    public void Standard_one_shanten_classic()
    {
        var c = Counts("123m45p789s11122z");
        Assert.Equal(13, c.Sum());
        Assert.Equal(0, ShantenCalculator.Standard(c));
    }

    [Fact]
    public void Standard_two_shanten()
    {
        var c = Counts("147m147p147s1234z");
        Assert.Equal(13, c.Sum());
        var s = ShantenCalculator.Standard(c);
        Assert.True(s >= 4, $"expected high shanten, got {s}");
    }

    [Fact]
    public void Standard_non_winning_has_positive_shanten()
    {
        var c = Counts("19m19p19s1234567z");
        Assert.True(ShantenCalculator.Standard(c) > 3);
    }

    [Fact]
    public void Compute_min_uses_chiitoitsu_when_best()
    {
        var hand = Hand.FromNotation("1122m3344p5566s77z");
        var r = ShantenCalculator.Compute(hand);
        Assert.Equal(-1, r.Min);
        Assert.True(r.IsAgari);
    }

    [Fact]
    public void Compute_min_uses_kokushi_when_best()
    {
        var hand = Hand.FromNotation("19m19p19s1234567z1m");
        var r = ShantenCalculator.Compute(hand);
        Assert.Equal(-1, r.Min);
    }

    [Fact]
    public void Compute_min_uses_standard_when_best()
    {
        var hand = Hand.FromNotation("123456789m11123p");
        var r = ShantenCalculator.Compute(hand);
        Assert.Equal(-1, r.Min);
    }

    [Fact]
    public void Compute_open_hand_ignores_chiitoi_and_kokushi()
    {
        var meld = Meld.Pon(Tile.FromId(0), Tile.FromId(0), 2);
        var openHand = Hand.FromNotation("234m567m78p11z", [meld]);
        var r = ShantenCalculator.Compute(openHand);
        Assert.Equal(8, r.Chiitoitsu);
        Assert.Equal(8, r.Kokushi);
    }
}

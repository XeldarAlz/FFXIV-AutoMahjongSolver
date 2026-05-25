namespace Mahjong.Engine.Tests;

public class DoraTests
{
    [Fact]
    public void Dora_indicator_cycles_next_tile_in_suit()
    {
        var hand = Hand.FromNotation("234m456p789s234s99m");
        var baseCtx = new WinContext(Tiles.Parse("2m")[0], WinKind.Tsumo);
        var withDora = baseCtx with { DoraIndicators = [Tiles.Parse("1m")[0]] };

        var r0 = TestRules.Scorer.Evaluate(hand, baseCtx)!;
        var r1 = TestRules.Scorer.Evaluate(hand, withDora)!;
        Assert.Equal(r0.Han + 1, r1.Han);
    }

    [Fact]
    public void Dora_cycles_nine_back_to_one()
    {
        var hand = Hand.FromNotation("234m456p123s123s99m");
        var baseCtx = new WinContext(Tiles.Parse("2m")[0], WinKind.Tsumo);
        var withDora = baseCtx with { DoraIndicators = [Tiles.Parse("9s")[0]] };

        var r0 = TestRules.Scorer.Evaluate(hand, baseCtx)!;
        var r1 = TestRules.Scorer.Evaluate(hand, withDora)!;
        Assert.Equal(r0.Han + 2, r1.Han);
    }

    [Fact]
    public void Wind_indicator_cycles_east_south_west_north_east()
    {
        var hand = Hand.FromNotation("234m456p789s234s11z");
        var baseCtx = new WinContext(Tiles.Parse("2m")[0], WinKind.Tsumo,
                                      RoundWindTileId: 28, SeatWindTileId: 28);
        var withDora = baseCtx with { DoraIndicators = [Tiles.Parse("4z")[0]] };

        var r0 = TestRules.Scorer.Evaluate(hand, baseCtx)!;
        var r1 = TestRules.Scorer.Evaluate(hand, withDora)!;
        Assert.Equal(r0.Han + 2, r1.Han);
    }

    [Fact]
    public void Dragon_indicator_cycles_haku_hatsu_chun_haku()
    {
        var hand = Hand.FromNotation("234m456p789s234s55z");
        var baseCtx = new WinContext(Tiles.Parse("2m")[0], WinKind.Tsumo);
        var withDora = baseCtx with { DoraIndicators = [Tiles.Parse("7z")[0]] };

        var r0 = TestRules.Scorer.Evaluate(hand, baseCtx)!;
        var r1 = TestRules.Scorer.Evaluate(hand, withDora)!;
        Assert.Equal(r0.Han + 2, r1.Han);
    }

    [Fact]
    public void Ura_dora_counted_only_with_riichi_and_menzen()
    {
        var hand = Hand.FromNotation("234m456p789s234s99m");
        var withoutRiichi = new WinContext(
            Tiles.Parse("2m")[0], WinKind.Tsumo,
            UraDoraIndicators: [Tiles.Parse("1m")[0]]);
        var withRiichi = withoutRiichi with { IsRiichi = true };

        var r1 = TestRules.Scorer.Evaluate(hand, withoutRiichi)!;
        var r2 = TestRules.Scorer.Evaluate(hand, withRiichi)!;

        Assert.Equal(r1.Han + 2, r2.Han);
    }

    [Fact]
    public void AkaDora_adds_one_han_per_red_in_closed_hand()
    {
        var hand = Hand.FromNotation("234m456p789s234s99m");
        var baseCtx = new WinContext(Tiles.Parse("2m")[0], WinKind.Tsumo);
        var withOneRed = baseCtx with { AkaDora = 1 };
        var withTwoReds = baseCtx with { AkaDora = 2 };

        var r0 = TestRules.Scorer.Evaluate(hand, baseCtx)!;
        var r1 = TestRules.Scorer.Evaluate(hand, withOneRed)!;
        var r2 = TestRules.Scorer.Evaluate(hand, withTwoReds)!;

        Assert.Equal(r0.Han + 1, r1.Han);
        Assert.Equal(r0.Han + 2, r2.Han);
    }

    [Fact]
    public void AkaDora_stacks_with_regular_dora()
    {
        var hand = Hand.FromNotation("234m456p789s234s99m");
        var baseCtx = new WinContext(Tiles.Parse("2m")[0], WinKind.Tsumo);
        var withBoth = baseCtx with
        {
            DoraIndicators = [Tiles.Parse("1m")[0]],
            AkaDora = 1,
        };

        var r0 = TestRules.Scorer.Evaluate(hand, baseCtx)!;
        var r1 = TestRules.Scorer.Evaluate(hand, withBoth)!;
        Assert.Equal(r0.Han + 2, r1.Han);
    }

    [Fact]
    public void AkaDora_combines_closed_and_open_meld_reds()
    {
        var hand = Hand.FromNotation("234m456p789s234s99m");
        var baseCtx = new WinContext(Tiles.Parse("2m")[0], WinKind.Tsumo);
        var twoReds = baseCtx with { AkaDora = 2 };

        var r0 = TestRules.Scorer.Evaluate(hand, baseCtx)!;
        var r1 = TestRules.Scorer.Evaluate(hand, twoReds)!;
        Assert.Equal(r0.Han + 2, r1.Han);
    }
}

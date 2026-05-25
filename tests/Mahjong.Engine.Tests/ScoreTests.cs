namespace Mahjong.Engine.Tests;

public class ScoreTests
{
    [Fact]
    public void Non_dealer_mangan_ron_is_8000()
    {
        var tier = TestRules.Tier(han: 5, fu: 30);
        Assert.Equal(2000, tier.BasePoints);
        Assert.Equal("mangan", tier.Name);
        var pay = TestRules.Pay(tier, isDealer: false, WinKind.Ron);
        Assert.Equal(8000, pay.RonTotal);
        Assert.Equal(8000, pay.Total);
    }

    [Fact]
    public void Dealer_mangan_ron_is_12000()
    {
        var tier = TestRules.Tier(5, 30);
        var pay = TestRules.Pay(tier, isDealer: true, WinKind.Ron);
        Assert.Equal(12000, pay.RonTotal);
    }

    [Fact]
    public void Non_dealer_tsumo_30fu_3han_is_2000_total()
    {
        var tier = TestRules.Tier(3, 30);
        Assert.Equal(960, tier.BasePoints);
        var pay = TestRules.Pay(tier, isDealer: false, WinKind.Tsumo);
        Assert.Equal(2000, pay.DealerPay);
        Assert.Equal(1000, pay.NonDealerPay);
        Assert.Equal(4000, pay.Total);
    }

    [Fact]
    public void Dealer_tsumo_30fu_3han_is_6000_total()
    {
        var tier = TestRules.Tier(3, 30);
        var pay = TestRules.Pay(tier, isDealer: true, WinKind.Tsumo);
        Assert.Equal(2000, pay.NonDealerPay);
        Assert.Equal(6000, pay.Total);
    }

    [Fact]
    public void Yakuman_non_dealer_ron_is_32000()
    {
        var tier = TestRules.Tier(13, 40, isYakuman: true);
        Assert.Equal(8000, tier.BasePoints);
        Assert.Equal("yakuman", tier.Name);
        var pay = TestRules.Pay(tier, isDealer: false, WinKind.Ron);
        Assert.Equal(32000, pay.RonTotal);
    }

    [Fact]
    public void Yakuman_dealer_tsumo_is_48000()
    {
        var tier = TestRules.Tier(13, 40, isYakuman: true);
        var pay = TestRules.Pay(tier, isDealer: true, WinKind.Tsumo);
        Assert.Equal(16000, pay.NonDealerPay);
        Assert.Equal(48000, pay.Total);
    }

    [Fact]
    public void Double_yakuman_non_dealer_ron_is_64000()
    {
        var tier = TestRules.Tier(26, 40, isYakuman: true);
        Assert.Equal(16000, tier.BasePoints);
        var pay = TestRules.Pay(tier, isDealer: false, WinKind.Ron);
        Assert.Equal(64000, pay.RonTotal);
    }

    [Fact]
    public void Haneman_6han_2han_value()
    {
        var tier = TestRules.Tier(6, 30);
        Assert.Equal(3000, tier.BasePoints);
        Assert.Equal("haneman", tier.Name);
    }

    [Fact]
    public void Low_han_low_fu_non_mangan()
    {
        var tier = TestRules.Tier(1, 20);
        Assert.Equal(160, tier.BasePoints);
        Assert.Equal(string.Empty, tier.Name);
    }

    [Fact]
    public void Evaluator_detects_riichi_pinfu_tsumo_and_pays_correct_amount()
    {
        var hand = Hand.FromNotation("234m456p789s234s99m");
        var ctx = new WinContext(
            WinningTile: Tiles.Parse("2m")[0],
            Kind: WinKind.Tsumo,
            IsRiichi: true,
            IsDealer: false);

        var result = TestRules.Scorer.Evaluate(hand, ctx);
        Assert.NotNull(result);
        Assert.Contains(result!.Yaku, h => h.Yaku == Yaku.Riichi);
        Assert.Contains(result.Yaku, h => h.Yaku == Yaku.MenzenTsumo);
        Assert.Contains(result.Yaku, h => h.Yaku == Yaku.Pinfu);
        Assert.Equal(3, result.Han);
        Assert.Equal(20, result.Fu);
        Assert.Equal(1300, result.Payments.DealerPay);
        Assert.Equal(700, result.Payments.NonDealerPay);
        Assert.Equal(2700, result.Payments.Total);
    }

    [Fact]
    public void Evaluator_returns_null_when_no_yaku()
    {
        var pon = Meld.Pon(Tile.FromId(3), Tile.FromId(3), fromSeat: 2);
        var hand = Hand.FromNotation("123m456p789s22s", [pon]);
        var ctx = new WinContext(Tiles.Parse("2s")[0], WinKind.Ron, IsDealer: false);
        var result = TestRules.Scorer.Evaluate(hand, ctx);
        Assert.Null(result);
    }
}

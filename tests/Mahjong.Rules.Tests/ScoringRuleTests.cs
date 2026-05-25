namespace Mahjong.Rules.Tests;

public class ScoringRuleTests
{
    private static readonly StandardScoringRule Rule = new();

    [Theory]
    [InlineData(1, 30, "")]
    [InlineData(4, 30, "")]
    [InlineData(5, 30, "mangan")]
    [InlineData(6, 30, "haneman")]
    [InlineData(7, 30, "haneman")]
    [InlineData(8, 30, "baiman")]
    [InlineData(10, 30, "baiman")]
    [InlineData(11, 30, "sanbaiman")]
    [InlineData(12, 30, "sanbaiman")]
    [InlineData(13, 30, "yakuman")]
    [InlineData(20, 30, "yakuman")]
    public void Han_thresholds_map_to_expected_tier(int han, int fu, string tierName)
    {
        var tier = Rule.ResolveTier(han, fu, isYakuman: false);
        Assert.Equal(tierName, tier.Name);
    }

    [Fact]
    public void Low_han_low_fu_caps_at_mangan()
    {
        var tier = Rule.ResolveTier(han: 4, fu: 60, isYakuman: false);
        Assert.Equal(2000, tier.BasePoints);
        Assert.Equal("mangan", tier.Name);
    }

    [Fact]
    public void Yakuman_with_double_multiplier()
    {
        var tier = Rule.ResolveTier(han: 26, fu: 30, isYakuman: true);
        Assert.Equal(16000, tier.BasePoints);
        Assert.Equal("yakuman", tier.Name);
    }

    [Fact]
    public void Non_dealer_ron_pays_base_times_four_rounded_up()
    {
        var tier = new ScoringTier("test", 960);
        var pay = Rule.Pay(tier, isDealer: false, WinKind.Ron);
        Assert.Equal(0, pay.DealerPay);
        Assert.Equal(0, pay.NonDealerPay);
        Assert.Equal(3900, pay.RonTotal);
    }

    [Fact]
    public void Dealer_ron_pays_base_times_six_rounded_up()
    {
        var tier = new ScoringTier("test", 960);
        var pay = Rule.Pay(tier, isDealer: true, WinKind.Ron);
        Assert.Equal(5800, pay.RonTotal);
    }

    [Fact]
    public void Non_dealer_tsumo_splits_two_one_one()
    {
        var tier = new ScoringTier("test", 1000);
        var pay = Rule.Pay(tier, isDealer: false, WinKind.Tsumo);
        Assert.Equal(2000, pay.DealerPay);
        Assert.Equal(1000, pay.NonDealerPay);
        Assert.Equal(4000, pay.Total);
    }

    [Fact]
    public void Dealer_tsumo_charges_each_non_dealer_two()
    {
        var tier = new ScoringTier("test", 1000);
        var pay = Rule.Pay(tier, isDealer: true, WinKind.Tsumo);
        Assert.Equal(0, pay.DealerPay);
        Assert.Equal(2000, pay.NonDealerPay);
        Assert.Equal(6000, pay.Total);
    }
}

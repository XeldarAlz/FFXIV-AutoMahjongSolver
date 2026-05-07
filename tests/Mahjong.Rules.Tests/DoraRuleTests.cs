namespace Mahjong.Rules.Tests;

public class DoraRuleTests
{
    private static readonly StandardDoraRule Rule = new();

    [Theory]
    [InlineData(0, 1)]   // 1m → 2m
    [InlineData(7, 8)]   // 8m → 9m
    [InlineData(8, 0)]   // 9m → 1m (suit wraparound)
    [InlineData(9, 10)]  // 1p → 2p
    [InlineData(17, 9)]  // 9p → 1p
    [InlineData(26, 18)] // 9s → 1s
    public void Suited_tiles_cycle_within_suit(int indicatorId, int doraId)
    {
        Assert.Equal(doraId, Rule.Next(Tile.FromId(indicatorId)).Id);
    }

    [Theory]
    [InlineData(27, 28)] // East → South
    [InlineData(28, 29)] // South → West
    [InlineData(29, 30)] // West → North
    [InlineData(30, 27)] // North → East (wind wraparound)
    public void Winds_cycle_through_four(int indicatorId, int doraId)
    {
        Assert.Equal(doraId, Rule.Next(Tile.FromId(indicatorId)).Id);
    }

    [Theory]
    [InlineData(31, 32)] // Haku → Hatsu
    [InlineData(32, 33)] // Hatsu → Chun
    [InlineData(33, 31)] // Chun → Haku (dragon wraparound)
    public void Dragons_cycle_through_three(int indicatorId, int doraId)
    {
        Assert.Equal(doraId, Rule.Next(Tile.FromId(indicatorId)).Id);
    }
}

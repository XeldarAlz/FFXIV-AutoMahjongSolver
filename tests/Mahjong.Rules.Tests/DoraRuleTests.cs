namespace Mahjong.Rules.Tests;

public class DoraRuleTests
{
    private static readonly StandardDoraRule Rule = new();

    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 8)]
    [InlineData(8, 0)]
    [InlineData(9, 10)]
    [InlineData(17, 9)]
    [InlineData(26, 18)]
    public void Suited_tiles_cycle_within_suit(int indicatorId, int doraId)
    {
        Assert.Equal(doraId, Rule.Next(Tile.FromId(indicatorId)).Id);
    }

    [Theory]
    [InlineData(27, 28)]
    [InlineData(28, 29)]
    [InlineData(29, 30)]
    [InlineData(30, 27)]
    public void Winds_cycle_through_four(int indicatorId, int doraId)
    {
        Assert.Equal(doraId, Rule.Next(Tile.FromId(indicatorId)).Id);
    }

    [Theory]
    [InlineData(31, 32)]
    [InlineData(32, 33)]
    [InlineData(33, 31)]
    public void Dragons_cycle_through_three(int indicatorId, int doraId)
    {
        Assert.Equal(doraId, Rule.Next(Tile.FromId(indicatorId)).Id);
    }
}

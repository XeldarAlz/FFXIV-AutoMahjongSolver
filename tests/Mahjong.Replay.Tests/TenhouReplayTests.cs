using Mahjong.Policy.Efficiency;

namespace Mahjong.Replay.Tests;

public class TenhouReplayTests
{
    private const string ThreeTurnSyntheticKyoku = """
{
  "log": [
    [
      [0, 0, 0],
      [25000, 25000, 25000, 25000],
      [0],
      [],
      [0,4,8,12,16,20,24,36,40,44,48,52,56],
      [60, 64, 68],
      [60, 64, 68],
      [100,104,108,112,116,120,124,128,132,1,2,3,5],
      [],
      [],
      [6,7,9,10,11,13,14,15,17,18,19,21,22],
      [],
      [],
      [23,25,26,27,28,29,30,31,32,33,34,35,37],
      [],
      []
    ]
  ]
}
""";

    [Fact]
    public void Replay_returns_one_decision_per_recorded_discard()
    {
        var kyokus = TenhouLog.ParseDocument(ThreeTurnSyntheticKyoku);
        var policy = new EfficiencyPolicy();
        var result = TenhouReplay.ReplaySeat(kyokus[0], policy, seat: 0);

        Assert.Equal(3, result.TotalDecisions);
        Assert.Equal(3, result.Decisions.Length);
        Assert.InRange(result.Accuracy, 0.0, 1.0);
        Assert.Equal(result.Matches, result.Decisions.Count(d => d.Matched));
    }

    [Fact]
    public void Replay_accuracy_is_zero_when_no_decisions_match()
    {
        var kyokus = TenhouLog.ParseDocument(ThreeTurnSyntheticKyoku);
        var policy = new EfficiencyPolicy();
        var result = TenhouReplay.ReplaySeat(kyokus[0], policy, seat: 0);

        double expected = result.TotalDecisions == 0 ? 0.0 : (double)result.Matches / result.TotalDecisions;
        Assert.Equal(expected, result.Accuracy, precision: 4);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Replay_rejects_out_of_range_seat(int seat)
    {
        var kyokus = TenhouLog.ParseDocument(ThreeTurnSyntheticKyoku);
        var policy = new EfficiencyPolicy();
        Assert.Throws<ArgumentOutOfRangeException>(() => TenhouReplay.ReplaySeat(kyokus[0], policy, seat));
    }
}

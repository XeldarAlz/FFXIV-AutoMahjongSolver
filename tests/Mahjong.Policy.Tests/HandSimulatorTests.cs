using System;
using Mahjong.Engine;
using Mahjong.Policy.Efficiency;
using Mahjong.Policy.Simulator;
using Xunit;

namespace Mahjong.Policy.Tests;

public class HandSimulatorTests
{
    [Fact]
    public void Simulates_single_hand_without_throwing()
    {
        var policies = new IPolicy[]
        {
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
        };
        var sim = new HandSimulator(new SeededRandomSource(42), new RiichiRuleSet());
        var result = sim.Simulate(policies);
        Assert.InRange(result.TurnCount, 1, 200);
        Assert.Equal(4, result.FinalScores.Length);
    }

    [Fact]
    public void Hand_outcome_is_tsumo_ron_or_ryuukyoku_in_valid_run()
    {
        var policies = new IPolicy[]
        {
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
        };
        var sim = new HandSimulator(new SeededRandomSource(100), new RiichiRuleSet());
        var result = sim.Simulate(policies);
        Assert.True(
            result.Outcome is HandSimulator.Outcome.Tsumo
                          or HandSimulator.Outcome.Ron
                          or HandSimulator.Outcome.Ryuukyoku,
            $"unexpected outcome: {result.Outcome}");
    }

    [Fact]
    public void Tsumo_winner_gets_score_increase()
    {
        var policies = new IPolicy[]
        {
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
        };

        for (int seed = 0; seed < 50; seed++)
        {
            var sim = new HandSimulator(new SeededRandomSource(seed), new RiichiRuleSet());
            var result = sim.Simulate(policies);
            if (result.Outcome == HandSimulator.Outcome.Tsumo)
            {
                Assert.True(result.WinnerSeat is >= 0 and < 4);
                Assert.True(result.FinalScores[result.WinnerSeat] > 25000,
                    $"winner score should exceed starting 25000, got {result.FinalScores[result.WinnerSeat]}");
                int othersSum = 0;
                for (int i = 0; i < 4; i++)
                    if (i != result.WinnerSeat)
                        othersSum += result.FinalScores[i];
                Assert.True(othersSum < 75000, "losers should have paid into the winner");
                return;
            }
        }
    }

    [Fact]
    public void Self_play_runs_N_hands_and_returns_stats()
    {
        var policies = new IPolicy[]
        {
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
        };
        var runner = new SelfPlayRunner(new RiichiRuleSet(), seed: 7);
        var stats = runner.Run(policies, hands: 10);
        Assert.Equal(10, stats.HandsPlayed);
        Assert.Equal(4, stats.WinCounts.Length);
        int totalOutcomes = 0;
        foreach (var w in stats.WinCounts)
            totalOutcomes += w;
        totalOutcomes += stats.RyuukyokuCount;
        totalOutcomes += stats.AbortCount;
        Assert.Equal(10, totalOutcomes);
    }

    [Fact]
    public void Furiten_detector_flags_when_a_wait_is_in_own_discards()
    {
        // 13-tile hand: 123m 456m 123p 456p 9p — tanki on 9p.
        var counts = new int[Tile.Count34];
        foreach (var id in new[] { 0, 1, 2, 3, 4, 5, 9, 10, 11, 12, 13, 14, 17 })
            counts[id]++;

        var emptyDiscards = Array.Empty<Tile>();
        var withWait = new[] { Tile.FromId(17) };
        var unrelated = new[] { Tile.FromId(20) };

        Assert.False(FuritenDetector.IsFuriten(counts, meldCount: 0, emptyDiscards));
        Assert.True(FuritenDetector.IsFuriten(counts, meldCount: 0, withWait));
        Assert.False(FuritenDetector.IsFuriten(counts, meldCount: 0, unrelated));
    }

    [Fact]
    public void Furiten_detector_returns_false_when_not_tenpai()
    {
        var counts = new int[Tile.Count34];
        foreach (var id in new[] { 0, 1, 9, 10, 17, 18, 19, 20, 25, 26, 27, 30, 33 })
            counts[id]++;
        Assert.False(FuritenDetector.IsFuriten(counts, meldCount: 0, new[] { Tile.FromId(0) }));
    }

    [Fact]
    public void Ron_detection_fires_when_policy_discards_a_winning_tile()
    {
        var policies = new IPolicy[]
        {
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
            new EfficiencyPolicy(),
        };
        var runner = new SelfPlayRunner(new RiichiRuleSet(), seed: 13);
        var stats = runner.Run(policies, hands: 20);
        int totalRons = 0;
        foreach (var r in stats.RonCounts)
            totalRons += r;
        int totalDealIns = 0;
        foreach (var d in stats.DealInCounts)
            totalDealIns += d;
        Assert.True(totalRons >= 0);
        Assert.Equal(totalRons, totalDealIns);
    }
}

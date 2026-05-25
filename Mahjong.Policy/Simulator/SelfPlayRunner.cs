using System;

namespace Mahjong.Policy.Simulator;

public sealed class SelfPlayRunner
{
    private readonly IRandomSource rng;
    private readonly IRuleSet rules;

    public SelfPlayRunner(IRuleSet rules, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(rng);
        this.rules = rules;
        this.rng = rng;
    }

    public SelfPlayRunner(IRuleSet rules, int? seed = null)
        : this(rules, seed is null ? new SeededRandomSource() : new SeededRandomSource(seed.Value))
    { }

    public readonly record struct Stats(
        int HandsPlayed,
        int[] WinCounts,
        int[] TsumoCounts,
        int[] RonCounts,
        int[] DealInCounts,
        int[] RiichiCounts,
        int RyuukyokuCount,
        int AbortCount,
        long[] TotalScoreDelta,
        int AverageTurnCount);

    public Stats Run(IPolicy[] policies, int hands = 100, int dealer = 0)
    {
        if (policies.Length != 4)
            throw new ArgumentException("need 4 policies");

        var wins = new int[4];
        var tsumoWins = new int[4];
        var ronWins = new int[4];
        var dealIns = new int[4];
        var riichis = new int[4];
        var ryuu = 0;
        var aborted = 0;
        var totalDelta = new long[4];
        int totalTurns = 0;
        var baseScores = new int[] { 25000, 25000, 25000, 25000 };

        var sim = new HandSimulator(rng, rules);
        for (int i = 0; i < hands; i++)
        {
            var result = sim.Simulate(policies, dealer: dealer);
            totalTurns += result.TurnCount;

            for (int s = 0; s < 4; s++)
                riichis[s] += result.RiichiDeclared[s];

            switch (result.Outcome)
            {
                case HandSimulator.Outcome.Tsumo:
                    wins[result.WinnerSeat]++;
                    tsumoWins[result.WinnerSeat]++;
                    for (int s = 0; s < 4; s++)
                        totalDelta[s] += result.FinalScores[s] - baseScores[s];
                    break;
                case HandSimulator.Outcome.Ron:
                    wins[result.WinnerSeat]++;
                    ronWins[result.WinnerSeat]++;
                    if (result.LoserSeat >= 0)
                        dealIns[result.LoserSeat]++;
                    for (int s = 0; s < 4; s++)
                        totalDelta[s] += result.FinalScores[s] - baseScores[s];
                    break;
                case HandSimulator.Outcome.Ryuukyoku:
                    ryuu++;
                    break;
                default:
                    aborted++;
                    break;
            }
        }

        return new Stats(
            HandsPlayed: hands,
            WinCounts: wins,
            TsumoCounts: tsumoWins,
            RonCounts: ronWins,
            DealInCounts: dealIns,
            RiichiCounts: riichis,
            RyuukyokuCount: ryuu,
            AbortCount: aborted,
            TotalScoreDelta: totalDelta,
            AverageTurnCount: hands > 0 ? totalTurns / hands : 0);
    }
}

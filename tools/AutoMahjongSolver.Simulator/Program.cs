using AutoMahjongSolver.Engine;
using AutoMahjongSolver.Simulator;

int matches = args.Length > 0 && int.TryParse(args[0], out int m) ? m : 100;
int seed = args.Length > 1 && int.TryParse(args[1], out int s) ? s : 42;

var policies = new Func<TurnInput, TurnDecision>[4];
Array.Fill(policies, TurnPolicy.Decide);

var rng = new Random(seed);
var stats = new SimStats();

var sw = System.Diagnostics.Stopwatch.StartNew();
for (int i = 0; i < matches; i++)
    MatchSimulator.Play(policies, rng, stats);
sw.Stop();

int wins = stats.Tsumos + stats.Rons;

Console.WriteLine($"self-play: {stats.Matches} matches, {stats.Hands} hands, seed {seed}, {sw.Elapsed.TotalSeconds:F1}s ({stats.Hands / Math.Max(sw.Elapsed.TotalSeconds, 0.001):F1} hands/s)");
Console.WriteLine($"  outcomes: {wins} wins ({stats.Tsumos} tsumo / {stats.Rons} ron), {stats.Draws} exhaustive draws ({100.0 * stats.Draws / Math.Max(stats.Hands, 1):F1}%)");
if (wins > 0)
    Console.WriteLine($"  avg win: {stats.WinPointsTotal / wins} points, {(double)stats.WinHanTotal / wins:F2} han");
Console.WriteLine();
Console.WriteLine("  seat  wins  win%   deal-in  deal-in%  riichi%  avg place  avg score");
for (int seat = 0; seat < 4; seat++)
{
    double winPct = 100.0 * stats.Wins[seat] / Math.Max(stats.Hands, 1);
    double dealInPct = 100.0 * stats.DealIns[seat] / Math.Max(stats.Hands, 1);
    double riichiPct = 100.0 * stats.Riichis[seat] / Math.Max(stats.Hands, 1);
    double avgPlace = (double)stats.PlacementSum[seat] / Math.Max(stats.Matches, 1);
    long avgScore = stats.ScoreSum[seat] / Math.Max(stats.Matches, 1);
    Console.WriteLine($"  {seat,4}  {stats.Wins[seat],4}  {winPct,4:F1}%  {stats.DealIns[seat],7}  {dealInPct,7:F1}%  {riichiPct,6:F1}%  {avgPlace,9:F2}  {avgScore,9}");
}

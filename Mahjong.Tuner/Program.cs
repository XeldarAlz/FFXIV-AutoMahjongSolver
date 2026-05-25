using Mahjong.Policy.Efficiency;
using Mahjong.Policy.Tuning;
using Mahjong.Rules;
using Mahjong.Rules.Rulesets;

namespace Mahjong.Tuner;

/// <summary>
/// Usage:
///   dotnet run --project Tuner -c Release -- [pop=8] [generations=10] [hands=50] [seed=42]
///   dotnet run --project Tuner -c Release -- coord [iters=30] [hands=200] [seed=4242]
///   dotnet run --project Tuner -c Release -- verify [hands=500] [seed=1234]
///
/// Default ruleset is Doman (MinHan=2). Pass --riichi to score under standard riichi rules.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
            System.Globalization.CultureInfo.InvariantCulture;

        bool useRiichi = args.Contains("--riichi");
        args = args.Where(a => a != "--riichi").ToArray();

        IRuleSet rules = useRiichi ? new RiichiRuleSet() : new DomanRuleSet();

        if (args.Length > 0 && args[0] == "verify")
            return Verify.RunVerify(args.Skip(1).ToArray(), rules);
        if (args.Length > 0 && args[0] == "coord")
            return RunCoord(args.Skip(1).ToArray(), rules);

        return RunEvolutionary(args, rules);
    }

    private static int RunEvolutionary(string[] args, IRuleSet rules)
    {
        int population = ParseArg(args, 0, 8);
        int generations = ParseArg(args, 1, 10);
        int hands = ParseArg(args, 2, 50);
        int seed = ParseArg(args, 3, 42);
        int sigmaPct = ParseArg(args, 4, 30);

        Console.WriteLine($"Doman Mahjong evolutionary tuner (rules={rules.Name})");
        Console.WriteLine($"  population={population}  generations={generations}  hands/eval={hands}  seed={seed}  sigma={sigmaPct / 100.0:F2}");
        Console.WriteLine($"  start = {FormatDiscard(DiscardWeights.Default)}");
        Console.WriteLine();

        var settings = new EvolutionaryTuner.Settings(
            Population: population,
            Survivors: Math.Max(2, population / 2),
            Generations: generations,
            HandsPerEvaluation: hands,
            InitialSigma: sigmaPct / 100.0,
            Seed: seed);

        var tuner = new EvolutionaryTuner();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = tuner.Tune(DiscardWeights.Default, settings, rules);
        sw.Stop();

        Console.WriteLine($"== complete in {sw.Elapsed.TotalSeconds:F1}s ==");
        Console.WriteLine();
        Console.WriteLine("per-generation incumbent mean:");
        foreach (var gen in run.Generations)
        {
            int beat = 0;
            long bestDelta = long.MinValue;
            foreach (var c in gen.Population)
            {
                if (c.NetDelta > 0)
                    beat++;
                if (c.NetDelta > bestDelta)
                    bestDelta = c.NetDelta;
            }
            Console.WriteLine(
                $"  gen {gen.Index,2}: best Δ={bestDelta,+8}  beat-baseline={beat}/{gen.Population.Length}  " +
                $"mean={FormatDiscard(gen.IncumbentMean)}");
        }

        Console.WriteLine();
        Console.WriteLine($"FINAL discard weights:");
        Console.WriteLine($"  {FormatDiscard(run.FinalMean)}");

        var bundle = WeightBundle.Default with { Discard = run.FinalMean };
        var outPath = WriteWeightsJson(bundle, prefix: "evo");
        Console.WriteLine($"wrote {outPath}");
        Console.WriteLine($"point JsonWeightProvider at this file (or copy to your weights.json) to use the tuned values.");
        return 0;
    }

    private static int RunCoord(string[] args, IRuleSet rules)
    {
        int iterations = ParseArg(args, 0, 30);
        int hands = ParseArg(args, 1, 200);
        int seed = ParseArg(args, 2, 4242);
        int perturbPct = ParseArg(args, 3, 30);
        double perturbFactor = 1.0 + perturbPct / 100.0;

        Console.WriteLine($"Doman Mahjong coordinate-descent tuner (rules={rules.Name})");
        Console.WriteLine($"  iterations={iterations}  hands/eval={hands}  seed={seed}  perturb={perturbFactor:F2}");
        Console.WriteLine($"  start = {FormatDiscard(DiscardWeights.Default)}");
        Console.WriteLine();

        var settings = new WeightTuner.Settings(
            HandsPerEvaluation: hands,
            Iterations: iterations,
            PerturbFactor: perturbFactor,
            Seed: seed);

        var tuner = new WeightTuner();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = tuner.Tune(DiscardWeights.Default, settings, rules);
        sw.Stop();

        Console.WriteLine($"== complete in {sw.Elapsed.TotalSeconds:F1}s ==");
        Console.WriteLine();
        Console.WriteLine($"accepted steps ({run.Steps.Count}/{iterations}):");
        foreach (var step in run.Steps)
        {
            Console.WriteLine(
                $"  iter {step.Iteration,2}: {step.Field,-18} {step.OldValue:F4} → {step.NewValue:F4}  Δ={step.ScoreDelta,+8}");
        }

        Console.WriteLine();
        Console.WriteLine($"FINAL discard weights:");
        Console.WriteLine($"  {FormatDiscard(run.FinalWeights)}");

        var bundle = WeightBundle.Default with { Discard = run.FinalWeights };
        var outPath = WriteWeightsJson(bundle, prefix: "coord");
        Console.WriteLine($"wrote {outPath}");
        return 0;
    }

    private static string WriteWeightsJson(WeightBundle bundle, string prefix)
    {
        var outputDir = Path.Combine("data", "weights");
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var outPath = Path.Combine(outputDir, $"{prefix}-{stamp}.json");
        JsonWeightProvider.Save(outPath, bundle);
        return outPath;
    }

    private static int ParseArg(string[] args, int index, int defaultValue)
        => index < args.Length && int.TryParse(args[index], out var v) ? v : defaultValue;

    private static string FormatDiscard(DiscardWeights w) =>
        $"Sh={w.Shanten:F2} Uk={w.UkeireKinds:F2}/{w.UkeireWeighted:F2} " +
        $"Do={w.Dora:F2} Ya={w.Yakuhai:F2} Iso={w.IsolatedTerminal:F2} Di={w.DealInCost:F4}";
}

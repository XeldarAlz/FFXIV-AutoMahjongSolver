using System;
using System.Collections.Generic;
using System.Linq;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Policy.Tuning;

/// <summary>(μ/μ, λ)-ES with diagonal-covariance Gaussian proposals.</summary>
public sealed class EvolutionaryTuner
{
    public sealed record Settings(
        int Population = 8,
        int Survivors = 4,
        int Generations = 10,
        int HandsPerEvaluation = 50,
        double InitialSigma = 0.3,
        double SigmaUp = 1.2,
        double SigmaDown = 0.85,
        double MaxSigma = 0.5,
        int Seed = 42)
    {
        public static Settings Default => new();
    }

    public readonly record struct Candidate(
        DiscardWeights Weights,
        long NetDelta);

    public readonly record struct Generation(
        int Index,
        DiscardWeights IncumbentMean,
        double[] Sigma,
        Candidate[] Population);

    public readonly record struct TuningRun(
        DiscardWeights StartingMean,
        DiscardWeights FinalMean,
        Generation[] Generations);

    private static readonly string[] Fields =
        { "UkeireKinds", "UkeireWeighted", "Dora", "Yakuhai", "IsolatedTerminal", "DealInCost" };

    public TuningRun Tune(DiscardWeights start, Settings? settings = null)
    {
        var s = settings ?? Settings.Default;
        if (s.Survivors <= 0 || s.Survivors > s.Population)
            throw new ArgumentException("Survivors must be in 1..Population");

        var rng = new SeededRandomSource(s.Seed);
        var mean = start;
        double[] sigma = Enumerable.Repeat(s.InitialSigma, Fields.Length).ToArray();
        var gens = new List<Generation>();

        for (int g = 0; g < s.Generations; g++)
        {
            var candidates = new Candidate[s.Population];
            int evalSeed = rng.Next();

            for (int i = 0; i < s.Population; i++)
            {
                var perturbed = mean;
                for (int f = 0; f < Fields.Length; f++)
                {
                    double factor = Math.Exp(Gaussian(rng) * sigma[f]);
                    double oldVal = GetField(perturbed, Fields[f]);
                    perturbed = SetField(perturbed, Fields[f], oldVal * factor);
                }

                var eval = WeightTuner.Evaluate(perturbed, mean, s.HandsPerEvaluation, evalSeed + i);
                long delta = eval.CandidateScoreDelta - eval.BaselineScoreDelta;
                candidates[i] = new Candidate(perturbed, delta);
            }

            Array.Sort(candidates, (a, b) => b.NetDelta.CompareTo(a.NetDelta));
            var survivors = candidates.Take(s.Survivors).ToArray();

            mean = AverageWeights(survivors.Select(c => c.Weights));

            int beating = survivors.Count(c => c.NetDelta > 0);
            double factor2 = beating * 2 > survivors.Length ? s.SigmaUp : s.SigmaDown;
            for (int f = 0; f < sigma.Length; f++)
                sigma[f] = Math.Min(sigma[f] * factor2, s.MaxSigma);

            gens.Add(new Generation(g, mean, (double[])sigma.Clone(), candidates));
        }

        return new TuningRun(start, mean, gens.ToArray());
    }

    private static double Gaussian(IRandomSource rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static DiscardWeights AverageWeights(IEnumerable<DiscardWeights> ws)
    {
        var list = ws.ToArray();
        int n = list.Length;
        return new DiscardWeights(
            Shanten: list.Average(w => w.Shanten),
            UkeireKinds: list.Average(w => w.UkeireKinds),
            UkeireWeighted: list.Average(w => w.UkeireWeighted),
            Dora: list.Average(w => w.Dora),
            Yakuhai: list.Average(w => w.Yakuhai),
            IsolatedTerminal: list.Average(w => w.IsolatedTerminal),
            DealInCost: list.Average(w => w.DealInCost));
    }

    private static double GetField(DiscardWeights w, string field) => field switch
    {
        "Shanten" => w.Shanten,
        "UkeireKinds" => w.UkeireKinds,
        "UkeireWeighted" => w.UkeireWeighted,
        "Dora" => w.Dora,
        "Yakuhai" => w.Yakuhai,
        "IsolatedTerminal" => w.IsolatedTerminal,
        "DealInCost" => w.DealInCost,
        _ => throw new ArgumentException(field),
    };

    private static DiscardWeights SetField(DiscardWeights w, string field, double value) => field switch
    {
        "Shanten" => w with { Shanten = value },
        "UkeireKinds" => w with { UkeireKinds = value },
        "UkeireWeighted" => w with { UkeireWeighted = value },
        "Dora" => w with { Dora = value },
        "Yakuhai" => w with { Yakuhai = value },
        "IsolatedTerminal" => w with { IsolatedTerminal = value },
        "DealInCost" => w with { DealInCost = value },
        _ => throw new ArgumentException(field),
    };
}

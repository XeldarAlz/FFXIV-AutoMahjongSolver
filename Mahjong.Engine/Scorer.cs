using Mahjong.Rules;

namespace Mahjong.Engine;

/// <summary>
/// Stateless beyond the injected <see cref="IRuleSet"/>; thread-safe if the rules are immutable.
/// </summary>
public sealed class Scorer
{
    private readonly IRuleSet rules;

    public Scorer(IRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        this.rules = rules;
    }

    public ScoreResult? Evaluate(Hand hand, WinContext ctx)
    {
        var decompositions = HandDecomposer.Enumerate(hand, ctx);
        if (decompositions.Count == 0)
            return null;

        ScoreResult? best = null;
        int bestTotal = -1;
        foreach (var d in decompositions)
        {
            var candidate = ScoreOne(d, ctx);
            if (candidate is null)
                continue;
            if (candidate.Payments.Total > bestTotal)
            {
                bestTotal = candidate.Payments.Total;
                best = candidate;
            }
        }
        return best;
    }

    private ScoreResult? ScoreOne(Decomposition d, WinContext ctx)
    {
        var hits = DetectYakuInternal(d, ctx);
        if (hits.Count == 0)
            return null;

        bool isYakuman = AnyYakuman(hits);
        int han = TotalHan(hits);
        if (!isYakuman)
        {
            han += CountDora(d, ctx);
            if (han < rules.MinHan)
                return null;
        }

        int fu = rules.FuRule.Compute(d, ctx, hits);
        var tier = rules.ScoringRule.ResolveTier(han, fu, isYakuman);
        var payments = rules.ScoringRule.Pay(tier, ctx.IsDealer, ctx.Kind);

        return new ScoreResult(d, hits, han, fu, tier.BasePoints, payments, tier.Name);
    }

    public IReadOnlyList<YakuHit> DetectYaku(Decomposition d, WinContext ctx)
        => DetectYakuInternal(d, ctx);

    private List<YakuHit> DetectYakuInternal(Decomposition d, WinContext ctx)
    {
        var hits = new List<YakuHit>(8);
        foreach (var rule in rules.YakuRules)
            hits.AddRange(rule.Detect(d, ctx));

        if (AnyYakuman(hits))
            return KeepOnlyYakuman(hits);

        ApplyConflicts(hits);
        return hits;
    }

    private void ApplyConflicts(List<YakuHit> hits)
    {
        if (hits.Count == 0)
            return;

        var hitYaku = new HashSet<Mahjong.Core.Yaku>();
        foreach (var h in hits)
            hitYaku.Add(h.Yaku);

        var toRemove = new HashSet<Mahjong.Core.Yaku>();
        foreach (var rule in rules.YakuRules)
        {
            if (rule.Conflicts.Count == 0)
                continue;
            if (!hitYaku.Contains(rule.Definition.Id))
                continue;
            foreach (var conflict in rule.Conflicts)
                toRemove.Add(conflict);
        }

        if (toRemove.Count > 0)
            hits.RemoveAll(h => toRemove.Contains(h.Yaku));
    }

    private int CountDora(Decomposition d, WinContext ctx)
    {
        int count = 0;
        var counts = new int[Tile.Count34];
        foreach (var g in d.Groups)
            foreach (var t in g.Tiles)
                counts[t.Id]++;

        foreach (var indicator in ctx.Dora)
            count += counts[rules.DoraRule.Next(indicator).Id];

        bool uraEligible = (ctx.IsRiichi || ctx.IsDoubleRiichi) && d.IsMenzen;
        if (uraEligible)
        {
            foreach (var indicator in ctx.UraDora)
                count += counts[rules.DoraRule.Next(indicator).Id];
        }

        count += ctx.AkaDora;

        return count;
    }

    private static bool AnyYakuman(List<YakuHit> hits)
    {
        foreach (var h in hits)
        {
            if (h.IsYakuman)
                return true;
        }
        return false;
    }

    private static int TotalHan(List<YakuHit> hits)
    {
        int total = 0;
        foreach (var h in hits)
            total += h.Han;
        return total;
    }

    private static List<YakuHit> KeepOnlyYakuman(List<YakuHit> hits)
    {
        var only = new List<YakuHit>(hits.Count);
        foreach (var h in hits)
        {
            if (h.IsYakuman)
                only.Add(h);
        }
        return only;
    }
}

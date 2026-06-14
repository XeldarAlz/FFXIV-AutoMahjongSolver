namespace AutoMahjongSolver.Engine;

/// <summary>Table context the value ranking needs beyond the tiles: winds, dealer, dora, held aka.</summary>
public sealed record EvalContext
{
    public int SeatWind { get; init; }
    public int RoundWind { get; init; }
    public bool Dealer { get; init; }
    public bool Closed { get; init; } = true;
    public IReadOnlyList<int>? DoraIndicators { get; init; }

    /// <summary>Red-five copies held, per tile type (only the 5m/5p/5s entries can be non-zero).</summary>
    public IReadOnlyList<int>? AkaByType { get; init; }
}

public readonly record struct ScoredDiscard(DiscardOption Option, long Value);

/// <summary>
/// Ranks discards by shanten first, then by expected value instead of raw acceptance. At tenpai the
/// value is exact: for every winning tile, remaining copies × the points the completed hand pays
/// (riichi assumed for a closed hand, since the bot declares it; a yakuless open wait is worth 0 —
/// it can never win). Above tenpai the value is acceptance weighted by what the hand keeps: dora,
/// aka, and yakuhai pairs, scaled so roughly one dora outweighs ~2.5 tiles of acceptance.
/// </summary>
public static class DiscardEvaluator
{
    private const int UkeireWeight = 16;
    private const int DoraWeight = 40;
    private const int YakuhaiPairWeight = 25;

    public static List<ScoredDiscard> Rank(
        ReadOnlySpan<int> counts14, IReadOnlyList<Meld>? melds, int[]? seen, EvalContext ctx)
    {
        melds ??= Array.Empty<Meld>();
        List<DiscardOption> options = UkeireCalculator.BestDiscards(counts14, melds.Count, seen);

        var ranked = new List<ScoredDiscard>(options.Count);
        Span<int> hand13 = stackalloc int[Tiles.TypeCount];

        foreach (DiscardOption option in options)
        {
            counts14.CopyTo(hand13);
            hand13[option.Tile]--;

            int akaRemaining = AkaAfterDiscard(counts14, option.Tile, ctx.AkaByType);
            long value = option.Shanten == 0
                ? TenpaiValue(hand13, melds, option, akaRemaining, ctx)
                : ProgressValue(hand13, option, akaRemaining, ctx);

            ranked.Add(new ScoredDiscard(option, value));
        }

        ranked.Sort(static (a, b) => a.Option.Shanten != b.Option.Shanten
            ? a.Option.Shanten - b.Option.Shanten
            : b.Value.CompareTo(a.Value));

        return ranked;
    }

    /// <summary>Σ over winning tiles: live copies × points paid. Exact EV ranking for tenpai hands.</summary>
    private static long TenpaiValue(
        ReadOnlySpan<int> hand13, IReadOnlyList<Meld> melds, in DiscardOption option, int akaRemaining, EvalContext ctx)
    {
        Span<int> hand14 = stackalloc int[Tiles.TypeCount];
        long value = 0;

        foreach (AcceptTile accept in option.Accepts)
        {
            hand13.CopyTo(hand14);
            hand14[accept.Tile]++;

            var winCtx = new WinContext
            {
                WinTile = accept.Tile,
                Tsumo = false,
                Riichi = ctx.Closed,
                SeatWind = ctx.SeatWind,
                RoundWind = ctx.RoundWind,
                AkaCount = akaRemaining,
                DoraIndicators = ctx.DoraIndicators,
            };

            WinResult? result = YakuCalculator.Evaluate(hand14, melds, winCtx);
            if (result is null || !result.HasYaku)
                continue; // a yakuless wait can never win

            value += accept.Remaining * (long)DomanScoring.Points(result.TotalHan, ctx.Dealer, result.IsYakuman);
        }

        return value;
    }

    /// <summary>Acceptance plus what the kept tiles are worth: dora, aka, and yakuhai pairs.</summary>
    private static long ProgressValue(ReadOnlySpan<int> hand13, in DiscardOption option, int akaRemaining, EvalContext ctx)
    {
        long held = akaRemaining;

        if (ctx.DoraIndicators is { } indicators)
            foreach (int indicator in indicators)
                held += hand13[YakuCalculator.DoraFromIndicator(indicator)];

        long pairs = 0;
        for (int t = 31; t <= 33; t++)
            if (hand13[t] >= 2)
                pairs++;
        if (hand13[27 + ctx.SeatWind] >= 2)
            pairs++;
        if (ctx.RoundWind != ctx.SeatWind && hand13[27 + ctx.RoundWind] >= 2)
            pairs++;

        return option.UkeireCount * (long)UkeireWeight + held * DoraWeight + pairs * YakuhaiPairWeight;
    }

    /// <summary>
    /// Red fives surviving the discard. The actuator discards a plain copy when one exists, so an
    /// aka is only lost when every remaining copy of that type is red.
    /// </summary>
    private static int AkaAfterDiscard(ReadOnlySpan<int> counts14, int discard, IReadOnlyList<int>? akaByType)
    {
        if (akaByType is null)
            return 0;

        int total = 0;
        for (int t = 0; t < Tiles.TypeCount && t < akaByType.Count; t++)
            total += akaByType[t];

        int akaOfType = discard < akaByType.Count ? akaByType[discard] : 0;
        int plainCopies = counts14[discard] - akaOfType;
        return plainCopies >= 1 ? total : total - 1;
    }
}

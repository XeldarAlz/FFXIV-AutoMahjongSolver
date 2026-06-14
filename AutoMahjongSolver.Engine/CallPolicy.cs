namespace AutoMahjongSolver.Engine;

/// <summary>
/// Decides whether to call an opponent's discard. V1 calls pon only: chi has an unverified
/// variant-selection UI and kan would flip dora the reader cannot see yet. A pon must strictly
/// advance the hand, never open up into a declared riichi, and must keep a yaku path alive —
/// yakuhai is always live; otherwise an already-secured yakuhai set, or a dora-rich tanyao
/// route, is required (an open hand without a yaku can never win).
/// </summary>
public static class CallPolicy
{
    public static bool ShouldPon(
        ReadOnlySpan<int> counts13, IReadOnlyList<Meld>? melds, int tile, EvalContext ctx, bool opponentRiichi)
    {
        if (tile < 0 || counts13[tile] < 2 || opponentRiichi)
            return false;

        melds ??= Array.Empty<Meld>();
        int current = ShantenCalculator.Calculate(counts13, melds.Count);

        Span<int> after = stackalloc int[Tiles.TypeCount];
        counts13.CopyTo(after);
        after[tile] -= 2;

        int best = int.MaxValue;
        for (int d = 0; d < Tiles.TypeCount; d++)
        {
            if (after[d] == 0)
                continue;
            after[d]--;
            best = Math.Min(best, ShantenCalculator.Calculate(after, melds.Count + 1));
            after[d]++;
        }

        if (best >= current)
            return false;

        if (IsYakuhai(tile, ctx))
            return true;

        if (HasYakuhaiSet(counts13, melds, ctx))
            return true;

        // Dora-rich tanyao route: the called tile is a simple and the off-plan terminals/honors
        // are few enough to shed on the way to tenpai.
        if (!Tiles.IsNumber(tile) || Tiles.IsTerminal(tile))
            return false;

        int offPlan = 0;
        for (int t = 0; t < Tiles.TypeCount; t++)
            if (Tiles.IsTerminalOrHonor(t))
                offPlan += after[t];
        if (offPlan > Math.Max(1, best))
            return false;

        return DoraValue(counts13, melds, ctx) >= 2;
    }

    private static bool IsYakuhai(int tile, EvalContext ctx)
        => tile >= 31 || tile == 27 + ctx.SeatWind || tile == 27 + ctx.RoundWind;

    private static bool HasYakuhaiSet(ReadOnlySpan<int> counts13, IReadOnlyList<Meld> melds, EvalContext ctx)
    {
        foreach (Meld meld in melds)
            if (meld.Kind != MeldKind.Chii && IsYakuhai(meld.Tiles[0], ctx))
                return true;

        for (int t = 27; t < Tiles.TypeCount; t++)
            if (counts13[t] >= 3 && IsYakuhai(t, ctx))
                return true;

        return false;
    }

    private static int DoraValue(ReadOnlySpan<int> counts13, IReadOnlyList<Meld> melds, EvalContext ctx)
    {
        int value = 0;

        if (ctx.AkaByType is { } aka)
            for (int t = 0; t < Tiles.TypeCount && t < aka.Count; t++)
                value += aka[t];

        if (ctx.DoraIndicators is { } indicators)
        {
            foreach (int indicator in indicators)
            {
                int dora = YakuCalculator.DoraFromIndicator(indicator);
                value += counts13[dora];
                foreach (Meld meld in melds)
                    foreach (int t in meld.Tiles)
                        if (t == dora)
                            value++;
            }
        }

        return value;
    }
}

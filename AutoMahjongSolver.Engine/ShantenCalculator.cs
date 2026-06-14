namespace AutoMahjongSolver.Engine;

/// <summary>
/// Exact shanten (distance-to-tenpai) over a 34-length tile-count array.
/// Convention: -1 = complete hand, 0 = tenpai, positive = tiles-of-progress away.
/// Takes the minimum of the regular, seven-pairs, and thirteen-orphans forms.
/// </summary>
public static class ShantenCalculator
{
    public const int Complete = -1;

    /// <param name="counts">Concealed tile counts (length 34).</param>
    /// <param name="meldCount">Number of called (open) melds; each is a completed set.</param>
    public static int Calculate(ReadOnlySpan<int> counts, int meldCount = 0)
    {
        int best = Regular(counts, meldCount);

        if (meldCount == 0)
        {
            best = Math.Min(best, SevenPairs(counts));
            best = Math.Min(best, ThirteenOrphans(counts));
        }

        return best;
    }

    public static int Regular(ReadOnlySpan<int> counts, int meldCount)
    {
        Span<int> work = stackalloc int[Tiles.TypeCount];
        counts.CopyTo(work);

        int best = Eval(work, meldCount, hasHead: false);

        for (int t = 0; t < Tiles.TypeCount; t++)
        {
            if (work[t] >= 2)
            {
                work[t] -= 2;
                best = Math.Min(best, Eval(work, meldCount, hasHead: true));
                work[t] += 2;
            }
        }

        return best;
    }

    private static int Eval(Span<int> work, int meldCount, bool hasHead)
    {
        int best = int.MaxValue;
        Decompose(work, 0, meldCount, sets: 0, partials: 0, hasHead, ref best);
        return best;
    }

    private static void Decompose(Span<int> c, int i, int meldCount, int sets, int partials, bool hasHead, ref int best)
    {
        while (i < Tiles.TypeCount && c[i] == 0)
            i++;

        if (i >= Tiles.TypeCount)
        {
            int blocks = meldCount + sets;
            int usefulPartials = Math.Clamp(partials, 0, Math.Max(0, 4 - blocks));
            int shanten = 8 - (2 * blocks) - usefulPartials - (hasHead ? 1 : 0);
            if (shanten < best)
                best = shanten;
            return;
        }

        bool number = Tiles.IsNumber(i);
        int rank = Tiles.Rank(i);

        if (c[i] >= 3)
        {
            c[i] -= 3;
            Decompose(c, i, meldCount, sets + 1, partials, hasHead, ref best);
            c[i] += 3;
        }

        if (number && rank <= 7 && c[i + 1] > 0 && c[i + 2] > 0)
        {
            c[i]--; c[i + 1]--; c[i + 2]--;
            Decompose(c, i, meldCount, sets + 1, partials, hasHead, ref best);
            c[i]++; c[i + 1]++; c[i + 2]++;
        }

        if (c[i] >= 2)
        {
            c[i] -= 2;
            Decompose(c, i, meldCount, sets, partials + 1, hasHead, ref best);
            c[i] += 2;
        }

        if (number && rank <= 8 && c[i + 1] > 0)
        {
            c[i]--; c[i + 1]--;
            Decompose(c, i, meldCount, sets, partials + 1, hasHead, ref best);
            c[i]++; c[i + 1]++;
        }

        if (number && rank <= 7 && c[i + 2] > 0)
        {
            c[i]--; c[i + 2]--;
            Decompose(c, i, meldCount, sets, partials + 1, hasHead, ref best);
            c[i]++; c[i + 2]++;
        }

        int saved = c[i];
        c[i] = 0;
        Decompose(c, i + 1, meldCount, sets, partials, hasHead, ref best);
        c[i] = saved;
    }

    public static int SevenPairs(ReadOnlySpan<int> counts)
    {
        int pairs = 0;
        int kinds = 0;
        for (int t = 0; t < Tiles.TypeCount; t++)
        {
            if (counts[t] >= 1) kinds++;
            if (counts[t] >= 2) pairs++;
        }

        int shanten = 6 - pairs;
        if (kinds < 7)
            shanten += 7 - kinds;
        return shanten;
    }

    public static int ThirteenOrphans(ReadOnlySpan<int> counts)
    {
        int distinct = 0;
        bool hasPair = false;
        foreach (int t in Tiles.TerminalsAndHonors)
        {
            if (counts[t] >= 1) distinct++;
            if (counts[t] >= 2) hasPair = true;
        }

        return 13 - distinct - (hasPair ? 1 : 0);
    }
}

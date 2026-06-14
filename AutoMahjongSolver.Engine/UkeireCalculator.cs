namespace AutoMahjongSolver.Engine;

/// <summary>A tile that advances the hand, with how many copies are still unseen.</summary>
public readonly record struct AcceptTile(int Tile, int Remaining);

/// <summary>The acceptance of a 13-tile hand: its shanten and which draws improve it.</summary>
public readonly record struct Acceptance(int Shanten, int Types, int Count, IReadOnlyList<AcceptTile> Tiles);

/// <summary>A candidate discard from a 14-tile hand and the acceptance it leaves behind.</summary>
public readonly record struct DiscardOption(int Tile, int Shanten, int UkeireTypes, int UkeireCount, IReadOnlyList<AcceptTile> Accepts);

/// <summary>
/// Ukeire (tile acceptance): which draws reduce shanten, and how many copies remain.
/// Remaining-copy counting uses an optional <c>seen</c> array (copies already visible across the
/// whole table: your hand, every discard, melds, dora indicator). When omitted it falls back to the
/// hand's own tiles only, which is exact for an isolated-hand analysis and keeps unit tests simple.
/// </summary>
public static class UkeireCalculator
{
    /// <summary>Acceptance of a 13-tile (concealed) hand at its current shanten.</summary>
    public static Acceptance Accept(ReadOnlySpan<int> counts13, int meldCount = 0, int[]? seen = null)
    {
        int current = ShantenCalculator.Calculate(counts13, meldCount);
        return AcceptAt(counts13, meldCount, current, seen);
    }

    private static Acceptance AcceptAt(ReadOnlySpan<int> counts13, int meldCount, int current, int[]? seen)
    {
        Span<int> work = stackalloc int[Tiles.TypeCount];
        counts13.CopyTo(work);

        var tiles = new List<AcceptTile>();
        int total = 0;

        for (int t = 0; t < Tiles.TypeCount; t++)
        {
            if (work[t] >= 4)
                continue;

            work[t]++;
            int shanten = ShantenCalculator.Calculate(work, meldCount);
            work[t]--;

            if (shanten < current)
            {
                int known = seen is not null ? seen[t] : work[t];
                int remaining = Math.Max(0, 4 - known);
                if (remaining > 0)
                {
                    tiles.Add(new AcceptTile(t, remaining));
                    total += remaining;
                }
            }
        }

        return new Acceptance(current, tiles.Count, total, tiles);
    }

    /// <summary>
    /// Rank every legal discard from a 14-tile hand by resulting shanten (ascending),
    /// breaking ties by total ukeire (descending). The pure-efficiency layer of the solver.
    /// </summary>
    public static List<DiscardOption> BestDiscards(ReadOnlySpan<int> counts14, int meldCount = 0, int[]? seen = null)
    {
        Span<int> work = stackalloc int[Tiles.TypeCount];
        counts14.CopyTo(work);

        var options = new List<DiscardOption>();

        for (int d = 0; d < Tiles.TypeCount; d++)
        {
            if (work[d] == 0)
                continue;

            work[d]--;
            int shanten = ShantenCalculator.Calculate(work, meldCount);
            Acceptance acc = AcceptAt(work, meldCount, shanten, seen);
            work[d]++;

            options.Add(new DiscardOption(d, shanten, acc.Types, acc.Count, acc.Tiles));
        }

        options.Sort(static (a, b) =>
            a.Shanten != b.Shanten ? a.Shanten - b.Shanten : b.UkeireCount - a.UkeireCount);

        return options;
    }
}

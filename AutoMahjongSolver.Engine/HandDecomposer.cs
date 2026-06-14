namespace AutoMahjongSolver.Engine;

public enum BlockKind
{
    Run,
    Triplet,
    Quad,
}

/// <summary>One block of a decomposed hand. <paramref name="Tile"/> is the lowest tile for a run.</summary>
public readonly record struct SetBlock(BlockKind Kind, int Tile, bool Concealed);

/// <summary>A standard-form split of the concealed tiles into a head pair and runs/triplets.</summary>
public sealed record Decomposition(int HeadTile, IReadOnlyList<SetBlock> Sets);

/// <summary>
/// Enumerates every standard-form decomposition of a complete concealed hand (2, 5, 8, 11 or 14
/// tiles). Different decompositions of the same tiles score different yaku, so the caller evaluates
/// all of them and keeps the best. Incomplete hands simply produce no results.
/// </summary>
public static class HandDecomposer
{
    public static List<Decomposition> Decompose(ReadOnlySpan<int> counts)
    {
        var results = new List<Decomposition>();
        int[] work = counts.ToArray();

        for (int head = 0; head < Tiles.TypeCount; head++)
        {
            if (work[head] < 2)
                continue;

            work[head] -= 2;
            Recurse(work, 0, new List<SetBlock>(), head, results);
            work[head] += 2;
        }

        return results;
    }

    /// <summary>
    /// At each tile index the split is fully determined by how many triplets we take there: the
    /// leftover copies must all start runs. Deciding triplets-then-runs per index enumerates every
    /// decomposition exactly once (no duplicate orderings).
    /// </summary>
    private static void Recurse(int[] c, int i, List<SetBlock> sets, int head, List<Decomposition> results)
    {
        while (i < Tiles.TypeCount && c[i] == 0)
            i++;

        if (i >= Tiles.TypeCount)
        {
            results.Add(new Decomposition(head, sets.ToArray()));
            return;
        }

        for (int triplets = c[i] / 3; triplets >= 0; triplets--)
        {
            int runs = c[i] - 3 * triplets;
            if (runs > 0 && (!Tiles.IsNumber(i) || Tiles.Rank(i) > 7 || c[i + 1] < runs || c[i + 2] < runs))
                continue;

            c[i] = 0;
            if (runs > 0)
            {
                c[i + 1] -= runs;
                c[i + 2] -= runs;
            }

            for (int k = 0; k < triplets; k++)
                sets.Add(new SetBlock(BlockKind.Triplet, i, true));
            for (int k = 0; k < runs; k++)
                sets.Add(new SetBlock(BlockKind.Run, i, true));

            Recurse(c, i + 1, sets, head, results);

            sets.RemoveRange(sets.Count - triplets - runs, triplets + runs);
            c[i] = 3 * triplets + runs;
            if (runs > 0)
            {
                c[i + 1] += runs;
                c[i + 2] += runs;
            }
        }
    }
}

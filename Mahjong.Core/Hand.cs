namespace Mahjong.Core;

/// <summary>
/// A player's hand: closed-tile counts (34-space) plus open melds.
/// Immutable — mutation returns a new instance, and inputs are defensive-copied
/// at construction so callers can keep their own buffers.
/// </summary>
public sealed class Hand
{
    private readonly int[] closedCounts;

    public IReadOnlyList<int> ClosedCounts => closedCounts;
    public IReadOnlyList<Meld> OpenMelds { get; }
    public int ClosedTileCount { get; }

    public Hand(int[] closedCounts, IReadOnlyList<Meld>? openMelds = null)
    {
        ArgumentNullException.ThrowIfNull(closedCounts);
        if (closedCounts.Length != Tile.Count34)
            throw new ArgumentException($"closedCounts must be length {Tile.Count34}");

        var copy = new int[Tile.Count34];
        int total = 0;
        for (int i = 0; i < Tile.Count34; i++)
        {
            int c = closedCounts[i];
            if (c < 0 || c > Tile.CopiesPerKind)
                throw new ArgumentException($"invalid count {c} at tile {i}");
            copy[i] = c;
            total += c;
        }

        this.closedCounts = copy;
        ClosedTileCount = total;
        OpenMelds = openMelds is null ? [] : [.. openMelds];
    }

    public static Hand FromTiles(IEnumerable<Tile> closed, IReadOnlyList<Meld>? melds = null)
        => new(Tiles.ToCounts(closed), melds);

    public static Hand FromNotation(string notation, IReadOnlyList<Meld>? melds = null)
        => FromTiles(Tiles.Parse(notation), melds);

    /// <summary>Total tiles including open melds (closed + 3 per meld; kans still count as 3 for shanten).</summary>
    public int TotalShantenTileCount => ClosedTileCount + OpenMelds.Count * 3;

    /// <summary>Return a mutable copy of the closed counts for in-place DP.</summary>
    public int[] CloneCounts()
    {
        var copy = new int[Tile.Count34];
        Array.Copy(closedCounts, copy, Tile.Count34);
        return copy;
    }

    public Hand WithTileAdded(Tile t)
    {
        var copy = CloneCounts();
        copy[t.Id]++;
        return new Hand(copy, OpenMelds);
    }

    public Hand WithTileRemoved(Tile t)
    {
        var copy = CloneCounts();
        if (copy[t.Id] == 0)
            throw new InvalidOperationException($"hand does not contain {t}");
        copy[t.Id]--;
        return new Hand(copy, OpenMelds);
    }

    public override string ToString()
    {
        var closed = Tiles.RenderCounts(closedCounts);
        if (OpenMelds.Count == 0)
            return closed;
        var melds = string.Join(" ", OpenMelds.Select(m => Tiles.Render(m.Tiles)));
        return $"{closed} | {melds}";
    }
}

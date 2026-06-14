namespace AutoMahjongSolver.Engine;

public enum MeldKind
{
    Chii,
    Pon,
    Kan,
}

/// <summary>
/// A called set: a run (chii), triplet (pon), or quad (kan), expressed in the canonical
/// 34-type tile space. Each meld is one completed block for shanten purposes regardless of tile
/// count; the tiles are retained for yaku/scoring. <paramref name="IsOpen"/> is false only for
/// a concealed kan (ankan), which keeps the hand closed for yaku purposes.
/// </summary>
public readonly record struct Meld(MeldKind Kind, IReadOnlyList<int> Tiles, bool IsOpen = true)
{
    /// <summary>
    /// Classify an unordered group of tiles into a meld. Accepts a triplet (three identical),
    /// a quad (four identical), or a run (three consecutive numbers of one suit).
    /// </summary>
    public static bool TryClassify(IReadOnlyList<int> tiles, out Meld meld)
    {
        meld = default;
        if (tiles is null || tiles.Count is < 3 or > 4)
            return false;

        var sorted = tiles.ToArray();
        Array.Sort(sorted);

        if (sorted.Length == 4)
        {
            if (sorted[0] == sorted[1] && sorted[1] == sorted[2] && sorted[2] == sorted[3])
            {
                meld = new Meld(MeldKind.Kan, sorted);
                return true;
            }
            return false;
        }

        if (sorted[0] == sorted[1] && sorted[1] == sorted[2])
        {
            meld = new Meld(MeldKind.Pon, sorted);
            return true;
        }

        if (Engine.Tiles.IsNumber(sorted[0]) &&
            Engine.Tiles.SuitOf(sorted[0]) == Engine.Tiles.SuitOf(sorted[2]) &&
            sorted[1] == sorted[0] + 1 &&
            sorted[2] == sorted[0] + 2)
        {
            meld = new Meld(MeldKind.Chii, sorted);
            return true;
        }

        return false;
    }

    public override string ToString()
        => $"{Kind}:{string.Join(string.Empty, Tiles.Select(Engine.Tiles.Name))}";
}

namespace Mahjong.Core;

public enum GroupKind : byte
{
    Run,
    Triplet,
    Kan,
    Pair,
}

public enum DecompositionForm : byte
{
    Standard,
    Chiitoitsu,
    Kokushi,
}

/// <summary><see cref="First"/> is the anchor — lowest tile of a run, the tile itself otherwise.</summary>
public readonly record struct Group(
    GroupKind Kind,
    Tile First,
    bool IsOpen,
    bool IsCompletedByWinningTile = false)
{
    public int TileCount => Kind switch
    {
        GroupKind.Run => 3,
        GroupKind.Triplet => 3,
        GroupKind.Kan => 4,
        GroupKind.Pair => 2,
        _ => 0,
    };

    public bool IsConcealedTriplet => Kind == GroupKind.Triplet && !IsOpen;
    public bool IsConcealedKan => Kind == GroupKind.Kan && !IsOpen;

    public bool ContainsTerminalOrHonor
    {
        get
        {
            if (Kind == GroupKind.Run)
            {
                int pos = First.Id % 9;
                return pos == 0 || pos == 6;
            }
            return First.IsTerminalOrHonor;
        }
    }

    /// <summary>False for runs — any 3-consecutive run hits a simple in 2..8.</summary>
    public bool AllTerminalOrHonor => Kind != GroupKind.Run && First.IsTerminalOrHonor;

    public Tile[] Tiles => Kind switch
    {
        GroupKind.Run => [First, Tile.FromId(First.Id + 1), Tile.FromId(First.Id + 2)],
        GroupKind.Pair => [First, First],
        GroupKind.Triplet => [First, First, First],
        GroupKind.Kan => [First, First, First, First],
        _ => [],
    };

    public bool ContainsTile(Tile t) => Kind switch
    {
        GroupKind.Run => t.Id >= First.Id && t.Id <= First.Id + 2,
        _ => t.Id == First.Id,
    };

    public static Group FromMeld(Meld m, bool completedByWin = false) => m.Kind switch
    {
        MeldKind.Chi => new Group(GroupKind.Run, m.Tiles[0], IsOpen: true, completedByWin),
        MeldKind.Pon => new Group(GroupKind.Triplet, m.Tiles[0], IsOpen: true, completedByWin),
        MeldKind.MinKan or MeldKind.ShouMinKan
            => new Group(GroupKind.Kan, m.Tiles[0], IsOpen: true, completedByWin),
        MeldKind.AnKan
            => new Group(GroupKind.Kan, m.Tiles[0], IsOpen: false, completedByWin),
        _ => throw new InvalidOperationException($"unknown meld kind {m.Kind}"),
    };
}

/// <summary>
/// Standard: 4 sets + 1 pair. Chiitoitsu: 7 pairs. Kokushi: pseudo — Groups may be empty,
/// info lives on the source hand.
/// </summary>
public sealed record Decomposition(
    DecompositionForm Form,
    IReadOnlyList<Group> Groups,
    bool IsMenzen,
    Tile WinningTile,
    bool WinningTileFromOpponent)
{
    public IReadOnlyList<Group> Groups { get; init; } = [.. Groups];

    public Group Pair => Groups.First(g => g.Kind == GroupKind.Pair);
    public IEnumerable<Group> Sets => Groups.Where(g => g.Kind != GroupKind.Pair);

    public int ConcealedTripletCount =>
        Groups.Count(g => g.Kind == GroupKind.Triplet && !g.IsOpen
                          && (!g.IsCompletedByWinningTile || !WinningTileFromOpponent));

    public int KanCount => Groups.Count(g => g.Kind == GroupKind.Kan);
}

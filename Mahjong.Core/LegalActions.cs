namespace Mahjong.Core;

[Flags]
public enum ActionFlags
{
    None = 0,
    Discard = 1 << 0,
    Riichi = 1 << 1,
    Tsumo = 1 << 2,
    Ron = 1 << 3,
    Pon = 1 << 4,
    Chi = 1 << 5,
    AnKan = 1 << 6,
    MinKan = 1 << 7,
    ShouMinKan = 1 << 8,
    Pass = 1 << 9,
}

/// <summary><paramref name="FromSeat"/> is -1 for self-kan.</summary>
public readonly record struct MeldCandidate(
    MeldKind Kind,
    Tile ClaimedTile,
    Tile[] HandTiles,
    int FromSeat);

public sealed record LegalActions(
    ActionFlags Flags,
    IReadOnlyList<Tile> DiscardableTiles,
    IReadOnlyList<MeldCandidate> PonCandidates,
    IReadOnlyList<MeldCandidate> ChiCandidates,
    IReadOnlyList<MeldCandidate> KanCandidates)
{
    public IReadOnlyList<Tile> DiscardableTiles { get; init; } = [.. DiscardableTiles];
    public IReadOnlyList<MeldCandidate> PonCandidates { get; init; } = [.. PonCandidates];
    public IReadOnlyList<MeldCandidate> ChiCandidates { get; init; } = [.. ChiCandidates];
    public IReadOnlyList<MeldCandidate> KanCandidates { get; init; } = [.. KanCandidates];

    public static LegalActions None { get; } = new(ActionFlags.None, [], [], [], []);

    public bool Can(ActionFlags flag) => (Flags & flag) != 0;
}

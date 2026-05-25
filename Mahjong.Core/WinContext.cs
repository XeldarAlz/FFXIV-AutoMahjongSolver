namespace Mahjong.Core;

public enum WinKind
{
    Tsumo,
    Ron,
}

/// <summary>
/// Situational context for scoring. Wind tile ids: 27=E, 28=S, 29=W, 30=N.
/// </summary>
/// <param name="AkaDora">Red-5 count, side-channel so Tile stays 1-byte. Scorer ignores for yakuman.</param>
public sealed record WinContext(
    Tile WinningTile,
    WinKind Kind,
    bool IsRiichi = false,
    bool IsDoubleRiichi = false,
    bool IsIppatsu = false,
    bool IsRinshan = false,
    bool IsChankan = false,
    bool IsHaitei = false,
    bool IsHoutei = false,
    bool IsTenhou = false,
    bool IsChihou = false,
    int RoundWindTileId = 27,
    int SeatWindTileId = 27,
    IReadOnlyList<Tile>? DoraIndicators = null,
    IReadOnlyList<Tile>? UraDoraIndicators = null,
    bool IsDealer = false,
    int AkaDora = 0)
{
    public IReadOnlyList<Tile>? DoraIndicators { get; init; }
        = DoraIndicators is null ? null : [.. DoraIndicators];

    public IReadOnlyList<Tile>? UraDoraIndicators { get; init; }
        = UraDoraIndicators is null ? null : [.. UraDoraIndicators];

    public IReadOnlyList<Tile> Dora => DoraIndicators ?? [];
    public IReadOnlyList<Tile> UraDora => UraDoraIndicators ?? [];

    public Tile RoundWind => Tile.FromId(RoundWindTileId);
    public Tile SeatWind => Tile.FromId(SeatWindTileId);

    public bool IsTsumo => Kind == WinKind.Tsumo;
    public bool IsRon => Kind == WinKind.Ron;
}

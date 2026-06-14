namespace AutoMahjongSolver.Engine;

/// <summary>
/// Everything about the table state, beyond the tiles themselves, that affects the value of a win.
/// Winds are 0-3 (East..North). Dora/ura indicator lists hold the indicator tiles, not the dora.
/// </summary>
public readonly record struct WinContext
{
    public required int WinTile { get; init; }
    public bool Tsumo { get; init; }

    public bool Riichi { get; init; }
    public bool DoubleRiichi { get; init; }
    public bool Ippatsu { get; init; }
    public bool Haitei { get; init; }
    public bool Houtei { get; init; }
    public bool Rinshan { get; init; }
    public bool Chankan { get; init; }

    public int SeatWind { get; init; }
    public int RoundWind { get; init; }

    public int AkaCount { get; init; }
    public IReadOnlyList<int>? DoraIndicators { get; init; }
    public IReadOnlyList<int>? UraIndicators { get; init; }
}

/// <summary>
/// The best-value interpretation of a winning hand. <see cref="YakuHanTotal"/> of 0 means the hand
/// has no yaku and cannot legally win (dora never qualify a hand on their own).
/// </summary>
public sealed class WinResult
{
    public required IReadOnlyList<YakuHan> Yaku { get; init; }

    /// <summary>Han from yaku alone; 13 for a yakuman regardless of how many stacked.</summary>
    public required int YakuHanTotal { get; init; }

    /// <summary>Dora + aka + ura han. Always 0 for a yakuman.</summary>
    public required int DoraHan { get; init; }

    public required bool IsYakuman { get; init; }

    public int TotalHan => YakuHanTotal + DoraHan;

    public bool HasYaku => YakuHanTotal > 0;
}

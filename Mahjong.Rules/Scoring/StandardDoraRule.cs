namespace Mahjong.Rules.Scoring;

/// <summary>
/// Standard dora cycle:
///   * Suited tiles cycle within their suit: 9m → 1m, 5p → 6p, etc.
///   * Winds cycle East → South → West → North → East.
///   * Dragons cycle Haku → Hatsu → Chun → Haku.
/// </summary>
public sealed class StandardDoraRule : IDoraRule
{
    public Tile Next(Tile indicator)
    {
        int id = indicator.Id;
        if (id < TileIds.HonorStart)
        {
            int suit = id / TileIds.SuitSize;
            int num = id % TileIds.SuitSize;
            return Tile.FromId(suit * TileIds.SuitSize + (num + 1) % TileIds.SuitSize);
        }

        if (id <= TileIds.LastWind)
        {
            int windIdx = id - TileIds.FirstWind;
            return Tile.FromId(TileIds.FirstWind + (windIdx + 1) % 4);
        }

        int dragonIdx = id - TileIds.FirstDragon;
        return Tile.FromId(TileIds.FirstDragon + (dragonIdx + 1) % 3);
    }
}

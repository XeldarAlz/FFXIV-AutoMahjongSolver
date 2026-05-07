namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Yakuhai (1 han per qualifying triplet/kan).
///
/// Emits up to five hits per hand:
///   * Haku, Hatsu, Chun (dragons)
///   * Round wind triplet
///   * Seat wind triplet
///
/// One physical group can contribute two hits (e.g. a triplet of East when both
/// the round wind and the seat wind are East — a "double East").
///
/// The Definition's <see cref="YakuDefinition.Id"/> is a placeholder; each emitted
/// hit carries the specific yaku id (YakuhaiHaku / YakuhaiSeat / etc.).
/// </summary>
public sealed class YakuhaiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.YakuhaiHaku,    // representative; rule emits the right id per hit
        Name: "Yakuhai",
        ClosedHan: 1,
        OpenHan: 1);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        var hits = new List<YakuHit>(2);
        foreach (var g in d.Groups)
        {
            if (g.Kind is not (GroupKind.Triplet or GroupKind.Kan))
                continue;
            AddDragonHit(g.First, hits);
            AddWindHits(g.First, ctx, hits);
        }
        return hits;
    }

    private static void AddDragonHit(Tile anchor, List<YakuHit> hits)
    {
        var id = anchor.Id;
        if (id == TileIds.Haku)
            hits.Add(new YakuHit(Mahjong.Core.Yaku.YakuhaiHaku, 1));
        else if (id == TileIds.Hatsu)
            hits.Add(new YakuHit(Mahjong.Core.Yaku.YakuhaiHatsu, 1));
        else if (id == TileIds.Chun)
            hits.Add(new YakuHit(Mahjong.Core.Yaku.YakuhaiChun, 1));
    }

    private static void AddWindHits(Tile anchor, WinContext ctx, List<YakuHit> hits)
    {
        if (!anchor.IsWind)
            return;
        if (anchor.Id == ctx.RoundWindTileId)
            hits.Add(new YakuHit(Mahjong.Core.Yaku.YakuhaiRound, 1));
        if (anchor.Id == ctx.SeatWindTileId)
            hits.Add(new YakuHit(Mahjong.Core.Yaku.YakuhaiSeat, 1));
    }
}

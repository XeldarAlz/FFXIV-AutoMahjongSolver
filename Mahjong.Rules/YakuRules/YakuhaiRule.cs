namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Up to five hits per hand (dragons + round + seat). A double East triplet (round=seat=E)
/// contributes two hits from one group. Each hit carries the specific yaku id; Definition.Id
/// is just a representative.
/// </summary>
public sealed class YakuhaiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.YakuhaiHaku,
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

namespace Mahjong.Rules.YakuRules.Yakuman;

public sealed class RyuuiisouRule : IYakuRule
{
    private static readonly int[] AllowedIds = [19, 20, 21, 23, 25, TileIds.Hatsu];

    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Ryuuiisou,
        Name: "Ryuuiisou",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        foreach (var g in d.Groups)
        {
            foreach (var t in g.Tiles)
            {
                if (!IsAllowed(t.Id))
                    return [];
            }
        }
        return [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)];
    }

    private static bool IsAllowed(int id)
    {
        foreach (int allowed in AllowedIds)
        {
            if (allowed == id)
                return true;
        }
        return false;
    }
}

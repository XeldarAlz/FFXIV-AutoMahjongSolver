namespace Mahjong.Rules.YakuRules.Yakuman;

/// <summary>
/// Ryuuiisou (yakuman). All "green" tiles — 2s, 3s, 4s, 6s, 8s + hatsu (green dragon).
/// </summary>
public sealed class RyuuiisouRule : IYakuRule
{
    /// <summary>
    /// 34-space ids of the only tiles allowed: 2s=19, 3s=20, 4s=21, 6s=23, 8s=25, hatsu=32.
    /// </summary>
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

namespace Mahjong.Rules.YakuRules.Yakuman;

/// <summary>
/// Tsuuiisou (yakuman). All honors. Runs are impossible (honors can't sequence).
/// </summary>
public sealed class TsuuiisouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Tsuuiisou,
        Name: "Tsuuiisou",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        foreach (var g in d.Groups)
        {
            if (!g.First.IsHonor)
                return [];
        }
        return [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)];
    }
}

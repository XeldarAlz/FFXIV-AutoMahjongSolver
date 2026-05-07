namespace Mahjong.Rules.YakuRules.Yakuman;

/// <summary>
/// Chinroutou (yakuman). All terminals — no simples, no honors. Runs are impossible.
/// </summary>
public sealed class ChinroutouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Chinroutou,
        Name: "Chinroutou",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        foreach (var g in d.Groups)
        {
            if (g.Kind == GroupKind.Run)
                return [];
            if (g.First.IsHonor)
                return [];
            if (!g.First.IsTerminal)
                return [];
        }
        return [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)];
    }
}

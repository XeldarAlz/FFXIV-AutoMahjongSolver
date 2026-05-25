namespace Mahjong.Rules.YakuRules;

public sealed class HonroutouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Honroutou,
        Name: "Honroutou",
        ClosedHan: 2,
        OpenHan: 2);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        foreach (var g in d.Groups)
        {
            if (g.Kind == GroupKind.Run)
                return [];
            if (!g.First.IsTerminalOrHonor)
                return [];
        }
        return [new YakuHit(Definition.Id, 2)];
    }
}

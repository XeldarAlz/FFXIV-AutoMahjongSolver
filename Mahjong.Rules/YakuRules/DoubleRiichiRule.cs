namespace Mahjong.Rules.YakuRules;

public sealed class DoubleRiichiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.DoubleRiichi,
        Name: "Double Riichi",
        ClosedHan: 2,
        OpenHan: 0,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (!ctx.IsDoubleRiichi || !d.IsMenzen)
            return [];
        return [new YakuHit(Definition.Id, Definition.ClosedHan)];
    }
}

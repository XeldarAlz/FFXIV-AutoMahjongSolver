namespace Mahjong.Rules.YakuRules;

public sealed class HouteiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Houtei,
        Name: "Houtei",
        ClosedHan: 1,
        OpenHan: 1);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
        => ctx.IsHoutei ? [new YakuHit(Definition.Id, 1)] : [];
}

namespace Mahjong.Rules.YakuRules;

public sealed class HaiteiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Haitei,
        Name: "Haitei",
        ClosedHan: 1,
        OpenHan: 1);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
        => ctx.IsHaitei ? [new YakuHit(Definition.Id, 1)] : [];
}

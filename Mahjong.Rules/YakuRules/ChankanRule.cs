namespace Mahjong.Rules.YakuRules;

public sealed class ChankanRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Chankan,
        Name: "Chankan",
        ClosedHan: 1,
        OpenHan: 1);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
        => ctx.IsChankan ? [new YakuHit(Definition.Id, 1)] : [];
}

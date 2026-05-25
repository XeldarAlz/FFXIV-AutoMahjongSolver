namespace Mahjong.Rules.YakuRules;

public sealed class RinshanRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Rinshan,
        Name: "Rinshan",
        ClosedHan: 1,
        OpenHan: 1);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
        => ctx.IsRinshan ? [new YakuHit(Definition.Id, 1)] : [];
}

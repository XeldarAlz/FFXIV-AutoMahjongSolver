namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Houtei Raoyui (1 han). Ron on the final discard of the round.
/// </summary>
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

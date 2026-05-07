namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Chankan (1 han). Robbing a kan — win on a tile an opponent is upgrading
/// from pon to kan.
/// </summary>
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

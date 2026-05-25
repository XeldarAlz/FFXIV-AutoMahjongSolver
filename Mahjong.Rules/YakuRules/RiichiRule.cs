namespace Mahjong.Rules.YakuRules;

/// <summary>Deliberately silent when DoubleRiichi fires.</summary>
public sealed class RiichiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Riichi,
        Name: "Riichi",
        ClosedHan: 1,
        OpenHan: 0,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (!ctx.IsRiichi || ctx.IsDoubleRiichi || !d.IsMenzen)
            return [];
        return [new YakuHit(Definition.Id, Definition.ClosedHan)];
    }
}

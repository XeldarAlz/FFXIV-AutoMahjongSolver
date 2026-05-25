namespace Mahjong.Rules.YakuRules;

public sealed class MenzenTsumoRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.MenzenTsumo,
        Name: "Menzen Tsumo",
        ClosedHan: 1,
        OpenHan: 0,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (!d.IsMenzen || !ctx.IsTsumo)
            return [];
        return [new YakuHit(Definition.Id, Definition.ClosedHan)];
    }
}

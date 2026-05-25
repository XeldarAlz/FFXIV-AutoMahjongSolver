namespace Mahjong.Rules.YakuRules;

public sealed class IppatsuRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Ippatsu,
        Name: "Ippatsu",
        ClosedHan: 1,
        OpenHan: 0,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (!ctx.IsIppatsu || !d.IsMenzen || !(ctx.IsRiichi || ctx.IsDoubleRiichi))
            return [];
        return [new YakuHit(Definition.Id, Definition.ClosedHan)];
    }
}

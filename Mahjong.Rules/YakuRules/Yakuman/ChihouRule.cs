namespace Mahjong.Rules.YakuRules.Yakuman;

public sealed class ChihouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Chihou,
        Name: "Chihou",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
        => ctx.IsChihou
            ? [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)]
            : [];
}

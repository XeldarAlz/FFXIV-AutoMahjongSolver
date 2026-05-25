namespace Mahjong.Rules.YakuRules.Yakuman;

public sealed class KokushiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Kokushi,
        Name: "Kokushi Musou",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
        => d.Form == DecompositionForm.Kokushi
            ? [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)]
            : [];
}

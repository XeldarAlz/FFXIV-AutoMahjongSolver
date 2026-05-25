namespace Mahjong.Rules.YakuRules.Yakuman;

public sealed class SuukantsuRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Suukantsu,
        Name: "Suukantsu",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        return d.KanCount == 4
            ? [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)]
            : [];
    }
}

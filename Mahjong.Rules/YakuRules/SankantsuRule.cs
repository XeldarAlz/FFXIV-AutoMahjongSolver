namespace Mahjong.Rules.YakuRules;

public sealed class SankantsuRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Sankantsu,
        Name: "Sankantsu",
        ClosedHan: 2,
        OpenHan: 2);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        return d.KanCount == 3
            ? [new YakuHit(Definition.Id, 2)]
            : [];
    }
}

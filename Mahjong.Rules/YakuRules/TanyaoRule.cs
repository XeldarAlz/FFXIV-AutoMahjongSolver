namespace Mahjong.Rules.YakuRules;

public sealed class TanyaoRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Tanyao,
        Name: "Tanyao",
        ClosedHan: 1,
        OpenHan: 1);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form == DecompositionForm.Kokushi)
            return [];

        foreach (var g in d.Groups)
        {
            if (g.ContainsTerminalOrHonor)
                return [];
        }
        return [new YakuHit(Definition.Id, 1)];
    }
}

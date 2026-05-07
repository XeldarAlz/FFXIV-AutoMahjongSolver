namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Tanyao (1 han). All-simples — no terminals (1, 9), no honors anywhere in the hand.
/// </summary>
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

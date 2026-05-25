namespace Mahjong.Rules.YakuRules;

public sealed class ChantaRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Chanta,
        Name: "Chanta",
        ClosedHan: 2,
        OpenHan: 1);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        bool hasRun = false;
        foreach (var g in d.Groups)
        {
            if (!g.ContainsTerminalOrHonor)
                return [];
            if (g.Kind == GroupKind.Run)
                hasRun = true;
        }

        return hasRun
            ? [new YakuHit(Definition.Id, Definition.Han(d.IsMenzen))]
            : [];
    }
}

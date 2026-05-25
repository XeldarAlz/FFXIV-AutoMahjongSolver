namespace Mahjong.Rules.YakuRules;

public sealed class JunchanRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Junchan,
        Name: "Junchan",
        ClosedHan: 3,
        OpenHan: 2);

    public IReadOnlyList<Mahjong.Core.Yaku> Conflicts { get; } = [Mahjong.Core.Yaku.Chanta];

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        bool hasRun = false;
        foreach (var g in d.Groups)
        {
            if (!g.ContainsTerminalOrHonor)
                return [];
            if (g.First.IsHonor)
                return [];
            if (g.Kind == GroupKind.Run)
                hasRun = true;
        }

        return hasRun
            ? [new YakuHit(Definition.Id, Definition.Han(d.IsMenzen))]
            : [];
    }
}

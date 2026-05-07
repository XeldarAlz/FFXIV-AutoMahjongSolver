namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Chanta (2 han closed / 1 han open). Every group (including the pair) contains
/// a terminal or honor, AND the hand has at least one run (otherwise it would
/// be Honroutou or Chinroutou, scored separately).
///
/// Junchan (3/2 han) is the strict variant — same shape but no honors anywhere
/// — and supersedes Chanta when both fire. The supersession is declared on
/// JunchanRule.Conflicts; this rule fires unconditionally on a chanta-shaped hand.
/// </summary>
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

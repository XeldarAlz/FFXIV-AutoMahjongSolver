namespace Mahjong.Rules.YakuRules.Yakuman;

/// <summary>
/// Daisangen (yakuman). All three dragon triplets (or kans).
/// </summary>
public sealed class DaisangenRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Daisangen,
        Name: "Daisangen",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        int dragonTrios = 0;
        foreach (var g in d.Groups)
        {
            if (g.Kind is GroupKind.Triplet or GroupKind.Kan && g.First.IsDragon)
                dragonTrios++;
        }

        return dragonTrios == 3
            ? [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)]
            : [];
    }
}

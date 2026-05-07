namespace Mahjong.Rules.YakuRules.Yakuman;

/// <summary>
/// Shousuushii (yakuman). Three wind triplets/kans plus a wind pair.
/// Mutually exclusive with Daisuushii by hand shape.
/// </summary>
public sealed class ShousuushiiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Shousuushii,
        Name: "Shousuushii",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        int windTrios = 0;
        bool windPair = false;
        foreach (var g in d.Groups)
        {
            if (g.Kind is GroupKind.Triplet or GroupKind.Kan && g.First.IsWind)
                windTrios++;
            else if (g.Kind == GroupKind.Pair && g.First.IsWind)
                windPair = true;
        }

        return windTrios == 3 && windPair
            ? [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)]
            : [];
    }
}

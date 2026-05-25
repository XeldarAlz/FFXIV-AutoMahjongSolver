namespace Mahjong.Rules.YakuRules.Yakuman;

public sealed class DaisuushiiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Daisuushii,
        Name: "Daisuushii",
        ClosedHan: HanValues.DoubleYakuman,
        OpenHan: HanValues.DoubleYakuman,
        IsYakuman: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        int windTrios = 0;
        foreach (var g in d.Groups)
        {
            if (g.Kind is GroupKind.Triplet or GroupKind.Kan && g.First.IsWind)
                windTrios++;
        }

        return windTrios == 4
            ? [new YakuHit(Definition.Id, HanValues.DoubleYakuman, IsYakuman: true)]
            : [];
    }
}

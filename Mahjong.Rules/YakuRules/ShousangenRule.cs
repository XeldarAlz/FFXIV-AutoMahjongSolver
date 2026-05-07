namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Shousangen (2 han). Two dragon triplets/kans plus a dragon pair — i.e.
/// "small three dragons." If you have all three dragon triplets it's
/// Daisangen (yakuman), not this.
/// </summary>
public sealed class ShousangenRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Shousangen,
        Name: "Shousangen",
        ClosedHan: 2,
        OpenHan: 2);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        int dragonTrios = 0;
        bool dragonPair = false;
        foreach (var g in d.Groups)
        {
            if (g.Kind is GroupKind.Triplet or GroupKind.Kan && g.First.IsDragon)
                dragonTrios++;
            else if (g.Kind == GroupKind.Pair && g.First.IsDragon)
                dragonPair = true;
        }
        return dragonTrios == 2 && dragonPair
            ? [new YakuHit(Definition.Id, 2)]
            : [];
    }
}

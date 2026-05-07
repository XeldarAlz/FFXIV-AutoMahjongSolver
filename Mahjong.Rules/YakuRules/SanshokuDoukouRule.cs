namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Sanshoku Doukou (2 han). Same triplet (or kan) in all three suits — e.g.
/// 555m + 555p + 555s.
/// </summary>
public sealed class SanshokuDoukouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.SanshokuDoukou,
        Name: "Sanshoku Doukou",
        ClosedHan: 2,
        OpenHan: 2);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        var trios = new HashSet<int>();
        foreach (var g in d.Groups)
        {
            if (g.Kind is GroupKind.Triplet or GroupKind.Kan)
                trios.Add(g.First.Id);
        }

        for (int n = 0; n < TileIds.SuitSize; n++)
        {
            if (trios.Contains(TileIds.ManStart + n)
                && trios.Contains(TileIds.PinStart + n)
                && trios.Contains(TileIds.SouStart + n))
            {
                return [new YakuHit(Definition.Id, 2)];
            }
        }
        return [];
    }
}

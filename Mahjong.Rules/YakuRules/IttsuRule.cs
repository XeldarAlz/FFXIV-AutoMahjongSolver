namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Ittsu (2 han closed / 1 han open). Straight 1-9 in a single suit:
/// 123 + 456 + 789 of m, p, or s.
/// </summary>
public sealed class IttsuRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Ittsu,
        Name: "Ittsu",
        ClosedHan: 2,
        OpenHan: 1);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        var runStarts = new HashSet<int>();
        foreach (var g in d.Groups)
        {
            if (g.Kind == GroupKind.Run)
                runStarts.Add(g.First.Id);
        }

        ReadOnlySpan<int> suitBases = [TileIds.ManStart, TileIds.PinStart, TileIds.SouStart];
        foreach (int suitBase in suitBases)
        {
            if (runStarts.Contains(suitBase)
                && runStarts.Contains(suitBase + 3)
                && runStarts.Contains(suitBase + 6))
            {
                return [new YakuHit(Definition.Id, Definition.Han(d.IsMenzen))];
            }
        }
        return [];
    }
}

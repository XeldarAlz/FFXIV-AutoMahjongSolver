namespace Mahjong.Rules.YakuRules;

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

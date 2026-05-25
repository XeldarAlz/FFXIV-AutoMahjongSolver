namespace Mahjong.Rules.YakuRules;

public sealed class SanankouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Sanankou,
        Name: "Sanankou",
        ClosedHan: 2,
        OpenHan: 2);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        int ankouCount = d.ConcealedTripletCount + CountConcealedKans(d);
        return ankouCount == 3
            ? [new YakuHit(Definition.Id, 2)]
            : [];
    }

    private static int CountConcealedKans(Decomposition d)
    {
        int count = 0;
        foreach (var g in d.Groups)
        {
            if (g.IsConcealedKan)
                count++;
        }
        return count;
    }
}

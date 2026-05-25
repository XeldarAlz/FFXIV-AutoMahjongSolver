namespace Mahjong.Rules.YakuRules.Yakuman;

public sealed class SuuankouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Suuankou,
        Name: "Suuankou",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        int ankou = d.ConcealedTripletCount + CountConcealedKans(d);
        return ankou == 4
            ? [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)]
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

namespace Mahjong.Rules.YakuRules;

public sealed class SanshokuDoujunRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.SanshokuDoujun,
        Name: "Sanshoku Doujun",
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

        for (int n = 0; n < TileIds.SuitSize - 2; n++)
        {
            if (runStarts.Contains(TileIds.ManStart + n)
                && runStarts.Contains(TileIds.PinStart + n)
                && runStarts.Contains(TileIds.SouStart + n))
            {
                return [new YakuHit(Definition.Id, Definition.Han(d.IsMenzen))];
            }
        }
        return [];
    }
}

namespace Mahjong.Rules.YakuRules;

public sealed class ToitoiRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Toitoi,
        Name: "Toitoi",
        ClosedHan: 2,
        OpenHan: 2);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        foreach (var g in d.Sets)
        {
            if (g.Kind == GroupKind.Run)
                return [];
        }
        return [new YakuHit(Definition.Id, 2)];
    }
}

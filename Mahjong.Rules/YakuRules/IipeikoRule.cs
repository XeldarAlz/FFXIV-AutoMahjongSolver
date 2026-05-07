namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Iipeiko (1 han, closed only). Two identical sequences in the same suit.
/// Superseded by Ryanpeikou (which finds two such pairs).
/// </summary>
public sealed class IipeikoRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Iipeiko,
        Name: "Iipeiko",
        ClosedHan: 1,
        OpenHan: 0,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (!d.IsMenzen || d.Form != DecompositionForm.Standard)
            return [];

        var seenRunStarts = new HashSet<int>();
        foreach (var g in d.Groups)
        {
            if (g.Kind != GroupKind.Run)
                continue;
            if (!seenRunStarts.Add(g.First.Id))
                return [new YakuHit(Definition.Id, 1)];
        }
        return [];
    }
}

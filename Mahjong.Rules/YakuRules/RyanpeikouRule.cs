namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Ryanpeikou (3 han, closed only). Two pairs of identical sequences in same suit.
/// Supersedes Iipeiko (which would otherwise also fire).
/// </summary>
public sealed class RyanpeikouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Ryanpeikou,
        Name: "Ryanpeikou",
        ClosedHan: 3,
        OpenHan: 0,
        RequiresMenzen: true);

    public IReadOnlyList<Mahjong.Core.Yaku> Conflicts { get; } = [Mahjong.Core.Yaku.Iipeiko];

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard || !d.IsMenzen)
            return [];

        var runIds = new List<int>(4);
        foreach (var g in d.Groups)
        {
            if (g.Kind == GroupKind.Run)
                runIds.Add(g.First.Id);
        }
        if (runIds.Count != 4)
            return [];

        runIds.Sort();
        bool firstPair = runIds[0] == runIds[1];
        bool secondPair = runIds[2] == runIds[3];
        bool distinct = runIds[0] != runIds[2];

        return firstPair && secondPair && distinct
            ? [new YakuHit(Definition.Id, Definition.ClosedHan)]
            : [];
    }
}

namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Pinfu (1 han, closed only). All sequences, valueless pair, two-sided (ryanmen) wait,
/// no fu beyond the base.
///
/// Detection breaks down to four checks:
///   1. Standard form, closed.
///   2. Every non-pair group is a sequence (no triplets / kans).
///   3. The pair is not yakuhai (round wind, seat wind, or dragon).
///   4. The wait that completed the hand is ryanmen — kanchan / penchan reject.
/// </summary>
public sealed class PinfuRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Pinfu,
        Name: "Pinfu",
        ClosedHan: 1,
        OpenHan: 0,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard || !d.IsMenzen)
            return [];

        if (!AllNonPairGroupsAreRuns(d))
            return [];

        if (PairIsYakuhai(d, ctx))
            return [];

        if (!WaitIsRyanmen(d, ctx))
            return [];

        return [new YakuHit(Definition.Id, Definition.ClosedHan)];
    }

    private static bool AllNonPairGroupsAreRuns(Decomposition d)
    {
        foreach (var g in d.Sets)
        {
            if (g.Kind != GroupKind.Run)
                return false;
        }
        return true;
    }

    private static bool PairIsYakuhai(Decomposition d, WinContext ctx)
    {
        var pair = d.Pair.First;
        if (pair.IsDragon)
            return true;
        if (pair.Id == ctx.RoundWindTileId)
            return true;
        if (pair.Id == ctx.SeatWindTileId)
            return true;
        return false;
    }

    private static bool WaitIsRyanmen(Decomposition d, WinContext ctx)
    {
        Group? completing = null;
        foreach (var g in d.Groups)
        {
            if (g.IsCompletedByWinningTile)
            {
                completing = g;
                break;
            }
        }
        if (completing is null || completing.Value.Kind != GroupKind.Run)
            return false;

        var run = completing.Value;
        int offset = ctx.WinningTile.Id - run.First.Id;       // 0, 1, or 2
        int firstMod = run.First.Id % TileIds.SuitSize;

        if (offset == 1)
            return false;                                  // kanchan
        if (offset == 2 && firstMod == 0)
            return false;                  // penchan 12 → 3
        if (offset == 0 && firstMod == TileIds.SuitSize - 3)
            return false; // penchan 89 → 7
        return true;
    }
}

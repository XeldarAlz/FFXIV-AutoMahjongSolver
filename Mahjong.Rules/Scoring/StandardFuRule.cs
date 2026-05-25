namespace Mahjong.Rules.Scoring;

/// <summary>
/// Chiitoitsu=25, Pinfu tsumo=20, Pinfu ron=30. Otherwise 20 base + bonuses, rounded up to 10.
/// </summary>
public sealed class StandardFuRule : IFuRule
{
    private const int Base = 20;
    private const int TsumoBonus = 2;
    private const int MenzenKafu = 10;
    private const int RoundStep = 10;
    private const int PinfuTsumoFu = 20;
    private const int PinfuRonFu = 30;
    private const int ChiitoitsuFu = 25;
    private const int KokushiFu = 30;
    private const int YakuhaiPairFu = 2;
    private const int SingleWaitFu = 2;

    private const int OpenTripletSimpleFu = 2;
    private const int OpenTripletTermHonorFu = 4;
    private const int ClosedTripletSimpleFu = 4;
    private const int ClosedTripletTermHonorFu = 8;
    private const int OpenKanSimpleFu = 8;
    private const int OpenKanTermHonorFu = 16;
    private const int ClosedKanSimpleFu = 16;
    private const int ClosedKanTermHonorFu = 32;

    public int Compute(Decomposition d, WinContext ctx, IReadOnlyList<YakuHit> yaku)
    {
        if (d.Form == DecompositionForm.Chiitoitsu)
            return ChiitoitsuFu;
        if (d.Form == DecompositionForm.Kokushi)
            return KokushiFu;

        bool isPinfu = ContainsYaku(yaku, Yaku.Pinfu);
        if (isPinfu)
            return ctx.IsTsumo ? PinfuTsumoFu : PinfuRonFu;

        int fu = Base;
        if (ctx.IsTsumo)
            fu += TsumoBonus;
        if (d.IsMenzen && ctx.IsRon)
            fu += MenzenKafu;

        foreach (var g in d.Groups)
            fu += GroupFu(g, ctx, d.WinningTileFromOpponent);

        fu += WaitFu(d, ctx);

        int rem = fu % RoundStep;
        return rem == 0 ? fu : fu + (RoundStep - rem);
    }

    private static bool ContainsYaku(IReadOnlyList<YakuHit> yaku, Yaku target)
    {
        foreach (var y in yaku)
            if (y.Yaku == target)
                return true;
        return false;
    }

    private static int GroupFu(Group g, WinContext ctx, bool winFromOpponent)
    {
        return g.Kind switch
        {
            GroupKind.Run => 0,
            GroupKind.Pair => PairFu(g, ctx),
            GroupKind.Triplet => TripletFu(g, winFromOpponent),
            GroupKind.Kan => KanFu(g),
            _ => 0,
        };
    }

    private static int PairFu(Group g, WinContext ctx)
    {
        int fu = 0;
        if (g.First.IsDragon)
            fu += YakuhaiPairFu;
        if (g.First.IsWind)
        {
            if (g.First.Id == ctx.RoundWindTileId)
                fu += YakuhaiPairFu;
            if (g.First.Id == ctx.SeatWindTileId)
                fu += YakuhaiPairFu;
        }
        return fu;
    }

    private static int TripletFu(Group g, bool winFromOpponent)
    {
        bool effectiveOpen = g.IsOpen || (g.IsCompletedByWinningTile && winFromOpponent);
        bool termHonor = g.First.IsTerminalOrHonor;
        return effectiveOpen
            ? (termHonor ? OpenTripletTermHonorFu : OpenTripletSimpleFu)
            : (termHonor ? ClosedTripletTermHonorFu : ClosedTripletSimpleFu);
    }

    private static int KanFu(Group g)
    {
        bool termHonor = g.First.IsTerminalOrHonor;
        return g.IsOpen
            ? (termHonor ? OpenKanTermHonorFu : OpenKanSimpleFu)
            : (termHonor ? ClosedKanTermHonorFu : ClosedKanSimpleFu);
    }

    private static int WaitFu(Decomposition d, WinContext ctx)
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
        if (completing is null)
            return 0;

        var c = completing.Value;
        return c.Kind switch
        {
            GroupKind.Pair => SingleWaitFu,
            GroupKind.Run => RunWaitFu(c, ctx.WinningTile),
            _ => 0,
        };
    }

    private static int RunWaitFu(Group run, Tile winTile)
    {
        int first = run.First.Id;
        int firstMod = first % TileIds.SuitSize;

        if (winTile.Id == first + 1)
            return SingleWaitFu;
        if (winTile.Id == first + 2 && firstMod == 0)
            return SingleWaitFu;
        if (winTile.Id == first && firstMod == TileIds.SuitSize - 3)
            return SingleWaitFu;
        return 0;
    }
}

using Mahjong.Engine;

namespace Mahjong.Policy.Opponents;

/// <summary>Opponents indexed relative to self: 0=shimocha, 1=toimen, 2=kamicha.</summary>
public sealed class OpponentModel : IOpponentModel
{
    public const int OpponentCountConst = 3;
    private const int TileKinds = Tile.Count34;

    private readonly OpponentWeights weights;

    public double[] TenpaiProb { get; } = new double[OpponentCountConst];
    public double[][] HandMarginal { get; } = new double[OpponentCountConst][];
    public double[][] DangerMap { get; } = new double[OpponentCountConst][];
    public double[] ExpectedHandValue { get; } = new double[OpponentCountConst];

    public int OpponentCount => OpponentCountConst;

    public OpponentModel() : this(OpponentWeights.Default) { }

    public OpponentModel(OpponentWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        this.weights = weights;
        for (int i = 0; i < OpponentCountConst; i++)
        {
            HandMarginal[i] = new double[TileKinds];
            DangerMap[i] = new double[TileKinds];
            ExpectedHandValue[i] = weights.ExpectedHandValue;
        }
    }

    public double TenpaiProbability(int opponentIndex) => TenpaiProb[opponentIndex];

    public void Update(StateSnapshot state)
    {
        UpdateTenpaiProbabilities(state);
        UpdateHandMarginals(state);
        UpdateDangerMap(state);
    }

    private void UpdateTenpaiProbabilities(StateSnapshot state)
    {
        for (int opp = 0; opp < OpponentCountConst; opp++)
        {
            int absSeat = (state.OurSeat + 1 + opp) % 4;
            var seat = state.Seats[absSeat];

            if (seat.Riichi)
            {
                TenpaiProb[opp] = 1.0;
                continue;
            }

            double discardCount = seat.DiscardCount > 0 ? seat.DiscardCount : seat.Discards.Count;
            double meldCount = seat.Melds.Count;
            int turnsElapsed = 70 - state.WallRemaining;
            int lateTedashi = CountLateTedashi(seat);

            double z = weights.TenpaiIntercept
                     + weights.TenpaiDiscardCount * discardCount
                     + weights.TenpaiMeldCount * meldCount
                     + weights.TenpaiTurnsElapsed * turnsElapsed
                     + weights.TenpaiLateTedashi * lateTedashi;
            TenpaiProb[opp] = Sigmoid(z);
        }
    }

    private static int CountLateTedashi(SeatView seat)
    {
        int count = 0;
        int n = Math.Min(seat.Discards.Count, seat.DiscardIsTedashi.Count);
        for (int i = 6; i < n; i++)
            if (seat.DiscardIsTedashi[i])
                count++;
        return count;
    }

    private void UpdateHandMarginals(StateSnapshot state)
    {
        ComputeLiveTileCounts(state, out var live);
        double unseenTotal = 0;
        for (int k = 0; k < TileKinds; k++)
            unseenTotal += live[k];

        double perTileBase = unseenTotal > 0 ? 13.0 / unseenTotal : 0.0;

        for (int opp = 0; opp < OpponentCountConst; opp++)
        {
            int absSeat = (state.OurSeat + 1 + opp) % 4;
            var seat = state.Seats[absSeat];

            for (int k = 0; k < TileKinds; k++)
            {
                double p = live[k] * perTileBase;

                if (ContainsTile(seat.Discards, k))
                    p = 0;
                else if (MeldContainsTile(seat.Melds, k))
                    p = 0;

                HandMarginal[opp][k] = Math.Clamp(p, 0.0, 1.0);
            }
        }
    }

    private void UpdateDangerMap(StateSnapshot state)
    {
        ComputeLiveTileCounts(state, out var live);
        UpdateExpectedHandValues(state);

        for (int opp = 0; opp < OpponentCountConst; opp++)
        {
            int absSeat = (state.OurSeat + 1 + opp) % 4;
            var seat = state.Seats[absSeat];
            double tenpai = TenpaiProb[opp];

            for (int k = 0; k < TileKinds; k++)
                DangerMap[opp][k] = ComputeDealInRisk(k, tenpai, seat, live);
        }
    }

    private void UpdateExpectedHandValues(StateSnapshot state)
    {
        var doraTileIds = new HashSet<int>();
        foreach (var ind in state.DoraIndicators)
            doraTileIds.Add(DoraNextId(ind.Id));

        for (int opp = 0; opp < OpponentCountConst; opp++)
        {
            int absSeat = (state.OurSeat + 1 + opp) % 4;
            var seat = state.Seats[absSeat];

            int visibleDora = 0;
            foreach (var m in seat.Melds)
                foreach (var t in m.Tiles)
                    if (doraTileIds.Contains(t.Id))
                        visibleDora++;

            double bump = weights.ExpectedHandValuePerVisibleDora * visibleDora;
            if (seat.Riichi)
                bump += 1000.0;
            ExpectedHandValue[opp] = weights.ExpectedHandValue + bump;
        }
    }

    private static int DoraNextId(int id)
    {
        if (id < 27)
            return (id / 9) * 9 + (id % 9 + 1) % 9;
        if (id <= 30)
            return 27 + (id - 27 + 1) % 4;
        return 31 + (id - 31 + 1) % 3;
    }

    private double ComputeDealInRisk(int tileId, double tenpai, SeatView seat, int[] live)
    {
        if (ContainsTile(seat.Discards, tileId))
            return 0.0;
        if (live[tileId] == 0)
            return 0.0;

        double risk = tenpai * weights.TenpaiBaseDealInRate;
        if (HasSujiBlock(tileId, seat))
            risk *= weights.SujiDiscount;
        if (HasKabeBlock(tileId, live))
            risk *= weights.KabeDiscount;
        if (IsKanchanBlocked(tileId, live))
            risk *= weights.KanchanBlockDiscount;
        if (IsNoChance(tileId, live))
            risk *= weights.NoChanceDiscount;

        return Math.Clamp(risk, 0.0, 1.0);
    }

    /// <summary>Adjacent number-tile fully visible — collapses some ryanmen and kanchan waits onto T.</summary>
    private static bool HasKabeBlock(int tileId, int[] live)
    {
        if (tileId >= 27) return false;
        int pos = tileId % 9;
        int suitBase = (tileId / 9) * 9;
        if (pos > 0 && live[suitBase + pos - 1] == 0) return true;
        if (pos < 8 && live[suitBase + pos + 1] == 0) return true;
        return false;
    }

    /// <summary>Kanchan-on-T impossible when at least one bridge tile (T-1 or T+1) is dead.</summary>
    private static bool IsKanchanBlocked(int tileId, int[] live)
    {
        if (tileId >= 27) return false;
        int pos = tileId % 9;
        if (pos == 0 || pos == 8) return false;
        int suitBase = (tileId / 9) * 9;
        return live[suitBase + pos - 1] == 0 || live[suitBase + pos + 1] == 0;
    }

    /// <summary>Both ryanmen sides dead (T-2 and T+2 unreachable) — only shanpon/tanki/kanchan-pair remain.</summary>
    private static bool IsNoChance(int tileId, int[] live)
    {
        if (tileId >= 27) return false;
        int pos = tileId % 9;
        int suitBase = (tileId / 9) * 9;
        bool lowSideDead = pos < 2 || live[suitBase + pos - 2] == 0;
        bool highSideDead = pos > 6 || live[suitBase + pos + 2] == 0;
        return lowSideDead && highSideDead;
    }

    private static bool HasSujiBlock(int tileId, SeatView seat)
    {
        var tile = Tile.FromId(tileId);
        if (tile.IsHonor)
            return false;

        int pos = tileId % 9;
        int suitBase = (tileId / 9) * 9;
        int middle = pos switch
        {
            0 => suitBase + 3,
            8 => suitBase + 5,
            _ => -1,
        };
        if (middle < 0)
            return false;

        return ContainsTile(seat.Discards, middle);
    }

    public double ExpectedDealInCost(int tileId)
    {
        double total = 0;
        for (int opp = 0; opp < OpponentCountConst; opp++)
            total += DangerMap[opp][tileId] * ExpectedHandValue[opp];
        return total;
    }

    private static bool ContainsTile(IReadOnlyList<Tile> tiles, int tileId)
    {
        foreach (var t in tiles)
            if (t.Id == tileId)
                return true;
        return false;
    }

    private static bool MeldContainsTile(IReadOnlyList<Meld> melds, int tileId)
    {
        foreach (var m in melds)
            foreach (var t in m.Tiles)
                if (t.Id == tileId)
                    return true;
        return false;
    }

    private static void ComputeLiveTileCounts(StateSnapshot state, out int[] live)
    {
        var seen = new int[TileKinds];
        foreach (var t in state.Hand)
            seen[t.Id]++;
        foreach (var m in state.OurMelds)
            foreach (var t in m.Tiles)
                seen[t.Id]++;
        foreach (var seat in state.Seats)
        {
            foreach (var t in seat.Discards)
                seen[t.Id]++;
            foreach (var m in seat.Melds)
                foreach (var t in m.Tiles)
                    seen[t.Id]++;
        }
        foreach (var t in state.DoraIndicators)
            seen[t.Id]++;

        live = new int[TileKinds];
        for (int k = 0; k < TileKinds; k++)
            live[k] = Math.Max(0, Tile.CopiesPerKind - seen[k]);
    }

    private static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));
}

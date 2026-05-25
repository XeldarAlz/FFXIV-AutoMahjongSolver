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

            double z = weights.TenpaiIntercept
                     + weights.TenpaiDiscardCount * discardCount
                     + weights.TenpaiMeldCount * meldCount
                     + weights.TenpaiTurnsElapsed * turnsElapsed;
            TenpaiProb[opp] = Sigmoid(z);
        }
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

        for (int opp = 0; opp < OpponentCountConst; opp++)
        {
            int absSeat = (state.OurSeat + 1 + opp) % 4;
            var seat = state.Seats[absSeat];
            double tenpai = TenpaiProb[opp];

            for (int k = 0; k < TileKinds; k++)
                DangerMap[opp][k] = ComputeDealInRisk(k, tenpai, seat, live);
        }
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

        return Math.Clamp(risk, 0.0, 1.0);
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

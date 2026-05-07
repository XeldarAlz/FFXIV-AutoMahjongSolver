using Mahjong.Engine;

namespace Mahjong.Policy.Opponents;

/// <summary>
/// Rule-based Bayesian opponent model. Maintains per-opponent estimates of:
/// <list type="bullet">
///   <item><c>TenpaiProb[3]</c> — logistic-ish score on public evidence</item>
///   <item><c>HandMarginal[3][34]</c> — P(tile_k ∈ hand[opp]), factorized per-tile</item>
///   <item><c>DangerMap[3][34]</c> — P(deal-in | we discard k), composited from genbutsu/suji/kabe/tenpai</item>
/// </list>
/// Indexed relative to self (<c>state.OurSeat</c>): index 0=shimocha, 1=toimen, 2=kamicha.
///
/// Coefficients (logistic intercept and slopes, suji discount, base deal-in
/// rate, expected hand value) come from <see cref="OpponentWeights"/> — defaults
/// preserve the pre-Phase-3 hand-tuned values; calibration via the weight tuner
/// is outstanding work.
///
/// Phase 2 MVP note: discard pools aren't yet read from the game. Until they
/// are, TenpaiProb uses only wall-remaining and meld-count heuristics, and
/// DangerMap treats <b>our own discards</b> as the only reliable genbutsu.
/// </summary>
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

    /// <summary>
    /// Recompute all per-opponent estimates from the current snapshot. Pure — no
    /// cross-tick state. Cheap enough to call every decision point.
    /// </summary>
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

            // Riichi declared → tenpai certain.
            if (seat.Riichi)
            {
                TenpaiProb[opp] = 1.0;
                continue;
            }

            // Prefer the authoritative DiscardCount (pinned from addon memory)
            // over Discards.Count (which is 0 when the pool couldn't be resolved).
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

        // Each opponent holds ~13 tiles; each live tile has ~13/unseenTotal chance
        // of being in their hand.
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
        // Genbutsu: opponent already discarded this tile → 0% deal-in.
        if (ContainsTile(seat.Discards, tileId))
            return 0.0;

        // Kabe: 4 copies all visible → nobody can wait on it.
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
            0 => suitBase + 3,        // 1 → middle 4
            8 => suitBase + 5,        // 9 → middle 6
            _ => -1,
        };
        if (middle < 0)
            return false;

        return ContainsTile(seat.Discards, middle);
    }

    /// <summary>Sum of P(deal-in) × value across all opponents if we discard kind k.</summary>
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

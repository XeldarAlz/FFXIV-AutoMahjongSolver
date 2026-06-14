using AutoMahjongSolver.Engine;

namespace AutoMahjongSolver.Simulator;

internal sealed class SimPlayer
{
    public int[] Counts { get; } = new int[Tiles.TypeCount];
    public int[] AkaByType { get; } = new int[Tiles.TypeCount];
    public List<int> River { get; } = [];
    public List<Meld> Melds { get; } = [];
    public int MeldAka;
    public bool Riichi;
    public bool Ippatsu;

    public void Add(int tileId)
    {
        Counts[TileId.Type(tileId)]++;
        if (TileId.IsAka(tileId))
            AkaByType[TileId.Type(tileId)]++;
    }

    /// <summary>Remove one copy for a discard, preferring a plain copy (mirrors the live actuator). Returns true if the red one left.</summary>
    public bool RemoveForDiscard(int type)
    {
        Counts[type]--;
        if (AkaByType[type] > Counts[type])
        {
            AkaByType[type]--;
            return true;
        }

        return false;
    }

    /// <summary>Move two hand copies plus the claimed discard into a pon. Aka stay scorable via <see cref="MeldAka"/>.</summary>
    public void TakePon(int type, bool calledAka)
    {
        for (int n = 0; n < 2; n++)
        {
            Counts[type]--;
            if (AkaByType[type] > Counts[type])
            {
                AkaByType[type]--;
                MeldAka++;
            }
        }

        if (calledAka)
            MeldAka++;
        Melds.Add(new Meld(MeldKind.Pon, [type, type, type]));
    }
}

internal sealed record HandOutcome
{
    public int Winner { get; init; } = -1;
    public int DealIn { get; init; } = -1;
    public required int[] Deltas { get; init; }
    public bool ExhaustiveDraw { get; init; }
    public required bool DealerKeeps { get; init; }
    public required int SticksLeft { get; init; }
    public required bool[] RiichiDeclared { get; init; }
    public int WinHan { get; init; }
    public int WinPoints { get; init; }
}

/// <summary>
/// Plays one hand under the bot's ruleset: riichi with ippatsu/ura, haitei/houtei, river furiten,
/// pon calls gated by <see cref="CallPolicy"/> (chi/kan never taken, mirroring the live bot), and
/// exhaustive-draw tenpai payments. Every seat runs the supplied policy with perfect seen-tile
/// tracking of public information.
/// </summary>
internal static class HandSimulator
{
    public static HandOutcome Play(int dealer, Func<TurnInput, TurnDecision>[] policies, Random rng, int stickPot)
    {
        var wall = new Wall(rng);
        var players = new SimPlayer[4];
        for (int s = 0; s < 4; s++)
            players[s] = new SimPlayer();
        for (int n = 0; n < 13; n++)
            for (int s = 0; s < 4; s++)
                players[(dealer + s) % 4].Add(wall.Draw());

        var deltas = new int[4];
        int pot = stickPot;
        var declared = new bool[4];
        int turn = dealer;

        while (wall.HasDraws)
        {
            SimPlayer p = players[turn];
            int drawId = wall.Draw();
            p.Add(drawId);
            bool haitei = !wall.HasDraws;

            if (ShantenCalculator.Calculate(p.Counts, p.Melds.Count) == ShantenCalculator.Complete)
            {
                WinResult? result = YakuCalculator.Evaluate(
                    p.Counts, p.Melds, WinCtx(p, turn, dealer, TileId.Type(drawId), tsumo: true, lastTile: haitei, wall, extraAka: 0));
                if (result is { HasYaku: true })
                    return ScoreTsumo(turn, dealer, result, deltas, pot, declared, players);
            }

            // The ippatsu window closes with this draw (the tsumo check above still counted it).
            p.Ippatsu = false;

            int discardType;
            if (p.Riichi)
            {
                discardType = TileId.Type(drawId);
            }
            else
            {
                TurnDecision decision = policies[turn](BuildInput(players, turn, dealer, wall));
                if (decision.Discard < 0)
                {
                    discardType = TileId.Type(drawId); // defensive fallback; should not happen
                }
                else
                {
                    discardType = decision.Discard;
                    if (decision.DeclareRiichi)
                    {
                        p.Riichi = true;
                        p.Ippatsu = true;
                        declared[turn] = true;
                        deltas[turn] -= 1000;
                        pot++;
                    }
                }
            }

            // A pon claims the discard, jumps the turn, and forces an immediate discard from the
            // caller — which can itself be ronned or ponned, hence the inner loop.
            while (true)
            {
                bool akaLeft = p.RemoveForDiscard(discardType);
                p.River.Add(discardType);

                for (int k = 1; k < 4; k++)
                {
                    int seat = (turn + k) % 4;
                    SimPlayer opp = players[seat];

                    opp.Counts[discardType]++;
                    bool complete = ShantenCalculator.Calculate(opp.Counts, opp.Melds.Count) == ShantenCalculator.Complete;
                    if (complete && !IsFuriten(opp, discardType))
                    {
                        WinResult? result = YakuCalculator.Evaluate(
                            opp.Counts, opp.Melds, WinCtx(opp, seat, dealer, discardType, tsumo: false, lastTile: !wall.HasDraws, wall, extraAka: akaLeft ? 1 : 0));
                        if (result is { HasYaku: true })
                        {
                            opp.Counts[discardType]--;
                            return ScoreRon(seat, turn, dealer, result, deltas, pot, declared);
                        }
                    }

                    opp.Counts[discardType]--;
                }

                int caller = FindPonCaller(players, turn, dealer, discardType, wall);
                if (caller < 0)
                    break;

                p.River.RemoveAt(p.River.Count - 1);
                players[caller].TakePon(discardType, akaLeft);
                foreach (SimPlayer pl in players)
                    pl.Ippatsu = false;

                turn = caller;
                p = players[turn];

                TurnDecision after = policies[turn](BuildInput(players, turn, dealer, wall));
                discardType = after.Discard >= 0 ? after.Discard : FirstHeld(p);
            }

            turn = (turn + 1) % 4;
        }

        return ScoreExhaustiveDraw(dealer, deltas, pot, declared, players);
    }

    /// <summary>At most one seat can hold two copies of a discarded tile; returns it if its policy pons.</summary>
    private static int FindPonCaller(SimPlayer[] players, int discarder, int dealer, int discardType, Wall wall)
    {
        for (int k = 1; k < 4; k++)
        {
            int seat = (discarder + k) % 4;
            SimPlayer opp = players[seat];
            if (opp.Riichi || opp.Counts[discardType] < 2)
                continue;

            bool anyRiichi = false;
            for (int s = 0; s < 4; s++)
                if (s != seat && players[s].Riichi)
                    anyRiichi = true;

            if (CallPolicy.ShouldPon(opp.Counts, opp.Melds, discardType, EvalCtx(opp, seat, dealer, wall), anyRiichi))
                return seat;
        }

        return -1;
    }

    private static int FirstHeld(SimPlayer p)
    {
        for (int t = 0; t < Tiles.TypeCount; t++)
            if (p.Counts[t] > 0)
                return t;
        return 0;
    }

    private static EvalContext EvalCtx(SimPlayer p, int seat, int dealer, Wall wall)
        => new()
        {
            SeatWind = (seat - dealer + 4) % 4,
            RoundWind = 0,
            Dealer = seat == dealer,
            Closed = p.Melds.Count == 0,
            DoraIndicators = new[] { wall.DoraIndicatorType },
            AkaByType = p.AkaByType,
        };

    private static TurnInput BuildInput(SimPlayer[] players, int turn, int dealer, Wall wall)
    {
        SimPlayer p = players[turn];

        var seen = new int[Tiles.TypeCount];
        Array.Copy(p.Counts, seen, Tiles.TypeCount);
        foreach (SimPlayer other in players)
        {
            foreach (int t in other.River)
                seen[t]++;
            foreach (Meld meld in other.Melds)
                foreach (int t in meld.Tiles)
                    seen[t]++;
        }

        seen[wall.DoraIndicatorType] = Math.Min(4, seen[wall.DoraIndicatorType] + 1);

        var threats = new List<IReadOnlyList<int>>();
        for (int s = 0; s < 4; s++)
            if (s != turn && players[s].Riichi)
                threats.Add(players[s].River);

        return new TurnInput
        {
            Counts14 = p.Counts,
            Melds = p.Melds,
            Seen = seen,
            Context = EvalCtx(p, turn, dealer, wall),
            ThreatRivers = threats,
            OwnRiver = p.River,
        };
    }

    private static WinContext WinCtx(
        SimPlayer p, int seat, int dealer, int winTile, bool tsumo, bool lastTile, Wall wall, int extraAka)
        => new()
        {
            WinTile = winTile,
            Tsumo = tsumo,
            Riichi = p.Riichi,
            Ippatsu = p.Ippatsu,
            Haitei = tsumo && lastTile,
            Houtei = !tsumo && lastTile,
            SeatWind = (seat - dealer + 4) % 4,
            RoundWind = 0,
            AkaCount = p.AkaByType.Sum() + p.MeldAka + extraAka,
            DoraIndicators = new[] { wall.DoraIndicatorType },
            UraIndicators = new[] { wall.UraIndicatorType },
        };

    /// <summary>River furiten: ron is dead while any tile of the wait set sits in the player's own river.</summary>
    private static bool IsFuriten(SimPlayer p, int justAdded)
    {
        p.Counts[justAdded]--;
        try
        {
            for (int t = 0; t < Tiles.TypeCount; t++)
            {
                if (p.Counts[t] >= 4)
                    continue;
                p.Counts[t]++;
                bool wins = ShantenCalculator.Calculate(p.Counts, p.Melds.Count) == ShantenCalculator.Complete;
                p.Counts[t]--;
                if (wins && p.River.Contains(t))
                    return true;
            }

            return false;
        }
        finally
        {
            p.Counts[justAdded]++;
        }
    }

    private static HandOutcome ScoreTsumo(
        int winner, int dealer, WinResult result, int[] deltas, int pot, bool[] declared, SimPlayer[] players)
    {
        int points = DomanScoring.Points(result.TotalHan, winner == dealer, result.IsYakuman);

        for (int s = 0; s < 4; s++)
        {
            if (s == winner)
                continue;
            int share = winner == dealer
                ? RoundUp100(points / 3)
                : s == dealer ? RoundUp100(points / 2) : RoundUp100(points / 4);
            deltas[s] -= share;
            deltas[winner] += share;
        }

        deltas[winner] += pot * 1000;
        return new HandOutcome
        {
            Winner = winner,
            Deltas = deltas,
            DealerKeeps = winner == dealer,
            SticksLeft = 0,
            RiichiDeclared = declared,
            WinHan = result.TotalHan,
            WinPoints = points,
        };
    }

    private static HandOutcome ScoreRon(
        int winner, int discarder, int dealer, WinResult result, int[] deltas, int pot, bool[] declared)
    {
        int points = DomanScoring.Points(result.TotalHan, winner == dealer, result.IsYakuman);
        deltas[winner] += points + pot * 1000;
        deltas[discarder] -= points;
        return new HandOutcome
        {
            Winner = winner,
            DealIn = discarder,
            Deltas = deltas,
            DealerKeeps = winner == dealer,
            SticksLeft = 0,
            RiichiDeclared = declared,
            WinHan = result.TotalHan,
            WinPoints = points,
        };
    }

    private static HandOutcome ScoreExhaustiveDraw(int dealer, int[] deltas, int pot, bool[] declared, SimPlayer[] players)
    {
        var tenpai = new bool[4];
        int tenpaiCount = 0;
        for (int s = 0; s < 4; s++)
        {
            tenpai[s] = ShantenCalculator.Calculate(players[s].Counts, players[s].Melds.Count) <= 0;
            if (tenpai[s])
                tenpaiCount++;
        }

        if (tenpaiCount is > 0 and < 4)
        {
            int gain = 3000 / tenpaiCount;
            int loss = 3000 / (4 - tenpaiCount);
            for (int s = 0; s < 4; s++)
                deltas[s] += tenpai[s] ? gain : -loss;
        }

        return new HandOutcome
        {
            Deltas = deltas,
            ExhaustiveDraw = true,
            DealerKeeps = tenpai[dealer],
            SticksLeft = pot,
            RiichiDeclared = declared,
        };
    }

    private static int RoundUp100(int points) => (points + 99) / 100 * 100;
}

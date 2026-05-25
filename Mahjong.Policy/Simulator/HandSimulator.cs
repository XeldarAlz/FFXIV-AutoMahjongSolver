using System;
using System.Collections.Generic;
using Mahjong.Engine;

namespace Mahjong.Policy.Simulator;

public sealed class HandSimulator
{
    private readonly IRandomSource rng;
    private readonly Scorer scorer;
    public int MaxTurns { get; set; } = 200;

    public enum Outcome { Tsumo, Ron, Ryuukyoku, Aborted }

    public record HandResult(
        Outcome Outcome,
        int WinnerSeat,
        int LoserSeat,
        int[] FinalScores,
        int TurnCount,
        int TotalDiscards,
        int[] RiichiDeclared);

    public HandSimulator(IRandomSource rng, IRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rules);
        this.rng = rng;
        scorer = new Scorer(rules);
    }

    public HandResult Simulate(
        IPolicy[] policies,
        int dealer = 0,
        int[]? startingScores = null,
        int round = 0,
        int honba = 0)
    {
        if (policies.Length != 4)
            throw new ArgumentException("need 4 policies");

        var state = new SimulationHand
        {
            Dealer = dealer,
            Round = round,
            Honba = honba,
            CurrentSeat = dealer,
        };
        if (startingScores is not null)
            for (int i = 0; i < 4; i++)
                state.Scores[i] = startingScores[i];
        else
            for (int i = 0; i < 4; i++)
                state.Scores[i] = 25000;

        DealInitialHands(state);

        int turnCount = 0;
        int totalDiscards = 0;

        while (state.Wall.Count > 0 && turnCount < MaxTurns)
        {
            int seat = state.CurrentSeat;
            int handSize = state.HandTileCount(seat);
            if (handSize == 13)
            {
                if (state.Wall.Count == 0)
                    break;
                var drawn = state.Wall.Dequeue();
                state.ClosedCounts[seat][drawn.Id]++;
                state.LastDrawnTile = drawn;
                handSize = 14;

                if (IsTsumoWin(state, seat))
                {
                    ApplyTsumoScore(state, seat);
                    return new HandResult(
                        Outcome.Tsumo, seat, -1,
                        (int[])state.Scores.Clone(), turnCount, totalDiscards,
                        ToIntArray(state.Riichi));
                }
            }

            var snap = state.ToSnapshot(seat, ActionFlags.Discard);
            var choice = policies[seat].Choose(snap);

            if (choice.Kind == ActionKind.Tsumo)
            {
                if (IsTsumoWin(state, seat))
                {
                    ApplyTsumoScore(state, seat);
                    return new HandResult(
                        Outcome.Tsumo, seat, -1,
                        (int[])state.Scores.Clone(), turnCount, totalDiscards,
                        ToIntArray(state.Riichi));
                }
                return new HandResult(
                    Outcome.Aborted, -1, -1,
                    (int[])state.Scores.Clone(), turnCount, totalDiscards,
                    ToIntArray(state.Riichi));
            }

            if (choice.Kind == ActionKind.Riichi && !state.Riichi[seat] && state.Scores[seat] >= 1000)
            {
                state.Riichi[seat] = true;
                state.Scores[seat] -= 1000;
                state.RiichiSticks++;
            }

            Tile discardTile;
            if ((choice.Kind == ActionKind.Discard || choice.Kind == ActionKind.Riichi)
                && choice.DiscardTile is { } d
                && state.ClosedCounts[seat][d.Id] > 0)
            {
                discardTile = d;
            }
            else
            {
                discardTile = PickFallbackDiscard(state, seat);
            }

            state.ClosedCounts[seat][discardTile.Id]--;
            state.Discards[seat].Add(discardTile);
            bool tedashi = state.LastDrawnTile is null || state.LastDrawnTile.Value.Id != discardTile.Id;
            state.DiscardIsTedashi[seat].Add(tedashi);
            state.LastDrawnTile = null;
            totalDiscards++;

            for (int offset = 1; offset <= 3; offset++)
            {
                int other = (seat + offset) % 4;
                if (IsRonWin(state, other, discardTile))
                {
                    ApplyRonScore(state, winner: other, loser: seat, wintile: discardTile);
                    return new HandResult(
                        Outcome.Ron, other, seat,
                        (int[])state.Scores.Clone(), turnCount, totalDiscards,
                        ToIntArray(state.Riichi));
                }
            }

            state.CurrentSeat = (state.CurrentSeat + 1) % 4;
            turnCount++;
        }

        return new HandResult(
            Outcome.Ryuukyoku, -1, -1,
            (int[])state.Scores.Clone(), turnCount, totalDiscards,
            ToIntArray(state.Riichi));
    }

    private static int[] ToIntArray(bool[] b)
    {
        var result = new int[b.Length];
        for (int i = 0; i < b.Length; i++)
            result[i] = b[i] ? 1 : 0;
        return result;
    }

    private void DealInitialHands(SimulationHand state)
    {
        var wall = new List<Tile>(136);
        for (int k = 0; k < Tile.Count34; k++)
            for (int c = 0; c < Tile.CopiesPerKind; c++)
                wall.Add(Tile.FromId(k));

        for (int i = wall.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (wall[i], wall[j]) = (wall[j], wall[i]);
        }

        for (int seat = 0; seat < 4; seat++)
        {
            for (int i = 0; i < 13; i++)
            {
                var t = wall[^1];
                wall.RemoveAt(wall.Count - 1);
                state.ClosedCounts[seat][t.Id]++;
            }
        }

        var dead = new List<Tile>();
        for (int i = 0; i < 14 && wall.Count > 0; i++)
        {
            dead.Add(wall[0]);
            wall.RemoveAt(0);
        }
        state.DoraIndicator = dead.Count > 0 ? dead[0] : Tile.FromId(0);

        foreach (var t in wall)
            state.Wall.Enqueue(t);
    }

    private bool IsRonWin(SimulationHand state, int seat, Tile discardedTile)
    {
        int handSize = state.HandTileCount(seat);
        if (handSize != 13)
            return false;

        if (FuritenDetector.IsFuriten(state.ClosedCounts[seat], state.Melds[seat].Count, state.Discards[seat]))
            return false;

        state.ClosedCounts[seat][discardedTile.Id]++;

        var tiles = new List<Tile>();
        for (int k = 0; k < Tile.Count34; k++)
            for (int c = 0; c < state.ClosedCounts[seat][k]; c++)
                tiles.Add(Tile.FromId(k));
        var hand = Hand.FromTiles(tiles, state.Melds[seat].ToArray());
        var ctx = new WinContext(
            discardedTile,
            WinKind.Ron,
            IsRiichi: state.Riichi[seat],
            IsHoutei: state.Wall.Count == 0,
            RoundWindTileId: 27 + state.Round,
            SeatWindTileId: 27 + seat,
            IsDealer: seat == state.Dealer);

        var result = scorer.Evaluate(hand, ctx);

        state.ClosedCounts[seat][discardedTile.Id]--;
        return result is not null;
    }

    private void ApplyRonScore(SimulationHand state, int winner, int loser, Tile wintile)
    {
        state.ClosedCounts[winner][wintile.Id]++;

        var tiles = new List<Tile>();
        for (int k = 0; k < Tile.Count34; k++)
            for (int c = 0; c < state.ClosedCounts[winner][k]; c++)
                tiles.Add(Tile.FromId(k));
        var hand = Hand.FromTiles(tiles, state.Melds[winner].ToArray());
        var ctx = new WinContext(
            wintile,
            WinKind.Ron,
            IsRiichi: state.Riichi[winner],
            IsHoutei: state.Wall.Count == 0,
            RoundWindTileId: 27 + state.Round,
            SeatWindTileId: 27 + winner,
            IsDealer: winner == state.Dealer);

        var result = scorer.Evaluate(hand, ctx);
        state.ClosedCounts[winner][wintile.Id]--;

        if (result is null)
            return;

        int total = result.Payments.RonTotal;
        state.Scores[loser] -= total;
        state.Scores[winner] += total;

        state.Scores[winner] += state.RiichiSticks * 1000;
        state.RiichiSticks = 0;
    }

    private bool IsTsumoWin(SimulationHand state, int seat)
    {
        int total = state.HandTileCount(seat);
        if (total != 14)
            return false;

        var tiles = new List<Tile>();
        for (int k = 0; k < Tile.Count34; k++)
            for (int c = 0; c < state.ClosedCounts[seat][k]; c++)
                tiles.Add(Tile.FromId(k));

        var hand = Hand.FromTiles(tiles, state.Melds[seat].ToArray());
        if (state.LastDrawnTile is null)
            return false;

        var ctx = new WinContext(
            state.LastDrawnTile.Value,
            WinKind.Tsumo,
            IsRiichi: state.Riichi[seat],
            IsHaitei: state.Wall.Count == 0,
            RoundWindTileId: 27 + state.Round,
            SeatWindTileId: 27 + seat,
            IsDealer: seat == state.Dealer);

        var result = scorer.Evaluate(hand, ctx);
        return result is not null;
    }

    private void ApplyTsumoScore(SimulationHand state, int seat)
    {
        var tiles = new List<Tile>();
        for (int k = 0; k < Tile.Count34; k++)
            for (int c = 0; c < state.ClosedCounts[seat][k]; c++)
                tiles.Add(Tile.FromId(k));
        var hand = Hand.FromTiles(tiles, state.Melds[seat].ToArray());
        var ctx = new WinContext(
            state.LastDrawnTile!.Value,
            WinKind.Tsumo,
            IsRiichi: state.Riichi[seat],
            IsHaitei: state.Wall.Count == 0,
            RoundWindTileId: 27 + state.Round,
            SeatWindTileId: 27 + seat,
            IsDealer: seat == state.Dealer);

        var result = scorer.Evaluate(hand, ctx);
        if (result is null)
            return;

        var pay = result.Payments;

        state.Scores[seat] += state.RiichiSticks * 1000;
        state.RiichiSticks = 0;
        if (seat == state.Dealer)
        {
            for (int i = 0; i < 4; i++)
            {
                if (i == seat)
                    continue;
                state.Scores[i] -= pay.NonDealerPay;
                state.Scores[seat] += pay.NonDealerPay;
            }
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                if (i == seat)
                    continue;
                int owed = (i == state.Dealer) ? pay.DealerPay : pay.NonDealerPay;
                state.Scores[i] -= owed;
                state.Scores[seat] += owed;
            }
        }
    }

    private Tile PickFallbackDiscard(SimulationHand state, int seat)
    {
        for (int k = 0; k < Tile.Count34; k++)
            if (state.ClosedCounts[seat][k] > 0)
                return Tile.FromId(k);
        return Tile.FromId(0);
    }
}

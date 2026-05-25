using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

public static class DiscardScorer
{
    public static ScoredDiscard[] Score(
        StateSnapshot state,
        DiscardWeights? weights = null,
        Wall? wall = null,
        PlacementMultipliers? placement = null,
        IOpponentModel? opponentModel = null)
    {
        var w = weights ?? DiscardWeights.Default;
        var p = placement ?? PlacementMultipliers.Neutral;

        var hand = BuildHand(state);
        if (hand.ClosedTileCount + hand.OpenMelds.Count * 3 != 14)
            throw new ArgumentException(
                $"DiscardScorer requires a 14-tile hand (closed={hand.ClosedTileCount}, melds={hand.OpenMelds.Count})");

        var ukeire = UkeireEnumerator.Enumerate(hand, wall);
        var result = new ScoredDiscard[ukeire.Length];

        for (int i = 0; i < ukeire.Length; i++)
        {
            var u = ukeire[i];
            int doraRetained = CountDora(hand, u.Discard, state.DoraIndicators);
            int yakuhaiRetained = CountYakuhai(hand, u.Discard, 27 + state.RoundWind, state);
            double yakuPotential = YakuPotential.Score(hand, u.Discard, state);

            double dealInCost = opponentModel?.ExpectedDealInCost(u.Discard.Id) ?? 0.0;

            double yakulessPenalty = u.ShantenAfter == 0 && yakuPotential < 0.5
                ? w.YakulessTenpaiPenalty * (0.5 - yakuPotential) * 2.0
                : 0.0;

            double score =
                -w.Shanten * Math.Max(0, u.ShantenAfter)
                + w.UkeireKinds * u.AcceptedKinds.Length * p.Ukeire
                + w.UkeireWeighted * u.WeightedCount * p.Ukeire
                + w.Dora * doraRetained * p.HandValue
                + w.Yakuhai * yakuhaiRetained * p.HandValue
                + w.YakuPotential * yakuPotential * p.HandValue
                - yakulessPenalty * p.HandValue
                + (IsIsolatedTerminalOrHonor(hand, u.Discard) ? w.IsolatedTerminal * p.Danger : 0.0)
                - w.DealInCost * dealInCost * p.Danger;

            result[i] = new ScoredDiscard(
                u.Discard, score, u.ShantenAfter,
                u.AcceptedKinds.Length, u.WeightedCount,
                doraRetained, yakuhaiRetained, dealInCost, yakuPotential);
        }

        Array.Sort(result, (a, b) => b.Score.CompareTo(a.Score));
        return result;
    }

    private static Hand BuildHand(StateSnapshot state)
    {
        var counts = new int[Tile.Count34];
        foreach (var t in state.Hand)
            counts[t.Id]++;
        return new Hand(counts, state.OurMelds);
    }

    private static int CountDora(Hand hand, Tile removed, IReadOnlyList<Tile> indicators)
    {
        if (indicators.Count == 0)
            return 0;
        int total = 0;
        for (int id = 0; id < Tile.Count34; id++)
        {
            int count = hand.ClosedCounts[id] - (removed.Id == id ? 1 : 0);
            if (count <= 0)
                continue;
            foreach (var ind in indicators)
                if (DoraNext(ind) == id)
                    total += count;
        }
        foreach (var m in hand.OpenMelds)
            foreach (var t in m.Tiles)
                foreach (var ind in indicators)
                    if (DoraNext(ind) == t.Id)
                        total++;
        return total;
    }

    private static int DoraNext(Tile indicator)
    {
        int id = indicator.Id;
        if (id < 27)
            return (id / 9) * 9 + (id % 9 + 1) % 9;
        if (id <= 30)
            return 27 + (id - 27 + 1) % 4;
        return 31 + (id - 31 + 1) % 3;
    }

    private static int CountYakuhai(Hand hand, Tile removed, int roundWindTileId, StateSnapshot state)
    {
        int seatWindTileId = 27 + state.OurSeat;
        int total = 0;
        for (int id = 27; id < Tile.Count34; id++)
        {
            int count = hand.ClosedCounts[id] - (removed.Id == id ? 1 : 0);
            if (count <= 0)
                continue;
            bool isYakuhai = id >= 31;
            if (!isYakuhai && state.SeatInfoKnown)
                isYakuhai = id == roundWindTileId || id == seatWindTileId;
            if (isYakuhai)
                total += count;
        }
        return total;
    }

    private static bool IsIsolatedTerminalOrHonor(Hand hand, Tile t)
    {
        if (!t.IsTerminalOrHonor)
            return false;
        if (hand.ClosedCounts[t.Id] >= 2)
            return false;

        if (t.IsHonor)
            return true;

        int suitBase = (t.Id / 9) * 9;
        int pos = t.Id - suitBase;
        int neighborPos = pos == 0 ? 1 : 7;
        int neighborId = suitBase + neighborPos;
        if (hand.ClosedCounts[neighborId] > 0)
            return false;

        int twoStepPos = pos == 0 ? 2 : 6;
        int twoStepId = suitBase + twoStepPos;
        if (hand.ClosedCounts[twoStepId] > 0)
            return false;

        return true;
    }
}

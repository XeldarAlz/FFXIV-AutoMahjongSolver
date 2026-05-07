namespace Mahjong.Replay;

/// <summary>
/// Replays a parsed Tenhou kyoku turn-by-turn for one seat, reconstructing the
/// observable state at each discard decision, asking the supplied <see cref="IPolicy"/>
/// for its choice, and comparing against the recorded discard. Yields per-decision
/// metrics for policy validation and weight tuning.
///
/// Simplifications: draws and discards are interleaved strictly in seat order,
/// starting with the dealer; calls are not replayed (calls break the simple
/// sequence — a full replay needs the event stream with tags, which the MVP
/// parser doesn't extract yet).
/// </summary>
public static class TenhouReplay
{
    public readonly record struct Decision(
        int TurnIndex,
        Tile ActualDiscard,
        Tile PolicyPick,
        bool Matched);

    public readonly record struct ReplayResult(
        int TotalDecisions,
        int Matches,
        double Accuracy,
        Decision[] Decisions);

    public static ReplayResult ReplaySeat(TenhouLog.Kyoku kyoku, IPolicy policy, int seat)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (seat is < 0 or >= 4)
            throw new ArgumentOutOfRangeException(nameof(seat));

        var counts = BuildStartingCounts(kyoku, seat);
        var publicDiscards = NewPerSeatDiscardLists();

        var draws = kyoku.DrawTiles[seat];
        var discards = kyoku.DiscardTiles[seat];
        int steps = Math.Min(draws.Length, discards.Length);
        var decisions = new List<Decision>(steps);

        for (int turn = 0; turn < steps; turn++)
        {
            ApplyDraw(counts, draws[turn]);

            var snap = BuildSnapshot(kyoku, seat, counts, publicDiscards);
            var choice = policy.Choose(snap);
            var policyPick = choice.DiscardTile ?? Tile.FromId(draws[turn]);
            var actual = Tile.FromId(discards[turn]);

            decisions.Add(new Decision(turn, actual, policyPick, policyPick.Id == actual.Id));

            // Apply the *actual* discard so subsequent turns match the recorded line.
            counts[actual.Id]--;
            publicDiscards[seat].Add(actual);
        }

        int matches = CountMatches(decisions);
        double accuracy = decisions.Count == 0 ? 0.0 : (double)matches / decisions.Count;
        return new ReplayResult(decisions.Count, matches, accuracy, decisions.ToArray());
    }

    private static int[] BuildStartingCounts(TenhouLog.Kyoku kyoku, int seat)
    {
        var counts = new int[Tile.Count34];
        foreach (var t in kyoku.StartingHands[seat])
            counts[t.Id]++;
        return counts;
    }

    private static List<Tile>[] NewPerSeatDiscardLists()
    {
        var lists = new List<Tile>[4];
        for (int i = 0; i < 4; i++)
            lists[i] = new List<Tile>();
        return lists;
    }

    private static void ApplyDraw(int[] counts, int drawId)
    {
        if (drawId < 0)
            return;        // event-only slot (tsumo declaration etc.) — nothing to add to hand
        counts[drawId]++;
    }

    private static StateSnapshot BuildSnapshot(
        TenhouLog.Kyoku kyoku, int seat, int[] counts, List<Tile>[] publicDiscards)
    {
        var hand = ExpandHand(counts);
        var seats = BuildSeatViews(seat, publicDiscards);
        return StateSnapshot.Empty with
        {
            Hand = hand,
            OurSeat = seat,
            RoundWind = kyoku.Round,
            Honba = kyoku.Honba,
            RiichiSticks = kyoku.RiichiSticks,
            Scores = kyoku.StartScores,
            DoraIndicators = kyoku.DoraIndicators,
            DealerSeat = kyoku.Dealer,
            Seats = seats,
            Legal = new LegalActions(ActionFlags.Discard, [], [], [], []),
        };
    }

    private static List<Tile> ExpandHand(int[] counts)
    {
        var hand = new List<Tile>(14);
        for (int k = 0; k < Tile.Count34; k++)
            for (int c = 0; c < counts[k]; c++)
                hand.Add(Tile.FromId(k));
        return hand;
    }

    private static SeatView[] BuildSeatViews(int seat, List<Tile>[] publicDiscards)
    {
        var seats = new SeatView[4];
        for (int rel = 0; rel < 4; rel++)
        {
            int abs = (seat + rel) % 4;
            seats[rel] = new SeatView(
                Discards: publicDiscards[abs].ToArray(),
                DiscardIsTedashi: new bool[publicDiscards[abs].Count],
                Melds: [],
                Riichi: false,
                RiichiDiscardIndex: -1,
                Ippatsu: false,
                IsTenpaiCalled: false);
        }
        return seats;
    }

    private static int CountMatches(IReadOnlyList<Decision> decisions)
    {
        int matches = 0;
        foreach (var d in decisions)
        {
            if (d.Matched)
                matches++;
        }
        return matches;
    }
}

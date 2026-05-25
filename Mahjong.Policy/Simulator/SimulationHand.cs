using System.Collections.Generic;
using Mahjong.Engine;

namespace Mahjong.Policy.Simulator;

internal sealed class SimulationHand
{
    public readonly int[][] ClosedCounts = new int[4][];
    public readonly List<Meld>[] Melds = new List<Meld>[4];
    public readonly List<Tile>[] Discards = new List<Tile>[4];
    public readonly List<bool>[] DiscardIsTedashi = new List<bool>[4];
    public readonly Queue<Tile> Wall = new();
    public Tile DoraIndicator;
    public readonly int[] Scores = new int[4];
    public int Dealer;
    public int Round;
    public int Honba;
#pragma warning disable CS0649
    public int RiichiSticks;
#pragma warning restore CS0649
    public int CurrentSeat;
    public readonly bool[] Riichi = new bool[4];
    public Tile? LastDrawnTile;

    public SimulationHand()
    {
        for (int i = 0; i < 4; i++)
        {
            ClosedCounts[i] = new int[Tile.Count34];
            Melds[i] = new List<Meld>();
            Discards[i] = new List<Tile>();
            DiscardIsTedashi[i] = new List<bool>();
        }
    }

    public int HandTileCount(int seat)
    {
        int total = 0;
        for (int k = 0; k < Tile.Count34; k++)
            total += ClosedCounts[seat][k];
        return total;
    }

    public StateSnapshot ToSnapshot(int observerSeat, ActionFlags legal)
    {
        var sortedHand = new List<Tile>();
        for (int k = 0; k < Tile.Count34; k++)
            for (int c = 0; c < ClosedCounts[observerSeat][k]; c++)
                sortedHand.Add(Tile.FromId(k));

        var seatsRelative = new SeatView[4];
        var scoresRelative = new int[4];
        for (int rel = 0; rel < 4; rel++)
        {
            int abs = (observerSeat + rel) % 4;
            seatsRelative[rel] = new SeatView(
                Discards[abs].ToArray(),
                DiscardIsTedashi[abs].ToArray(),
                Melds[abs].ToArray(),
                Riichi[abs],
                Riichi[abs] ? 0 : -1,
                Ippatsu: false,
                IsTenpaiCalled: false);
            scoresRelative[rel] = Scores[abs];
        }

        return StateSnapshot.Empty with
        {
            Hand = sortedHand,
            OurMelds = Melds[observerSeat].ToArray(),
            OurSeat = observerSeat,
            RoundWind = Round,
            Honba = Honba,
            RiichiSticks = RiichiSticks,
            Scores = scoresRelative,
            DoraIndicators = new[] { DoraIndicator },
            WallRemaining = Wall.Count,
            DealerSeat = Dealer,
            Seats = seatsRelative,
            Legal = new LegalActions(legal, [], [], [], []),
        };
    }
}

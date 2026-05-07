using Mahjong.Engine;
using Mahjong.Policy.Opponents;

namespace Mahjong.Policy.Mcts;

/// <summary>
/// Sample a concrete assignment of hidden information consistent with the public
/// observation — i.e. hypothesize each opponent's closed hand and the future
/// wall order. Used by <see cref="IsmctsPolicy"/> to run tree search over
/// plausible game states.
///
/// Samples are drawn from the unseen-tile pool. The opponent model's
/// <see cref="IOpponentModel.HandMarginal"/> isn't yet biased into the sampling
/// (Q11 in docs/ruleset.md tracks that improvement) — we shuffle uniformly,
/// which is correct in expectation since the pool already excludes tiles known
/// to be impossible.
/// </summary>
public sealed class Determinizer
{
    private readonly IRandomSource rng;

    public Determinizer(IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        this.rng = rng;
    }

    public record struct Determinization(
        Tile[][] OpponentHands,      // [3][13] — hypothesized closed hands
        Tile[] WallOrder);           // remaining draw pile, shuffled

    /// <summary>
    /// Produce a sampled determinization. Opponents' hand sizes default to 13
    /// minus their open-meld tile count (each called meld removes 3 closed tiles).
    /// Returns null if the pool is too small to deal everyone their hand.
    /// </summary>
    public Determinization? Sample(StateSnapshot state, IOpponentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var pool = BuildUnseenPool(state);

        // Closed tile count = 13 - 3 * meldCount (ignoring the +1 mid-turn case
        // — the extra tile is negligible at the sampler resolution).
        var handSizes = new int[model.OpponentCount];
        for (int opp = 0; opp < model.OpponentCount; opp++)
        {
            int absSeat = (state.OurSeat + 1 + opp) % 4;
            handSizes[opp] = Math.Max(0, 13 - state.Seats[absSeat].Melds.Count * 3);
        }

        int totalDemand = 0;
        foreach (var n in handSizes)
            totalDemand += n;
        if (totalDemand > pool.Count)
            return null;

        rng.Shuffle(pool);

        var handsOut = new Tile[model.OpponentCount][];
        int cursor = 0;
        for (int opp = 0; opp < model.OpponentCount; opp++)
        {
            int n = handSizes[opp];
            handsOut[opp] = new Tile[n];
            for (int i = 0; i < n; i++)
                handsOut[opp][i] = pool[cursor++];
        }

        var wallOrder = new Tile[pool.Count - cursor];
        for (int i = 0; i < wallOrder.Length; i++)
            wallOrder[i] = pool[cursor + i];

        return new Determinization(handsOut, wallOrder);
    }

    private static List<Tile> BuildUnseenPool(StateSnapshot state)
    {
        var seen = new int[Tile.Count34];
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

        var pool = new List<Tile>();
        for (int k = 0; k < Tile.Count34; k++)
        {
            int remaining = Tile.CopiesPerKind - seen[k];
            for (int i = 0; i < remaining; i++)
                pool.Add(Tile.FromId(k));
        }
        return pool;
    }
}

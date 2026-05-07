using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

/// <summary>
/// Heuristic <see cref="ICallPolicy"/>. Accepts a pon/chi/kan call when:
/// <list type="bullet">
///   <item>Shanten strictly improves after taking the meld.</item>
///   <item>The post-call hand still has a reachable yaku path
///         (yakuhai pair, tanyao, honitsu/chinitsu suit dominance, or toitoi).</item>
/// </list>
/// Conservative: false-negatives lean toward not calling. Opponent-model-aware
/// deal-in weighting is a follow-up that lands when push/fold drives calls too.
/// </summary>
public sealed class HeuristicCallPolicy : ICallPolicy
{
    public Decision<MeldCandidate?> Evaluate(StateSnapshot state)
    {
        var candidates = CollectCandidates(state);
        if (candidates.Count == 0)
            return Decline("no call candidates offered");

        // Counts of our 13-tile pre-call closed hand.
        var counts = new int[Tile.Count34];
        foreach (var t in state.Hand)
            counts[t.Id]++;

        int currentShanten = ComputeShanten(counts, state.OurMelds.Count);

        MeldCandidate? best = null;
        int bestShantenDelta = 0;
        string bestSummary = string.Empty;

        foreach (var c in candidates)
        {
            int? shantenAfter = TryShantenAfter(c, counts, state.OurMelds.Count);
            if (shantenAfter is null)
                continue;

            int delta = currentShanten - shantenAfter.Value;
            if (delta <= 0)
                continue;

            // Apply call to counts to evaluate yaku reachability, then revert.
            foreach (var t in c.HandTiles)
                counts[t.Id]--;
            int meldsAfter = state.OurMelds.Count + 1;
            bool yakuReachable = HasReachableYaku(counts, meldsAfter, state, c);
            foreach (var t in c.HandTiles)
                counts[t.Id]++;

            if (!yakuReachable)
                continue;

            if (delta > bestShantenDelta)
            {
                bestShantenDelta = delta;
                best = c;
                bestSummary = $"shanten {currentShanten}→{shantenAfter.Value}, kind={c.Kind}";
            }
        }

        if (best is null)
        {
            return new Decision<MeldCandidate?>(
                Accept: false,
                Value: null,
                Reason: new Reason(
                    Code: "no-shanten-gain-with-yaku",
                    Display: $"no call improves shanten with yaku (current={currentShanten})"));
        }

        return new Decision<MeldCandidate?>(
            Accept: true,
            Value: best,
            Reason: new Reason(
                Code: "shanten-gain",
                Display: bestSummary,
                Data: new Dictionary<string, object>
                {
                    ["shantenBefore"] = currentShanten,
                    ["shantenDelta"] = bestShantenDelta,
                    ["meldKind"] = best.Value.Kind.ToString(),
                }));
    }

    private static List<MeldCandidate> CollectCandidates(StateSnapshot state)
    {
        var legal = state.Legal;
        var candidates = new List<MeldCandidate>(
            legal.PonCandidates.Count + legal.ChiCandidates.Count + legal.KanCandidates.Count);
        candidates.AddRange(legal.PonCandidates);
        candidates.AddRange(legal.ChiCandidates);
        candidates.AddRange(legal.KanCandidates);
        return candidates;
    }

    private static int? TryShantenAfter(MeldCandidate candidate, int[] counts, int currentMelds)
    {
        foreach (var t in candidate.HandTiles)
        {
            if (counts[t.Id] <= 0)
                return null;
        }
        foreach (var t in candidate.HandTiles)
            counts[t.Id]--;
        int shantenAfter = ComputeShanten(counts, currentMelds + 1);
        foreach (var t in candidate.HandTiles)
            counts[t.Id]++;
        return shantenAfter;
    }

    private static int ComputeShanten(int[] counts, int meldCount)
    {
        int std = ShantenCalculator.Standard(counts, meldCount);
        int ci = meldCount == 0 ? ShantenCalculator.Chiitoitsu(counts) : 8;
        int ko = meldCount == 0 ? ShantenCalculator.Kokushi(counts) : 8;
        return Math.Min(std, Math.Min(ci, ko));
    }

    /// <summary>
    /// Cheap yaku-reachability check: yakuhai, tanyao, honitsu/chinitsu, or toitoi.
    /// Conservative — false-negatives lean toward not calling.
    /// </summary>
    private static bool HasReachableYaku(int[] counts, int meldsAfter, StateSnapshot state, MeldCandidate thisCall)
    {
        if (HasReachableYakuhai(counts, state, thisCall))
            return true;
        if (HasReachableTanyao(counts, state, thisCall))
            return true;
        if (HasReachableSuitFlush(counts))
            return true;
        if (HasReachableToitoi(counts, state, thisCall))
            return true;
        return false;
    }

    private static bool HasReachableYakuhai(int[] counts, StateSnapshot state, MeldCandidate thisCall)
    {
        if (thisCall.Kind == MeldKind.Pon && IsYakuhaiTile(thisCall.ClaimedTile, state))
            return true;
        for (int id = TileIds.FirstDragon; id <= TileIds.LastDragon; id++)
            if (counts[id] >= 2)
                return true;
        for (int id = TileIds.FirstWind; id <= TileIds.LastWind; id++)
            if (counts[id] >= 2 && IsYakuhaiWind(id, state))
                return true;
        return false;
    }

    private static bool HasReachableTanyao(int[] counts, StateSnapshot state, MeldCandidate thisCall)
    {
        for (int id = 0; id < Tile.Count34; id++)
        {
            if (counts[id] == 0)
                continue;
            if (Tile.FromId(id).IsTerminalOrHonor)
                return false;
        }
        foreach (var m in state.OurMelds)
            foreach (var t in m.Tiles)
                if (t.IsTerminalOrHonor)
                    return false;
        return thisCall.ClaimedTile.IsSimple;
    }

    private static bool HasReachableSuitFlush(int[] counts)
    {
        int? suit = null;
        for (int id = 0; id < TileIds.HonorStart; id++)
        {
            if (counts[id] == 0)
                continue;
            int s = id / TileIds.SuitSize;
            if (suit is null)
                suit = s;
            else if (suit != s)
                return false;
        }
        return suit is not null;
    }

    private static bool HasReachableToitoi(int[] counts, StateSnapshot state, MeldCandidate thisCall)
    {
        if (thisCall.Kind == MeldKind.Chi)
            return false;

        int triplets = 0;
        foreach (var m in state.OurMelds)
        {
            if (m.Kind is MeldKind.Pon or MeldKind.MinKan or MeldKind.AnKan or MeldKind.ShouMinKan)
                triplets++;
        }
        for (int id = 0; id < Tile.Count34; id++)
            if (counts[id] >= 3)
                triplets++;
        return triplets >= 2;
    }

    private static bool IsYakuhaiTile(Tile t, StateSnapshot state)
    {
        if (t.IsDragon)
            return true;
        if (!t.IsWind || !state.SeatInfoKnown)
            return false;
        int seatWindId = TileIds.FirstWind + state.OurSeat;
        int roundWindId = TileIds.FirstWind + state.RoundWind;
        return t.Id == seatWindId || t.Id == roundWindId;
    }

    private static bool IsYakuhaiWind(int id, StateSnapshot state)
    {
        if (!state.SeatInfoKnown)
            return false;
        int seatWindId = TileIds.FirstWind + state.OurSeat;
        int roundWindId = TileIds.FirstWind + state.RoundWind;
        return id == seatWindId || id == roundWindId;
    }

    private static Decision<MeldCandidate?> Decline(string display) => new(
        Accept: false,
        Value: null,
        Reason: new Reason(Code: "no-candidate", Display: display));
}

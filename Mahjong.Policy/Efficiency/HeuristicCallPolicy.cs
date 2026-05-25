using Mahjong.Engine;
using Mahjong.Rules;
using Mahjong.Rules.Rulesets;

namespace Mahjong.Policy.Efficiency;

/// <summary>Conservative: requires strict shanten gain AND a reachable yaku path post-call.</summary>
public sealed class HeuristicCallPolicy : ICallPolicy
{
    private readonly IRuleSet ruleSet;

    public HeuristicCallPolicy() : this(new RiichiRuleSet()) { }

    public HeuristicCallPolicy(IRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        this.ruleSet = ruleSet;
    }

    public Decision<MeldCandidate?> Evaluate(StateSnapshot state)
    {
        var candidates = CollectCandidates(state);
        if (candidates.Count == 0)
            return Decline("no call candidates offered");

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

            foreach (var t in c.HandTiles)
                counts[t.Id]--;
            int meldsAfter = state.OurMelds.Count + 1;
            int reachableHan = EstimateReachableHan(counts, meldsAfter, state, c, ruleSet.DoraRule);
            foreach (var t in c.HandTiles)
                counts[t.Id]++;

            if (reachableHan < ruleSet.MinHan)
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
                    Display:
                        $"no call improves shanten with reachable yaku ≥ {ruleSet.MinHan} han " +
                        $"(current={currentShanten})"));
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
    /// Sum reachable han across yaku families plus dora retained on the post-call concealed hand.
    /// Treats families as independent so MinHan=2 rulesets (Doman) reject lone 1-han yakuhai paths.
    /// </summary>
    private static int EstimateReachableHan(int[] counts, int meldsAfter, StateSnapshot state, MeldCandidate thisCall, IDoraRule doraRule)
    {
        int han = ReachableYakuhaiHan(counts, state, thisCall);
        if (HasReachableTanyao(counts, state, thisCall)) han++;
        if (HasReachableSuitFlush(counts)) han += 2;
        if (HasReachableToitoi(counts, state, thisCall)) han += 2;
        if (HasReachableSanshokuDoujun(counts, state, thisCall)) han++;
        if (HasReachableIttsu(counts, state, thisCall)) han++;
        han += RetainedDoraHan(counts, state, thisCall, doraRule);
        return han;
    }

    private static int ReachableYakuhaiHan(int[] counts, StateSnapshot state, MeldCandidate thisCall)
    {
        int han = 0;
        if (thisCall.Kind == MeldKind.Pon && IsYakuhaiTile(thisCall.ClaimedTile, state))
            han++;
        for (int id = TileIds.FirstDragon; id <= TileIds.LastDragon; id++)
            if (counts[id] >= 2)
                han++;
        for (int id = TileIds.FirstWind; id <= TileIds.LastWind; id++)
            if (counts[id] >= 2 && IsYakuhaiWind(id, state))
                han++;
        return han;
    }

    private static int RetainedDoraHan(int[] counts, StateSnapshot state, MeldCandidate thisCall, IDoraRule doraRule)
    {
        if (state.DoraIndicators.Count == 0)
            return 0;
        int han = 0;
        foreach (var indicator in state.DoraIndicators)
        {
            int doraId = doraRule.Next(indicator).Id;
            han += counts[doraId];
            foreach (var m in state.OurMelds)
                foreach (var t in m.Tiles)
                    if (t.Id == doraId) han++;
            if (thisCall.ClaimedTile.Id == doraId) han++;
            foreach (var t in thisCall.HandTiles)
                if (t.Id == doraId) han++;
        }
        return han;
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

    private static bool HasReachableSanshokuDoujun(int[] counts, StateSnapshot state, MeldCandidate thisCall)
    {
        Span<bool> chiAt = stackalloc bool[21];
        RecordChiOffset(state.OurMelds, chiAt, ittsuOnly: false);
        if (thisCall.Kind == MeldKind.Chi)
        {
            int firstId = ChiStartId(thisCall);
            int suit = firstId / TileIds.SuitSize;
            int offset = firstId % TileIds.SuitSize;
            if (suit < 3 && offset <= TileIds.SuitSize - 3)
                chiAt[offset * 3 + suit] = true;
        }

        for (int n = 0; n <= TileIds.SuitSize - 3; n++)
        {
            bool allThree = true;
            for (int suit = 0; suit < 3; suit++)
            {
                if (chiAt[n * 3 + suit])
                    continue;
                int suitBase = suit * TileIds.SuitSize;
                int present = 0;
                if (counts[suitBase + n] > 0) present++;
                if (counts[suitBase + n + 1] > 0) present++;
                if (counts[suitBase + n + 2] > 0) present++;
                if (present < 2)
                {
                    allThree = false;
                    break;
                }
            }
            if (allThree)
                return true;
        }
        return false;
    }

    private static bool HasReachableIttsu(int[] counts, StateSnapshot state, MeldCandidate thisCall)
    {
        Span<bool> chiAt = stackalloc bool[9];
        RecordChiOffset(state.OurMelds, chiAt, ittsuOnly: true);
        if (thisCall.Kind == MeldKind.Chi)
        {
            int firstId = ChiStartId(thisCall);
            int suit = firstId / TileIds.SuitSize;
            int offset = firstId % TileIds.SuitSize;
            if (suit < 3 && (offset == 0 || offset == 3 || offset == 6))
                chiAt[(offset / 3) * 3 + suit] = true;
        }

        for (int suit = 0; suit < 3; suit++)
        {
            int suitBase = suit * TileIds.SuitSize;
            bool allThree = true;
            for (int subrun = 0; subrun < 3; subrun++)
            {
                if (chiAt[subrun * 3 + suit])
                    continue;
                int start = subrun * 3;
                int present = 0;
                if (counts[suitBase + start]     > 0) present++;
                if (counts[suitBase + start + 1] > 0) present++;
                if (counts[suitBase + start + 2] > 0) present++;
                if (present < 2)
                {
                    allThree = false;
                    break;
                }
            }
            if (allThree)
                return true;
        }
        return false;
    }

    private static void RecordChiOffset(IReadOnlyList<Meld> melds, Span<bool> chiAt, bool ittsuOnly)
    {
        foreach (var m in melds)
        {
            if (m.Kind != MeldKind.Chi)
                continue;
            int firstId = m.Tiles[0].Id;
            int suit = firstId / TileIds.SuitSize;
            int offset = firstId % TileIds.SuitSize;
            if (suit >= 3)
                continue;
            if (ittsuOnly)
            {
                if (offset == 0 || offset == 3 || offset == 6)
                    chiAt[(offset / 3) * 3 + suit] = true;
            }
            else if (offset <= TileIds.SuitSize - 3)
            {
                chiAt[offset * 3 + suit] = true;
            }
        }
    }

    private static int ChiStartId(MeldCandidate c)
    {
        int lowId = c.ClaimedTile.Id;
        foreach (var t in c.HandTiles)
            if (t.Id < lowId)
                lowId = t.Id;
        return lowId;
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

using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

/// <summary>
/// Continuous [0, 1] yaku-completion estimate normalised to <see cref="TargetHan"/>. Targets
/// mangan-floor so the scorer keeps differentiating value beyond Doman's 2-han minimum.
/// Two soft 0.5-cert routes beat one 1.0-cert route, biasing toward yaku-stacked hands.
/// </summary>
public static class YakuPotential
{
    /// <summary>
    /// Saturation point in han. Was 2.0 (Doman min). Raising it to 4.0 means a fat 4-han
    /// hand reads as 1.0 and a thin 2-han hand reads as 0.5, so the scorer no longer
    /// plateaus the moment legality is reached. DiscardScorer reads this to size the
    /// yakuless-tenpai penalty.
    /// </summary>
    public const double TargetHan = 4.0;

    /// <summary>Pass null for <paramref name="removed"/> to score the hand as-is.</summary>
    public static double Score(Hand hand, Tile? removed, StateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(state);

        bool isClosed = IsClosed(hand);
        var adjusted = new int[Tile.Count34];
        for (int i = 0; i < Tile.Count34; i++)
            adjusted[i] = hand.ClosedCounts[i];
        if (removed is { } r)
            adjusted[r.Id]--;

        double tanyaoCert = ScoreTanyao(adjusted, hand.OpenMelds);
        double yakuhaiCert = ScoreYakuhai(adjusted, hand.OpenMelds, state);
        var (suitCert, hasHonors) = ScoreSuitedness(adjusted, hand.OpenMelds);
        double chiitoitsuCert = hand.OpenMelds.Count == 0 ? ScoreChiitoitsu(adjusted) : 0;
        double toitoiCert = ScoreToitoi(adjusted, hand.OpenMelds);
        double sanshokuDoujunCert = ScoreSanshokuDoujun(adjusted, hand.OpenMelds);
        double sanshokuDoukouCert = ScoreSanshokuDoukou(adjusted, hand.OpenMelds);
        double ittsuCert = ScoreIttsu(adjusted, hand.OpenMelds);
        var (chantaCert, junchanCert) = ScoreChantaRoute(adjusted, hand.OpenMelds);
        double honroutouCert = ScoreHonroutou(adjusted, hand.OpenMelds);
        double shousangenCert = ScoreShousangen(adjusted, hand.OpenMelds);
        double iipeikouCert = isClosed ? ScoreIipeikou(adjusted) : 0;
        // Closed hands can always riichi at tenpai for +1 han. Ignores points/turns-left,
        // which is a separate riichi-policy concern, not a yaku-projection one. Empty/degenerate
        // hands stay at zero so partial test snapshots and pre-deal states score cleanly.
        double riichiCert = isClosed && hand.ClosedTileCount > 0 ? 0.9 : 0;

        double suitednessHan = hasHonors
            ? (isClosed ? 3.0 : 2.0) * suitCert      // honitsu
            : (isClosed ? 6.0 : 5.0) * suitCert;     // chinitsu

        double han =
            1.0 * tanyaoCert
            + 1.0 * yakuhaiCert
            + suitednessHan
            + 2.0 * chiitoitsuCert
            + 2.0 * toitoiCert
            + (isClosed ? 2.0 : 1.0) * sanshokuDoujunCert
            + 2.0 * sanshokuDoukouCert
            + (isClosed ? 2.0 : 1.0) * ittsuCert
            + (isClosed ? 2.0 : 1.0) * chantaCert
            + (isClosed ? 3.0 : 2.0) * junchanCert
            + 2.0 * honroutouCert
            + 2.0 * shousangenCert
            + 1.0 * iipeikouCert
            + 1.0 * riichiCert;

        return Math.Min(1.0, han / TargetHan);
    }

    private static bool IsClosed(Hand hand)
    {
        foreach (var m in hand.OpenMelds)
            if (m.Kind != MeldKind.AnKan)
                return false;
        return true;
    }

    private static double ScoreTanyao(IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        int nonSimple = 0;
        int simples = 0;
        for (int id = 0; id < Tile.Count34; id++)
        {
            int c = closed[id];
            if (Tile.FromId(id).IsTerminalOrHonor)
                nonSimple += c;
            else
                simples += c;
        }
        foreach (var m in melds)
            foreach (var t in m.Tiles)
            {
                if (t.IsTerminalOrHonor)
                    nonSimple++;
                else
                    simples++;
            }

        if (simples == 0 || nonSimple >= 3)
            return 0;
        return (3 - nonSimple) / 3.0;
    }

    private static double ScoreYakuhai(IReadOnlyList<int> closed, IReadOnlyList<Meld> melds, StateSnapshot state)
    {
        int seatWindId = 27 + state.OurSeat;
        int roundWindId = 27 + state.RoundWind;

        double best = 0;
        for (int id = 27; id < Tile.Count34; id++)
        {
            bool isYakuhai = id >= 31;
            if (!isYakuhai && state.SeatInfoKnown)
                isYakuhai = id == seatWindId || id == roundWindId;
            if (!isYakuhai)
                continue;

            int total = closed[id];
            foreach (var m in melds)
                foreach (var t in m.Tiles)
                    if (t.Id == id)
                        total++;

            double cert = total switch
            {
                >= 3 => 1.0,
                2 => 0.5,
                1 => 0.05,
                _ => 0,
            };
            if (cert > best)
                best = cert;
        }
        return best;
    }

    /// <summary>
    /// Single-suit dominance shape used by both honitsu and chinitsu. Returns the readiness
    /// cert plus whether any honors are present, so the caller picks the right han multiplier
    /// (honors → honitsu 2/3 han, no honors → chinitsu 5/6 han).
    /// </summary>
    private static (double Cert, bool HasHonors) ScoreSuitedness(
        IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        Span<int> suitCounts = stackalloc int[3];
        int honors = 0;
        int totalNonHonor = 0;
        for (int suit = 0; suit < 3; suit++)
        {
            int sum = 0;
            for (int i = 0; i < 9; i++)
                sum += closed[suit * 9 + i];
            foreach (var m in melds)
                foreach (var t in m.Tiles)
                    if (t.Id >= suit * 9 && t.Id < (suit + 1) * 9)
                        sum++;
            suitCounts[suit] = sum;
            totalNonHonor += sum;
        }
        for (int id = 27; id < Tile.Count34; id++)
            honors += closed[id];
        foreach (var m in melds)
            foreach (var t in m.Tiles)
                if (t.IsHonor)
                    honors++;

        if (totalNonHonor == 0)
            return (0, honors > 0);

        int dominant = Math.Max(Math.Max(suitCounts[0], suitCounts[1]), suitCounts[2]);
        int offSuit = totalNonHonor - dominant;
        if (offSuit >= 4)
            return (0, honors > 0);
        return ((4 - offSuit) / 4.0, honors > 0);
    }

    /// <summary>Caller must gate on closed-only. Triplets/quads don't contribute — needs distinct pairs.</summary>
    private static double ScoreChiitoitsu(IReadOnlyList<int> closed)
    {
        int pairs = 0;
        for (int i = 0; i < Tile.Count34; i++)
            if (closed[i] == 2)
                pairs++;
        return Math.Min(1.0, pairs / 6.0);
    }

    /// <summary>
    /// Closed-only hands need ≥2 locked sources or ≥3 pairs — a single closed triplet alone
    /// reads as yakuhai, and 1–2 pairs is just incidental 1-shanten noise.
    /// </summary>
    private static double ScoreToitoi(IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        int openLocked = 0;
        foreach (var m in melds)
        {
            if (m.Kind == MeldKind.Chi)
                return 0;
            if (m.Kind is MeldKind.Pon or MeldKind.MinKan
                       or MeldKind.AnKan or MeldKind.ShouMinKan)
                openLocked++;
        }

        int closedLocked = 0;
        int pairs = 0;
        for (int id = 0; id < Tile.Count34; id++)
        {
            int c = closed[id];
            if (c >= 3)
                closedLocked++;
            else if (c == 2)
                pairs++;
        }

        int locked = openLocked + closedLocked;
        if (openLocked == 0 && locked < 2 && pairs < 3)
            return 0;

        double progress = locked + 0.5 * pairs;
        return Math.Min(1.0, progress / 4.0);
    }

    /// <summary>
    /// Product-of-readinesses across 3 suits: any fully-absent suit zeroes the offset,
    /// preventing the scorer from chasing partial 2-suit coverage that can never complete.
    /// </summary>
    private static double ScoreSanshokuDoujun(IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        Span<bool> chiAt = stackalloc bool[21];
        foreach (var m in melds)
        {
            if (m.Kind != MeldKind.Chi)
                continue;
            int firstId = m.Tiles[0].Id;
            int suit = firstId / TileIds.SuitSize;
            int offset = firstId % TileIds.SuitSize;
            if (suit < 3 && offset <= TileIds.SuitSize - 3)
                chiAt[offset * 3 + suit] = true;
        }

        double best = 0;
        for (int n = 0; n <= TileIds.SuitSize - 3; n++)
        {
            double product = 1.0;
            for (int suit = 0; suit < 3; suit++)
            {
                double ready;
                if (chiAt[n * 3 + suit])
                {
                    ready = 1.0;
                }
                else
                {
                    int suitBase = suit * TileIds.SuitSize;
                    int present = 0;
                    if (closed[suitBase + n]     > 0) present++;
                    if (closed[suitBase + n + 1] > 0) present++;
                    if (closed[suitBase + n + 2] > 0) present++;
                    ready = present / 3.0;
                }
                product *= ready;
            }
            if (product > best)
                best = product;
        }
        return best;
    }

    /// <summary>
    /// Same number triplet across all three suits. Product-of-readinesses keyed off triplet
    /// completion in each suit — zeroing if any suit has no copies of the number.
    /// </summary>
    private static double ScoreSanshokuDoukou(IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        Span<int> openTrip = stackalloc int[TileIds.SuitSize * 3];
        foreach (var m in melds)
        {
            if (m.Kind is not (MeldKind.Pon or MeldKind.MinKan or MeldKind.AnKan or MeldKind.ShouMinKan))
                continue;
            int id = m.Tiles[0].Id;
            if (id >= 27)
                continue;
            int suit = id / TileIds.SuitSize;
            int offset = id % TileIds.SuitSize;
            openTrip[offset * 3 + suit] = 3;
        }

        double best = 0;
        for (int n = 0; n < TileIds.SuitSize; n++)
        {
            double product = 1.0;
            for (int suit = 0; suit < 3; suit++)
            {
                int count = openTrip[n * 3 + suit];
                if (count == 0)
                    count = closed[suit * TileIds.SuitSize + n];
                double ready = Math.Min(count, 3) / 3.0;
                product *= ready;
            }
            if (product > best)
                best = product;
        }
        return best;
    }

    /// <summary>Same product-of-readinesses shape as sanshoku, but within a single suit at offsets {0, 3, 6}.</summary>
    private static double ScoreIttsu(IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        Span<bool> chiAt = stackalloc bool[9];
        foreach (var m in melds)
        {
            if (m.Kind != MeldKind.Chi)
                continue;
            int firstId = m.Tiles[0].Id;
            int suit = firstId / TileIds.SuitSize;
            int offset = firstId % TileIds.SuitSize;
            if (suit < 3 && (offset == 0 || offset == 3 || offset == 6))
                chiAt[(offset / 3) * 3 + suit] = true;
        }

        double best = 0;
        for (int suit = 0; suit < 3; suit++)
        {
            int suitBase = suit * TileIds.SuitSize;
            double product = 1.0;
            for (int subrun = 0; subrun < 3; subrun++)
            {
                double ready;
                if (chiAt[subrun * 3 + suit])
                {
                    ready = 1.0;
                }
                else
                {
                    int start = subrun * 3;
                    int present = 0;
                    if (closed[suitBase + start]     > 0) present++;
                    if (closed[suitBase + start + 1] > 0) present++;
                    if (closed[suitBase + start + 2] > 0) present++;
                    ready = present / 3.0;
                }
                product *= ready;
            }
            if (product > best)
                best = product;
        }
        return best;
    }

    /// <summary>
    /// Every group has at least one terminal-or-honor. Estimates density of non-simples;
    /// junchan is the honor-free variant of the same density check, so they're mutex.
    /// </summary>
    private static (double Chanta, double Junchan) ScoreChantaRoute(
        IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        int nonSimple = 0;
        int simples = 0;
        int honors = 0;
        for (int id = 0; id < Tile.Count34; id++)
        {
            int c = closed[id];
            var tile = Tile.FromId(id);
            if (tile.IsHonor)
            {
                honors += c;
                nonSimple += c;
            }
            else if (tile.IsTerminal)
            {
                nonSimple += c;
            }
            else
            {
                simples += c;
            }
        }
        foreach (var m in melds)
            foreach (var t in m.Tiles)
            {
                if (t.IsHonor) { honors++; nonSimple++; }
                else if (t.IsTerminal) nonSimple++;
                else simples++;
            }

        // Need a terminal/honor in every meld + pair, ~5 sets total. Below 5 there's no path.
        if (nonSimple < 5)
            return (0, 0);

        double cert = Math.Min(1.0, (nonSimple - 4) / 9.0);
        return honors == 0 ? (0, cert) : (cert, 0);
    }

    /// <summary>All tiles terminal-or-honor. Combines with toitoi or chiitoitsu in practice.</summary>
    private static double ScoreHonroutou(IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        int simples = 0;
        int nonSimple = 0;
        foreach (var m in melds)
            foreach (var t in m.Tiles)
                if (t.IsTerminalOrHonor) nonSimple++;
                else simples++;
        for (int id = 0; id < Tile.Count34; id++)
        {
            int c = closed[id];
            if (c == 0) continue;
            if (Tile.FromId(id).IsTerminalOrHonor) nonSimple += c;
            else simples += c;
        }

        // Need ≥5 non-simple tiles to support 4 melds + pair, all terminal/honor. Otherwise
        // even a zero-simple hand (e.g. empty pre-deal state) shouldn't claim the route.
        if (simples >= 4 || nonSimple < 5)
            return 0;
        return (4 - simples) / 4.0;
    }

    /// <summary>
    /// Two dragon triplets + one dragon pair. Sorts the three dragon-id counts and measures
    /// progress against the needed [3, 3, 2] shape — saturates at exactly that distribution.
    /// </summary>
    private static double ScoreShousangen(IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        Span<int> dragonCounts = stackalloc int[3];
        for (int i = 0; i < 3; i++)
            dragonCounts[i] = closed[31 + i];
        foreach (var m in melds)
            foreach (var t in m.Tiles)
                if (t.Id >= 31)
                    dragonCounts[t.Id - 31]++;

        // Sort descending in-place.
        if (dragonCounts[0] < dragonCounts[1])
            (dragonCounts[0], dragonCounts[1]) = (dragonCounts[1], dragonCounts[0]);
        if (dragonCounts[1] < dragonCounts[2])
            (dragonCounts[1], dragonCounts[2]) = (dragonCounts[2], dragonCounts[1]);
        if (dragonCounts[0] < dragonCounts[1])
            (dragonCounts[0], dragonCounts[1]) = (dragonCounts[1], dragonCounts[0]);

        // Need a completed triplet before crediting shousangen at all — three dragon singletons
        // are nowhere near a 2-trip + pair shape and shouldn't read as 3/8 of the way there.
        // Also need at least one copy of every dragon for the pair slot.
        if (dragonCounts[0] < 3 || dragonCounts[2] == 0)
            return 0;

        int progress = Math.Min(dragonCounts[0], 3) + Math.Min(dragonCounts[1], 3) + Math.Min(dragonCounts[2], 2);
        return progress / 8.0;
    }

    /// <summary>
    /// Closed-only twin identical run, e.g. 234m + 234m. Requires min(counts) ≥ 2 across the
    /// three consecutive ids — one of each is just a normal run and shouldn't credit iipeikou.
    /// </summary>
    private static double ScoreIipeikou(IReadOnlyList<int> closed)
    {
        double best = 0;
        for (int suit = 0; suit < 3; suit++)
        {
            int suitBase = suit * TileIds.SuitSize;
            for (int n = 0; n <= TileIds.SuitSize - 3; n++)
            {
                int a = closed[suitBase + n];
                int b = closed[suitBase + n + 1];
                int c = closed[suitBase + n + 2];
                int doubled = Math.Min(Math.Min(a, b), c);
                double cert = doubled >= 2 ? 1.0 : 0;
                if (cert > best)
                    best = cert;
            }
        }
        return best;
    }
}

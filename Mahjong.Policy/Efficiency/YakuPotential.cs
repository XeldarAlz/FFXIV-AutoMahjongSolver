using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

/// <summary>
/// Continuous [0, 1] yaku-completion estimate normalised to Doman's 2-han minimum. Two soft
/// 0.5-cert routes beat one 1.0-cert route — biases toward yaku-stacked hands.
/// </summary>
public static class YakuPotential
{
    private const double TargetHan = 2.0;

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
        double honitsuCert = ScoreHonitsu(adjusted, hand.OpenMelds);
        double chiitoitsuCert = hand.OpenMelds.Count == 0 ? ScoreChiitoitsu(adjusted) : 0;
        double toitoiCert = ScoreToitoi(adjusted, hand.OpenMelds);
        double sanshokuDoujunCert = ScoreSanshokuDoujun(adjusted, hand.OpenMelds);
        double ittsuCert = ScoreIttsu(adjusted, hand.OpenMelds);

        double han =
            1.0 * tanyaoCert +
            1.0 * yakuhaiCert +
            (isClosed ? 3.0 : 2.0) * honitsuCert +
            2.0 * chiitoitsuCert +
            2.0 * toitoiCert +
            (isClosed ? 2.0 : 1.0) * sanshokuDoujunCert +
            (isClosed ? 2.0 : 1.0) * ittsuCert;

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

    private static double ScoreHonitsu(IReadOnlyList<int> closed, IReadOnlyList<Meld> melds)
    {
        Span<int> suitCounts = stackalloc int[3];
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
        if (totalNonHonor == 0)
            return 0;

        int dominant = Math.Max(Math.Max(suitCounts[0], suitCounts[1]), suitCounts[2]);
        int offSuit = totalNonHonor - dominant;
        if (offSuit >= 4)
            return 0;
        return (4 - offSuit) / 4.0;
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
}

using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

/// <summary>
/// Estimates the "yaku potential" of a 13-tile hand state for use as a discard
/// feature. Returns a continuous [0, 1] score reflecting how plausibly the hand
/// can complete with Doman's 2-han minimum.
///
/// <para>Each detected route is multiplied by approximate han value and the
/// total is normalised to <see cref="TargetHan"/> = 2 (Doman <c>MinHan</c>).
/// Two soft routes at 0.5 cert beat one route at 1.0 cert: that nudges the
/// scorer toward yaku-stacked hands rather than betting everything on one
/// yaku that may evaporate.</para>
///
/// <para>Routes covered (V1):
/// <list type="bullet">
///   <item><b>Tanyao</b> — 1 han. Decays with terminal/honor count.</item>
///   <item><b>Yakuhai</b> — 1 han per triplet. Honors-only count, with
///         seat/round winds gated on <see cref="StateSnapshot.SeatInfoKnown"/>.</item>
///   <item><b>Honitsu</b> — 3 han closed / 2 open. Decays with off-suit
///         non-honor count past the dominant suit.</item>
///   <item><b>Chiitoitsu</b> — 2 han closed-only. Scales with pair count.</item>
/// </list>
/// Toitoi/Sanshoku/Ittsu are deferred — partial-hand detection is noisier than
/// the discard term they'd add, and the four routes above already cover the
/// common yakuless-tenpai trap that motivated this feature.</para>
/// </summary>
public static class YakuPotential
{
    private const double TargetHan = 2.0;

    /// <summary>
    /// Score the 13-tile hand resulting from discarding <paramref name="removed"/>
    /// out of <paramref name="hand"/>. Pass <c>null</c> for <paramref name="removed"/>
    /// to score the hand as-is (e.g. when the closed counts already represent
    /// the post-discard state, or for unit tests on a 13-tile hand).
    /// </summary>
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

        double han =
            1.0 * tanyaoCert +
            1.0 * yakuhaiCert +
            (isClosed ? 3.0 : 2.0) * honitsuCert +
            2.0 * chiitoitsuCert;

        return Math.Min(1.0, han / TargetHan);
    }

    private static bool IsClosed(Hand hand)
    {
        foreach (var m in hand.OpenMelds)
            if (m.Kind != MeldKind.AnKan)
                return false;
        return true;
    }

    /// <summary>
    /// Tanyao closeness: 1.0 with zero terminals/honors and a non-empty body
    /// of simples; degrades linearly to 0 at 3+ such tiles. Counts open-meld
    /// tiles too — even one terminal in a called meld locks tanyao out
    /// forever. Empty / honors-only hands return 0; tanyao requires simples
    /// to actually exist as the target shape.
    /// </summary>
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

            // 3+ = locked triplet; 2 = needs one more (1/3 of remaining tiles
            // out there, weighted higher because shanten-cheap); 1 = remote.
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
    /// Honitsu closeness: pick the suit with the most tiles (ignoring honors,
    /// which are universally legal in honitsu) and decay with off-suit count.
    /// 0 off-suit = 1.0; 4+ off-suit = 0. Honors-only hands return 0 (would be
    /// tsuuiisou yakuman territory; not in this feature's scope).
    /// </summary>
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

    /// <summary>
    /// Chiitoitsu closeness: count of tiles held at exactly 2 copies. Triplets
    /// and quads don't contribute — chiitoitsu wants 7 *distinct* pairs and
    /// holding three of a kind directs the hand toward toitoi/standard shape,
    /// not seven-pairs. Six pairs reads as 1.0 (one pair away). Caller should
    /// only invoke this for fully-concealed hands.
    /// </summary>
    private static double ScoreChiitoitsu(IReadOnlyList<int> closed)
    {
        int pairs = 0;
        for (int i = 0; i < Tile.Count34; i++)
            if (closed[i] == 2)
                pairs++;
        return Math.Min(1.0, pairs / 6.0);
    }
}

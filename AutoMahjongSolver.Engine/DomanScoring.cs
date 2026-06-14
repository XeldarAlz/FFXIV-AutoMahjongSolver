namespace AutoMahjongSolver.Engine;

/// <summary>
/// FFXIV Doman Mahjong scoring: a flat han-to-points table with no fu calculation. The sub-mangan
/// values below follow the standard riichi limits (kiriage mangan at 4 han) and must be reconciled
/// against live result screens — they are close enough for EV ranking either way.
/// </summary>
public static class DomanScoring
{
    public const int YakumanPoints = 32000;

    private static readonly int[] NonDealerPoints =
        [0, 1000, 2000, 3900, 8000, 8000, 12000, 12000, 16000, 16000, 16000, 24000, 24000];

    public static int Points(int han, bool dealer, bool yakuman = false)
    {
        int basePoints = yakuman || han >= 13
            ? YakumanPoints
            : NonDealerPoints[Math.Clamp(han, 0, 12)];

        return dealer ? basePoints * 3 / 2 : basePoints;
    }

    public static int Points(WinResult result, bool dealer)
        => result.HasYaku ? Points(result.TotalHan, dealer, result.IsYakuman) : 0;
}

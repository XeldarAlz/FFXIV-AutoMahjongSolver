namespace Mahjong.Rules.Scoring;

public sealed class StandardScoringRule : IScoringRule
{
    public ScoringTier ResolveTier(int han, int fu, bool isYakuman)
    {
        if (isYakuman)
        {
            int multiplier = Math.Max(1, han / HanValues.Yakuman);
            return new ScoringTier("yakuman", ScoringConstants.YakumanBase * multiplier);
        }

        if (han >= ScoringConstants.KazoeYakumanHan)
            return new ScoringTier("yakuman", ScoringConstants.YakumanBase);
        if (han >= ScoringConstants.SanbaimanHan)
            return new ScoringTier("sanbaiman", ScoringConstants.SanbaimanBase);
        if (han >= ScoringConstants.BaimanHan)
            return new ScoringTier("baiman", ScoringConstants.BaimanBase);
        if (han >= ScoringConstants.HanemanHan)
            return new ScoringTier("haneman", ScoringConstants.HanemanBase);
        if (han >= ScoringConstants.ManganHan)
            return new ScoringTier("mangan", ScoringConstants.ManganBase);

        long basePoints = (long)fu * (1L << (han + 2));
        if (basePoints >= ScoringConstants.ManganBase)
            return new ScoringTier("mangan", ScoringConstants.ManganBase);
        return new ScoringTier(string.Empty, (int)basePoints);
    }

    public Payments Pay(ScoringTier tier, bool isDealer, WinKind kind)
    {
        int basePoints = tier.BasePoints;
        if (isDealer)
        {
            if (kind == WinKind.Ron)
            {
                int total = RoundUp(basePoints * 6);
                return new Payments(0, 0, total, total);
            }
            int per = RoundUp(basePoints * 2);
            return new Payments(0, per, 0, per * 3);
        }

        if (kind == WinKind.Ron)
        {
            int total = RoundUp(basePoints * 4);
            return new Payments(0, 0, total, total);
        }
        int dealerPay = RoundUp(basePoints * 2);
        int otherPay = RoundUp(basePoints);
        return new Payments(dealerPay, otherPay, 0, dealerPay + otherPay * 2);
    }

    private static int RoundUp(int value)
    {
        int step = ScoringConstants.PaymentRoundStep;
        int rem = value % step;
        return rem == 0 ? value : value + (step - rem);
    }
}

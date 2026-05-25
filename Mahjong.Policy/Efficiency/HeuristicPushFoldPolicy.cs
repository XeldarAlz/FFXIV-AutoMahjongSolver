using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

/// <summary>
/// EV(push) = P(complete) × E(hand value) − E(deal-in cost). Push when positive.
/// Hard veto only for hopelessly-far hands under late-round pressure where the EV math is noisiest.
/// </summary>
public sealed class HeuristicPushFoldPolicy : IPushFoldPolicy
{
    private const int LateRoundWallThreshold = 15;
    private const int FarShantenHardVeto = 3;

    public Decision<PushFoldStance> Evaluate(
        StateSnapshot state,
        IOpponentModel opponentModel,
        ScoredDiscard plannedDiscard)
    {
        ArgumentNullException.ThrowIfNull(opponentModel);

        int shanten = plannedDiscard.ShantenAfter;
        var threats = SummarizeThreats(state, opponentModel);
        double dealInCost = plannedDiscard.DealInCost;

        if (shanten >= FarShantenHardVeto)
        {
            if (threats.AnyRiichi)
                return Fold("far-vs-riichi", $"{shanten}-shanten vs a riichi");
            if (state.WallRemaining < LateRoundWallThreshold)
                return Fold("late-round-far", $"{shanten}-shanten with wall {state.WallRemaining} remaining");
            if (threats.MaxTenpaiProbability >= 0.7)
                return Fold("far-vs-tenpai-prob",
                    $"{shanten}-shanten, opponent tenpai-prob ≥ 0.7");
        }

        bool isDealer = state.OurSeat == state.DealerSeat;
        double pComplete = ProbComplete(shanten, plannedDiscard.UkeireWeighted, state.WallRemaining);
        double handValue = EstimateHandValuePoints(plannedDiscard, isDealer);
        double evPush = pComplete * handValue - dealInCost;

        if (evPush > 0)
            return Push("ev-push",
                $"EV={evPush:F0} (p_complete={pComplete:F2} × value={handValue:F0} − danger={dealInCost:F0})");

        return Fold("ev-fold",
            $"EV={evPush:F0} (p_complete={pComplete:F2} × value={handValue:F0} − danger={dealInCost:F0})");
    }

    private static double ProbComplete(int shanten, int ukeireWeighted, int wallRemaining)
    {
        if (shanten >= 4) return 0.01;
        double turnsLeft = Math.Max(0.0, wallRemaining / 4.0);
        double perTurn = shanten switch
        {
            0 => 0.045 * Math.Max(1, ukeireWeighted),
            1 => 0.020 * Math.Max(1, ukeireWeighted),
            2 => 0.009 * Math.Max(1, ukeireWeighted),
            3 => 0.004 * Math.Max(1, ukeireWeighted),
            _ => 0.001,
        };
        perTurn = Math.Min(perTurn, 0.35);
        double cumulative = 1.0 - Math.Pow(1.0 - perTurn, turnsLeft);
        return Math.Clamp(cumulative, 0.0, 0.95);
    }

    private static double EstimateHandValuePoints(ScoredDiscard d, bool isDealer)
    {
        double han = d.YakuPotential * YakuPotential.TargetHan + d.DoraRetained + d.YakuhaiRetained;
        double baseHan = Math.Max(0.0, han);
        double pts;
        if (baseHan < 1.0) pts = 0;
        else if (baseHan < 2.0) pts = 1500;
        else if (baseHan < 3.0) pts = 2700;
        else if (baseHan < 4.0) pts = 5200;
        else if (baseHan < 5.0) pts = 7700;
        else pts = 8000 + (baseHan - 5.0) * 4000;
        return isDealer ? pts * 1.5 : pts;
    }

    private static ThreatSummary SummarizeThreats(StateSnapshot state, IOpponentModel model)
    {
        bool anyRiichi = false;
        double maxTenpai = 0;
        for (int opp = 0; opp < model.OpponentCount; opp++)
        {
            int absSeat = (state.OurSeat + 1 + opp) % 4;
            if (state.Seats[absSeat].Riichi)
                anyRiichi = true;
            double tp = model.TenpaiProbability(opp);
            if (tp > maxTenpai)
                maxTenpai = tp;
        }
        return new ThreatSummary(anyRiichi, maxTenpai);
    }

    private static Decision<PushFoldStance> Push(string code, string display)
        => new(Accept: true, Value: PushFoldStance.Push, Reason: new Reason(code, display));

    private static Decision<PushFoldStance> Fold(string code, string display)
        => new(Accept: true, Value: PushFoldStance.Fold, Reason: new Reason(code, display));

    private readonly record struct ThreatSummary(bool AnyRiichi, double MaxTenpaiProbability);
}

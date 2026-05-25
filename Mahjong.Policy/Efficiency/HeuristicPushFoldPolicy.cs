using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

public sealed class HeuristicPushFoldPolicy : IPushFoldPolicy
{
    private const int TenpaiDealInCap = 3000;
    private const int OneShantenRiichiDealInCap = 500;
    private const int LateRoundWallThreshold = 15;
    private const double HighTenpaiProbability = 0.7;

    public Decision<PushFoldStance> Evaluate(
        StateSnapshot state,
        IOpponentModel opponentModel,
        ScoredDiscard plannedDiscard)
    {
        ArgumentNullException.ThrowIfNull(opponentModel);

        var threats = SummarizeThreats(state, opponentModel);
        double dealInCost = opponentModel.ExpectedDealInCost(plannedDiscard.Discard.Id);
        int shanten = plannedDiscard.ShantenAfter;

        if (shanten == 0)
            return EvaluateTenpai(dealInCost);
        if (shanten == 1)
            return EvaluateOneShanten(threats.AnyRiichi, dealInCost);
        return EvaluateFarFromTenpai(state, shanten, threats);
    }

    private static Decision<PushFoldStance> EvaluateTenpai(double dealInCost)
    {
        if (dealInCost > TenpaiDealInCap)
            return Fold("tenpai-deal-in", $"tenpai but deal-in cost {dealInCost:F0} > {TenpaiDealInCap}");
        return Push("tenpai-push", $"tenpai, push (deal-in cost {dealInCost:F0})");
    }

    private static Decision<PushFoldStance> EvaluateOneShanten(bool anyRiichi, double dealInCost)
    {
        if (anyRiichi && dealInCost > OneShantenRiichiDealInCap)
            return Fold("one-shanten-vs-riichi", $"1-shanten vs riichi, danger {dealInCost:F0}");
        return Push("one-shanten-push", "1-shanten, push");
    }

    private static Decision<PushFoldStance> EvaluateFarFromTenpai(
        StateSnapshot state, int shanten, ThreatSummary threats)
    {
        if (state.WallRemaining < LateRoundWallThreshold)
            return Fold("late-round-far", $"{shanten}-shanten with wall {state.WallRemaining} remaining");
        if (threats.AnyRiichi)
            return Fold("far-vs-riichi", $"{shanten}-shanten vs a riichi");
        if (threats.MaxTenpaiProbability >= HighTenpaiProbability)
            return Fold("far-vs-tenpai-prob",
                $"{shanten}-shanten, opponent tenpai-prob ≥ {HighTenpaiProbability:F1}");
        return Push("far-quiet", $"{shanten}-shanten, opponents quiet, push");
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

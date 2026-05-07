using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

/// <summary>
/// Heuristic <see cref="IRiichiPolicy"/>. Declares riichi when:
/// <list type="bullet">
///   <item>Hand is closed (ankan does not break menzen).</item>
///   <item>Hand reaches tenpai after the planned discard.</item>
///   <item>Score ≥ 1000 (riichi stick cost).</item>
///   <item>Wall ≥ 4 — at least one chance to draw the win.</item>
///   <item>Weighted ukeire ≥ 4 live tiles after the discard.</item>
/// </list>
/// Damaten preference is owed: when a value estimator lands, declare-riichi
/// gates against "would already score mangan+ without it."
/// </summary>
public sealed class HeuristicRiichiPolicy : IRiichiPolicy
{
    private const int MinScore = 1000;
    private const int MinWallRemaining = 4;
    private const int MinWeightedUkeire = 4;

    public Decision<bool> Evaluate(StateSnapshot state, ScoredDiscard plannedDiscard)
    {
        if (plannedDiscard.ShantenAfter != 0)
            return Decline("not-tenpai", "not tenpai after discard");

        if (!IsClosed(state))
            return Decline("hand-open", "hand is open");

        int ourScore = state.Scores[state.OurSeat];
        if (ourScore < MinScore)
            return Decline("low-score", $"score {ourScore} < {MinScore}");

        if (state.WallRemaining < MinWallRemaining)
            return Decline("late-round", $"wall {state.WallRemaining} < {MinWallRemaining}");

        if (plannedDiscard.UkeireWeighted < MinWeightedUkeire)
            return Decline("thin-waits", $"only {plannedDiscard.UkeireWeighted} live accepting tiles");

        return new Decision<bool>(
            Accept: true,
            Value: true,
            Reason: new Reason(
                Code: "riichi-ready",
                Display: $"tenpai closed, score={ourScore}, wall={state.WallRemaining}, " +
                         $"ukeire={plannedDiscard.UkeireKinds}kinds/{plannedDiscard.UkeireWeighted}w",
                Data: new Dictionary<string, object>
                {
                    ["score"] = ourScore,
                    ["wallRemaining"] = state.WallRemaining,
                    ["ukeireKinds"] = plannedDiscard.UkeireKinds,
                    ["ukeireWeighted"] = plannedDiscard.UkeireWeighted,
                }));
    }

    private static bool IsClosed(StateSnapshot state)
    {
        foreach (var m in state.OurMelds)
        {
            if (m.Kind != MeldKind.AnKan)
                return false;
        }
        return true;
    }

    private static Decision<bool> Decline(string code, string display) =>
        new(Accept: false, Value: false, Reason: new Reason(Code: code, Display: display));
}

using Mahjong.Engine;
using Mahjong.Policy.Opponents;
using Mahjong.Policy.Placement;

namespace Mahjong.Policy.Efficiency;

public sealed class EfficiencyPolicy : IPolicy
{
    private readonly IOpponentModel opponentModel;
    private readonly IDiscardPolicy discard;
    private readonly ICallPolicy call;
    private readonly IRiichiPolicy riichi;
    private readonly IPushFoldPolicy pushFold;

    public EfficiencyPolicy(
        IOpponentModel opponentModel,
        IDiscardPolicy discard,
        ICallPolicy call,
        IRiichiPolicy riichi,
        IPushFoldPolicy pushFold)
    {
        ArgumentNullException.ThrowIfNull(opponentModel);
        ArgumentNullException.ThrowIfNull(discard);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(riichi);
        ArgumentNullException.ThrowIfNull(pushFold);
        this.opponentModel = opponentModel;
        this.discard = discard;
        this.call = call;
        this.riichi = riichi;
        this.pushFold = pushFold;
    }

    public EfficiencyPolicy(IWeightProvider? weightProvider = null)
        : this(BuildDefault(weightProvider ?? new DefaultWeightProvider()))
    { }

    public EfficiencyPolicy(DiscardWeights weights)
        : this(BuildWithDiscardWeights(weights))
    { }

    private EfficiencyPolicy(EfficiencyPolicy template)
        : this(template.opponentModel, template.discard, template.call, template.riichi, template.pushFold)
    { }

    public ActionChoice Choose(StateSnapshot state)
    {
        var legal = state.Legal;

        if (legal.Can(ActionFlags.Tsumo))
            return ActionChoice.DeclareTsumo("tsumo legal");
        if (legal.Can(ActionFlags.Ron))
            return ActionChoice.DeclareRon("ron legal");

        opponentModel.Update(state);

        var steps = new List<Reason>(3);

        if (HasCallOffered(legal))
        {
            var callDecision = call.Evaluate(state);
            steps.Add(callDecision.Reason);
            if (callDecision.Accept && callDecision.Value is { } accepted)
                return BuildCallChoice(accepted, callDecision.Reason, steps);
            if (legal.Can(ActionFlags.Pass))
                return ActionChoice.Pass($"pass: {callDecision.Reason.Display}", steps);
        }

        if (!legal.Can(ActionFlags.Discard))
            return ActionChoice.Pass("no actionable legal action for efficiency policy");

        if (TsumogiriFallback(state) is { } fallback)
            return fallback;

        ScoredDiscard[] scored;
        try
        {
            scored = discard.Score(state);
        }
        catch (ArgumentException ex)
        {
            return ActionChoice.Pass(
                $"scorer invariant: {ex.Message} (likely mid-transition state)");
        }
        if (scored.Length == 0)
            return ActionChoice.Pass("no legal discards found");

        var best = ApplyPushFold(state, scored, steps);

        if (legal.Can(ActionFlags.Riichi))
        {
            var riichiDecision = riichi.Evaluate(state, best);
            steps.Add(riichiDecision.Reason);
            if (riichiDecision.Accept && riichiDecision.Value)
            {
                return ActionChoice.DeclareRiichi(
                    best.Discard,
                    $"riichi on {best.Discard}: {riichiDecision.Reason.Display}",
                    steps);
            }
        }

        var summary = FormatDiscardSummary(best);
        steps.Add(new Reason("discard", summary));
        return ActionChoice.Discard(best.Discard, summary, steps);
    }

    private ScoredDiscard ApplyPushFold(StateSnapshot state, ScoredDiscard[] scored, List<Reason> steps)
    {
        var best = scored[0];
        var pushFoldDecision = pushFold.Evaluate(state, opponentModel, best);
        steps.Add(pushFoldDecision.Reason);

        if (pushFoldDecision.Accept && pushFoldDecision.Value == PushFoldStance.Fold)
        {
            ScoredDiscard safest = scored[0];
            for (int i = 1; i < scored.Length; i++)
            {
                if (scored[i].DealInCost < safest.DealInCost)
                    safest = scored[i];
            }
            return safest;
        }

        return best;
    }

    private static bool HasCallOffered(LegalActions legal) =>
        legal.Can(ActionFlags.Pon) || legal.Can(ActionFlags.Chi) ||
        legal.Can(ActionFlags.MinKan) || legal.Can(ActionFlags.ShouMinKan) ||
        legal.Can(ActionFlags.AnKan);

    private static ActionChoice BuildCallChoice(MeldCandidate cand, Reason reason, IReadOnlyList<Reason> steps)
    {
        var kind = cand.Kind switch
        {
            MeldKind.Pon => ActionKind.Pon,
            MeldKind.Chi => ActionKind.Chi,
            MeldKind.AnKan => ActionKind.AnKan,
            MeldKind.MinKan => ActionKind.MinKan,
            MeldKind.ShouMinKan => ActionKind.ShouMinKan,
            _ => ActionKind.Pass,
        };
        return new ActionChoice(kind, Call: cand, Reasoning: $"call: {reason.Display}", Steps: steps);
    }

    /// <summary>
    /// Pauses suggestions when hand+meld arithmetic ≠ 14 (typically a call MeldTracker
    /// couldn't reconstruct). Must use <see cref="Meld.TileCount"/>, not melds.Count*3 —
    /// kans count 4 and a per-kan undercount left those hands stuck in fallback forever.
    /// </summary>
    private static ActionChoice? TsumogiriFallback(StateSnapshot state)
    {
        int meldTiles = 0;
        for (int i = 0; i < state.OurMelds.Count; i++)
            meldTiles += state.OurMelds[i].TileCount;
        int totalTiles = state.Hand.Count + meldTiles;
        if (totalTiles == 14 || state.Hand.Count == 0)
            return null;
        return ActionChoice.Pass(
            $"hand state out of sync — pausing hints (closed={state.Hand.Count}, meld-tiles={meldTiles}; expected 14)");
    }

    private static string FormatDiscardSummary(ScoredDiscard best) =>
        $"best={best.Discard} shanten={best.ShantenAfter} ukeire={best.UkeireKinds}kinds/{best.UkeireWeighted}w " +
        $"dora={best.DoraRetained} yakuhai={best.YakuhaiRetained} yaku-pot={best.YakuPotential:F2} score={best.Score:F1}";

    private static EfficiencyPolicy BuildDefault(IWeightProvider provider)
    {
        var bundle = provider.Current;
        var opponent = new OpponentModel(bundle.Opponent);
        var placement = new PlacementAdjuster(bundle.Placement);
        return new EfficiencyPolicy(
            opponent,
            new HeuristicDiscardPolicy(provider, opponent, placement),
            new HeuristicCallPolicy(),
            new HeuristicRiichiPolicy(),
            new HeuristicPushFoldPolicy());
    }

    private static EfficiencyPolicy BuildWithDiscardWeights(DiscardWeights weights)
    {
        var bundle = WeightBundle.Default with { Discard = weights };
        return BuildDefault(new InMemoryWeightProvider(bundle));
    }

    private sealed class InMemoryWeightProvider : IWeightProvider
    {
        public WeightBundle Current { get; }
        public event Action<WeightBundle>? Changed { add { } remove { } }
        public InMemoryWeightProvider(WeightBundle bundle) => Current = bundle;
    }
}

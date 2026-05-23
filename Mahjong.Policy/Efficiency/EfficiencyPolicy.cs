using Mahjong.Engine;
using Mahjong.Policy.Opponents;
using Mahjong.Policy.Placement;

namespace Mahjong.Policy.Efficiency;

/// <summary>
/// Heuristic top-level <see cref="IPolicy"/>. Composes the four sub-policies
/// (<see cref="IDiscardPolicy"/>, <see cref="ICallPolicy"/>,
/// <see cref="IRiichiPolicy"/>, <see cref="IPushFoldPolicy"/>) plus an
/// <see cref="IOpponentModel"/> that's updated once per decision so every
/// sub-policy reads consistent threat data.
///
/// Decision precedence:
///   1. Agari (legal Tsumo/Ron) — declare immediately.
///   2. Call offered — accept iff <see cref="ICallPolicy"/> says yes.
///   3. Discard pipeline:
///        a. Tsumogiri fallback when meld+hand counts are inconsistent.
///        b. Pick top discard from <see cref="IDiscardPolicy"/>.
///        c. Push/fold check; on Fold, swap to lowest-deal-in-cost cut.
///        d. Riichi check on the chosen cut; declare iff yes.
///        e. Plain discard otherwise.
///
/// Each step contributes a <see cref="Reason"/> to <see cref="ActionChoice.Steps"/>
/// so the UI can render the rationale chain without parsing strings.
/// </summary>
public sealed class EfficiencyPolicy : IPolicy
{
    private readonly IOpponentModel opponentModel;
    private readonly IDiscardPolicy discard;
    private readonly ICallPolicy call;
    private readonly IRiichiPolicy riichi;
    private readonly IPushFoldPolicy pushFold;

    /// <summary>Full DI constructor.</summary>
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

    /// <summary>Convenience constructor — wires up heuristic defaults from the given weights.</summary>
    public EfficiencyPolicy(IWeightProvider? weightProvider = null)
        : this(BuildDefault(weightProvider ?? new DefaultWeightProvider()))
    { }

    /// <summary>Convenience constructor — overrides discard weights only; everything else default.</summary>
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

        // Tsumogiri fallback for inconsistent meld+hand counts (e.g. round-end races).
        if (TsumogiriFallback(state) is { } fallback)
            return fallback;

        ScoredDiscard[] scored;
        try
        {
            scored = discard.Score(state);
        }
        catch (ArgumentException ex)
        {
            // DiscardScorer requires a shanten-valid 14-tile hand
            // (closed + 3*melds.Count == 14). Mid-transition states the
            // variant occasionally surfaces with Discard legal — e.g.
            // post-minkan before the rinshan replacement tile lands in
            // the hand array — fail that check. Pre-fix, the exception
            // bubbled to AutoPlayLoop and re-fired every 3 seconds for
            // minutes (live: 2026-05-23T15:29..32, state=6 hand=10
            // melds=1 minkan, ~50+ throws in 3 minutes). Now we return
            // Pass with a clear reason — the next tick's snapshot may
            // have stabilized (rinshan tile arrived, MeldTracker caught
            // up, etc.) and we'll re-enter the normal flow.
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
            // Swap to the lowest-deal-in cut available.
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
    /// Detect a hand/meld count mismatch — typically the symptom of a call
    /// the plugin saw fire but couldn't reconstruct the meld for (the
    /// claimed-tile parsing failed, so MeldTracker didn't record it). The
    /// closed hand shrank but OurMelds didn't grow, leaving total ≠ 14.
    ///
    /// <para>Old behavior was to "tsumogiri" — discard the last hand tile
    /// blind. That confidently surfaced a misleading suggestion in the hint
    /// UI and a misleading auto-click in auto mode. Both were worse than no
    /// suggestion: the user saw the highlight on a tile that had nothing to
    /// do with their actual hand state.</para>
    ///
    /// <para>Now we return Pass with a clear "out of sync" reason. The hint
    /// UI won't draw an overlay (DiscardTile is null), the auto-play loop
    /// won't click, and the next state event whose count reconciles back to
    /// 14 unblocks normal hint flow.</para>
    ///
    /// <para>Total-tile arithmetic uses each meld's actual tile count
    /// (<see cref="Meld.TileCount"/>) so kans contribute 4 — not 3 —
    /// to the invariant. Pre-fix this used <c>melds.Count * 3</c> which
    /// silently undercounted every kan by 1 and left every kan-bearing
    /// hand stuck in fallback for the rest of the round.</para>
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

    /// <summary>
    /// Tiny inline provider for the <c>EfficiencyPolicy(DiscardWeights)</c> back-compat
    /// constructor. Doesn't fire <see cref="IWeightProvider.Changed"/> — the bundle is
    /// frozen for the lifetime of the policy.
    /// </summary>
    private sealed class InMemoryWeightProvider : IWeightProvider
    {
        public WeightBundle Current { get; }
        public event Action<WeightBundle>? Changed { add { } remove { } }
        public InMemoryWeightProvider(WeightBundle bundle) => Current = bundle;
    }
}

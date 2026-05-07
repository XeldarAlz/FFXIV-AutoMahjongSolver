# Policy (Mahjong.Policy)

Concrete implementations of the Phase 3 abstractions. Top-level
`EfficiencyPolicy` composes four heuristic sub-policies, plus an MCTS
fallback (`IsmctsPolicy`) for close decisions.

## Composition (Phase 4)

```
EfficiencyPolicy : IPolicy
    │
    ├─→ IDiscardPolicy    (HeuristicDiscardPolicy)
    ├─→ ICallPolicy       (HeuristicCallPolicy)
    ├─→ IRiichiPolicy     (HeuristicRiichiPolicy)
    ├─→ IPushFoldPolicy   (HeuristicPushFoldPolicy)
    └─→ IOpponentModel    (OpponentModel)
                            └─→ OpponentWeights
```

All sub-policies and the opponent model are constructor-injected — testable
in isolation, swappable for different ruleset / weight bundles.

## MCTS

`IsmctsPolicy` falls through to `EfficiencyPolicy` for non-close decisions.
On close discards (top-2 heuristic gap < ε), runs `MctsSearch` with:
- `Determinizer` for hidden-info sampling (uses `IRandomSource`)
- `Rollout` (implements `IRolloutPolicy`) for leaf evaluation
- `MctsNodePool : INodePool<MctsNode>` — search tree allocations are
  rented + returned per determinization, not GC'd.

## Tuning

`Tuning/EvolutionaryTuner` and `Tuning/WeightTuner` produce
`weights.json` files that `JsonWeightProvider` loads at runtime.
**Tuner uses `RiichiRuleSet`** — see `docs/ruleset.md` for why mixing
rulesets corrupts tuning data.

## Consumers

The plugin (`Mahjong.Plugin.Dalamud`) constructs `EfficiencyPolicy` / `IsmctsPolicy`
through its MEDI container; the `Tuner` console exe runs offline weight
optimization.

## Tests

`tests/Policy.Tests/` — 77 tests covering each sub-policy in isolation,
MCTS pool semantics, weight bundle defaults, JSON weight provider
round-trip, structured `Decision<T>` rationale.

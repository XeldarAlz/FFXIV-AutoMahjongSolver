# Policy (Mahjong.Policy)

Concrete implementations of the Phase 3 abstractions. Top-level
`EfficiencyPolicy` composes four heuristic sub-policies.

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

## Tuning

`Tuning/EvolutionaryTuner` and `Tuning/WeightTuner` produce
`weights.json` files that `JsonWeightProvider` loads at runtime.
**Tuner uses `RiichiRuleSet`** — see `docs/ruleset.md` for why mixing
rulesets corrupts tuning data.

## Consumers

The plugin (`Mahjong.Plugin.Dalamud`) constructs `EfficiencyPolicy`
through its MEDI container; the `Tuner` console exe runs offline weight
optimization.

## Tests

`tests/Policy.Tests/` covers each sub-policy in isolation, weight bundle
defaults, JSON weight provider round-trip, and structured `Decision<T>`
rationale.

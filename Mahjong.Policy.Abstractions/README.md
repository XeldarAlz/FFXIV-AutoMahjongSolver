# Mahjong.Policy.Abstractions

Contracts only — interfaces + value records, no implementation logic.
Consumed by `Mahjong.Policy` (concrete decision logic), `Mahjong.Replay`,
`Mahjong.Tuner`, and the Dalamud plugin. Engine does **not** depend on this
— policy decisions are an opt-in layer above the rule engine.

## Contract surface

| Category | Types |
|---|---|
| Decision types | `ActionChoice`, `ActionKind`, `Decision<T>`, `Reason`, `ScoredDiscard` |
| Top-level | `IPolicy` |
| Sub-policies (Phase 4 chain) | `IDiscardPolicy`, `ICallPolicy`, `IRiichiPolicy`, `IPushFoldPolicy`, `IPlacementPolicy` |
| Opponent modeling | `IOpponentModel` |
| RNG | `IRandomSource`, `SeededRandomSource`, `RandomSourceExtensions` |
| Weights | `IWeightProvider`, `DefaultWeightProvider`, `WeightBundle` + `Discard/Opponent/Placement` weight records |

## Why a separate abstractions project?

Plugin and Tuner depend on **contracts**, not implementations. This separation
means swapping a learned policy for the heuristic policy is a one-line DI
binding change.

## Consumers

Mahjong.Policy implements every interface here. The plugin's MEDI composition
root (`PluginServices.cs`) registers the implementations.

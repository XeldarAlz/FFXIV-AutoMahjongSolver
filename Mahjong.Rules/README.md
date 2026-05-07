# Mahjong.Rules

Pluggable rule definitions — `IRuleSet`, `IYakuRule[]`, `IScoringRule`,
`IDoraRule`, `IFuRule`. Two implementations: `RiichiRuleSet` (Tenhou replay)
and `DomanRuleSet` (live FFXIV plugin).

## Contract

- **Zero magic numbers in calling code.** Every yaku han value, scoring tier
  threshold, fu component, and dora cycle lives here as a named constant.
- **One file per yaku** — 38 of them, each ~30 LOC. Adding a new yaku is a
  single new `IYakuRule` implementation; adding a Doman-specific yaku slots
  into `DomanRuleSet`'s registry without touching `RiichiRuleSet`.
- **Declarative conflicts.** `Ryanpeikou.Conflicts = [Iipeiko]`,
  `Junchan.Conflicts = [Chanta]`, `Chinitsu.Conflicts = [Honitsu]`. The
  Engine's `Scorer` applies them post-detection.

## Consumers

Engine (`Scorer` orchestrates), Replay (`RiichiRuleSet` for Tenhou logs),
Plugin.Dalamud (`DomanRuleSet` for live play).

## Extending

To add a new yaku:
1. Drop a new `IYakuRule` implementation under `YakuRules/` (or `YakuRules/Yakuman/`).
2. Register it in `RiichiRuleSet.YakuRules` and/or `DomanRuleSet.YakuRules`.
3. Add a focused unit test in `tests/Mahjong.Rules.Tests/`.

To tune scoring values: edit `Constants.cs`. Every threshold has a name and
a comment explaining what it affects.

## Tests

`tests/Mahjong.Rules.Tests/` — yaku-rule unit tests, scoring-tier boundaries,
dora cycles, conflict declarations, ruleset structural invariants.

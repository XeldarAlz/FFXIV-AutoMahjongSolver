# Engine (Mahjong.Engine)

Pure mahjong logic — hand decomposition, shanten counting, ukeire enumeration,
call-candidate derivation, top-level scoring orchestration. No I/O, no
Dalamud, no third-party packages.

## Public surface

| Class | Purpose |
|---|---|
| `HandDecomposer` | Enumerate all valid 14-tile decompositions (standard / chiitoitsu / kokushi) |
| `ShantenCalculator` | Distance to tenpai for standard / chiitoitsu / kokushi shapes |
| `UkeireEnumerator` | For each legal discard, compute accepting tiles + counts |
| `CallCandidateDeriver` | Given hand + claimed tile, derive valid pon / chi / kan groupings |
| `Scorer` | Top-level scoring — given `IRuleSet`, returns the best-paying `ScoreResult?` |
| `LegalActionGenerator` | Compute legal action flags from a state |

## Scoring pipeline

`Scorer` is the orchestrator (Phase 2 collapsed `ScoreEvaluator + YakuDetector
+ FuCalculator + ScoreCalculator` into this single class):

```
Hand + WinContext + IRuleSet
            │
            ▼
   HandDecomposer.Enumerate
            │
            ▼          (per decomposition)
   IRuleSet.YakuRules.Detect
            │
            ▼
   Yakuman shortcircuit + Conflict resolution
            │
            ▼
   IRuleSet.FuRule.Compute
            │
            ▼
   IRuleSet.ScoringRule.ResolveTier → Pay
            │
            ▼
        ScoreResult
```

Returns the best-paying decomposition across all valid forms.

## Consumers

Policy (every decision needs shanten/ukeire), Replay (golden-file scoring
verification), Plugin (live game-state evaluation).

## Tests

`tests/Engine.Tests/` — 113 tests covering decomposition, shanten, ukeire,
fu, scoring, yaku detection, dora-cycle behavior. Tests use a shared
`TestRules.RuleSet = new RiichiRuleSet()` fixture.

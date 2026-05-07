# Mahjong.Replay

Tenhou log parser + per-seat replay engine + golden-file regression harness.

## Contract

| Class | Purpose |
|---|---|
| `TenhouLog` | Parse Tenhou 6-format JSON; map 136-IDs to 34-IDs; extract starting state + draws + discards + events |
| `TenhouReplay` | Replay one seat's turns turn-by-turn through an `IPolicy`, comparing recorded vs. policy-picked discards |
| `ReplaySnapshot` + `ReplayDecisionEntry` | JSON-serializable trace for golden-file storage |
| `GoldenFileReplayHarness` | One-line `VerifyOrUpdate(tenhou.json, snapshot.json, policy)` for the regression suite |
| `RepoPathResolver` | Walk up from test assembly dir to find repo root via the `.sln` marker |

## Riichi rules, not Doman

Tenhou games are played under standard Japanese riichi rules. Replaying a
Tenhou log under `DomanRuleSet` would silently corrupt accuracy — for
instance, a Doman 2-han minimum (per `docs/ruleset.md` Q1) would reject
Tenhou 1-han wins as no-yaku and tuner deltas would chase noise. The
harness wires `RiichiRuleSet` automatically.

## Golden-file regression flow

```
data/replays/synthetic-east-1.tenhou.json   ──┐
data/replays/synthetic-east-1.snapshot.json ──┤   GoldenFileReplayHarness
                                                │   .VerifyOrUpdate
data/replays/synthetic-east-2.tenhou.json   ──┤
data/replays/synthetic-east-2.snapshot.json ──┘
                                                │
                              UPDATE_REPLAY_SNAPSHOTS=1?
                              ├─ yes → write golden, status=Updated/Created
                              └─ no  → diff golden, status=Match/Mismatch
```

Adding a new fixture: drop `<name>.tenhou.json` in `data/replays/`, run the
suite once with `UPDATE_REPLAY_SNAPSHOTS=1` to generate the snapshot, commit
both files. See [`data/replays/README.md`](../data/replays/README.md) for
the full workflow.

## Consumers

`tests/Mahjong.Replay.Tests/` (regression suite), `Tuner` (replay-based
weight evaluation owed — currently uses self-play only).

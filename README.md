# Auto Mahjong Solver

A Dalamud plugin that reads FFXIV Doman Mahjong, computes the mathematically best discard and call, shows a suggestion overlay, and (optionally, behind a gate) plays for you.

Command: `/amjs` (alias `/mahjong`).

## Status

The solver engine, board reading, and the autonomous play loop are live-verified on casual tables
(multi-hand sessions without stalls). Riichi declaration and the defense layer are implemented and
await their first observed in-game trigger.

| Capability | State |
|---|---|
| Solver engine (shanten, ukeire) | Implemented, unit-tested |
| Yaku / Doman scoring | Implemented, unit-tested; han→points table pending live verification |
| `Emj` hand reader (closed hand, aka) | **Verified live** (incl. under popups) |
| `Emj` meld reader (chii/pon/kan, feeds meldCount) | **Verified live** — meld strips mapped (self=112 family); post-call discard state 28 handled |
| `Emj` addon reader (rivers, dora, winds, scores, riichi badges) | Implemented; opponent river↔panel pairing pending live verification (`/amjs state`) |
| Seen-tile ukeire + furiten avoidance + fold vs riichi | Implemented; fold path awaits first observed opponent riichi |
| Auto-riichi (declare on live closed tenpai, then tsumogiri) | **Verified live** |
| Auto-play loop (discard, win, calls, redeal prompt, result screens) | **Verified live** — multi-hand sessions; popup fires retry-verified; settle grace + stall watchdog |
| Nine-terminals redeal prompt (keep on kokushi shanten ≤ 2) | Implemented; option label match pending first observation |
| Value-aware discard EV (exact points EV at tenpai; dora/aka/yakuhai retention above) | Implemented, unit-tested |
| Shared turn policy (engine `TurnPolicy`, used by bot and simulator) | Implemented |
| Self-play simulator (`tools/AutoMahjongSolver.Simulator`) | Implemented; baseline below |
| Calling strategy v1 (pon: yakuhai / secured-yaku / dora-tanyao; chi+kan never) | Implemented, unit-tested, simulated; pending live verification |
| Suggestion overlay | Planned |
| Chi / kan calls | Planned (chi variant-selection UI unverified; kan flips unreadable dora) |

## Layout

- `AutoMahjongSolver.Engine` — pure C# (`net10.0`), no Dalamud dependency. Tiles, shanten, ukeire, and (later) yaku/scoring. Fully unit-testable offline.
- `tests/AutoMahjongSolver.Engine.Tests` — xUnit tests for the engine.
- `AutoMahjongSolver` — the Dalamud plugin (planned): `Emj` reader, suggestion overlay, gated actuation. Built on ECommons.

## Build

```
dotnet test                                                   # engine + tests (no game required)
dotnet run --project tools/AutoMahjongSolver.Simulator -c Release -- 200 7   # self-play: matches, seed
```

Self-play baseline (200 matches, seed 7, all four seats on the current policy), with pon calls
(2026-06-12): 1125 hands, ~17.5% win rate per seat-hand, ~9.7% deal-in, ~30.5% riichi, 30.1%
exhaustive draws, average win 5990 points / 3.31 han. The closed-hands-only run before calls was
38.7% draws / 15.3% wins / 7.2% deal-in / avg win 7302 — pon trades win size for completion rate.
Policy changes should be judged against these numbers, same seed.

The Dalamud plugin project builds against `Dalamud.NET.Sdk` and requires a Dalamud dev environment.

## Notes

Doman Mahjong is standard 4-player Japanese riichi with FFXIV-specific scoring: a flat han-to-points table with **no fu calculation**. The engine targets this ruleset.

Auto-play of a ranked, human-PvP minigame carries account risk. Auto-play is off by default, gated behind an explicit opt-in, and disabled in ranked rooms.

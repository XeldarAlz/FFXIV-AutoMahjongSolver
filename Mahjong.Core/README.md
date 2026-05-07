# Mahjong.Core

Pure value-types for the mahjong domain — `Tile`, `Wind`, `Seat`, `Meld`,
`Hand`, `Decomposition`, `Wall`, `WinContext`, `LegalActions`, `ScoreResult`,
`StateSnapshot`, `Yaku`. The vocabulary every other project speaks.

## Contract

- **Zero external dependencies.** Pure C#; no I/O, no Dalamud, no third-party
  packages.
- **Defensive copies at construction.** Every record that takes a list-typed
  input copies it via `[.. input]` so callers can mutate their buffers
  freely without corrupting the snapshot.
- **`Wall` is the deliberate exception** — a mutable seen-counter, called out
  in its summary.

## Consumers

Engine (decomposition + shanten + ukeire), Rules (yaku + scoring), Policy
(decision making), Replay (Tenhou log replay), Plugin.Game (snapshots).

## Tests

`tests/Mahjong.Core.Tests/` — value-type semantics, defensive-copy contract
pinned for every list-bearing record.

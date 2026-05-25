# Roadmap

The end goal is full intelligent automation across **all clients** (EU, NA, JP, OC) — addon detection, tile reading, hint overlay, auto-discard, and full call acceptance (Pon / Chi / Kan / Riichi / Tsumo / Ron) at parity on every variant.

## Shipped

- Multi-variant addon resolution (`Emj` + `EmjL`) with auto-detect on load
- Per-variant tile encoding (texture base + akadora 5m/5p/5s flip) handled for both
- Hand / score / discard-count readout from addon memory
- Hints mode with reasoning + top-3 alternatives
- Auto-discard via the two-callback handshake `[15, textureId]` + `[7, slotIndex]` (corpus-verified 2026-05-23, the cause of the prior 0 % hand-end rate)
- Call-prompt acceptance via `FireCallback` opcode 11 with per-action accept-button index computation
- Tsumo via the dedicated agari opcode 9 (corpus-confirmed, 14 installs)
- Ron / AnKan / ShouMinKan / Riichi declaration all routed through the call-prompt button-row path (opcode 11) — uniform with Pon/Chi, eliminates the speculative-opcode failure mode that bricked the game into the DRAW screen (issue #39)
- Riichi tsumogiri honors the policy's chosen discard (latched alongside the riichi-confirm flag) rather than the last-drawn tile
- Akadora-aware scoring: red 5s in closed hand and open melds contribute to dora count
- MeldTracker chi/pon/minkan inference from closed-hand deltas + opp-discard increments, with a 30-tick deferred-baseline retry to ride out memory-write races
- Kan-aware TsumogiriFallback (each meld contributes its actual `TileCount`)
- Self-AnKan tracked via `MeldTracker.Record` so suggestions don't pause for the rest of the hand
- AutoPlayLoop FSM duplicate-fire guard (animation-gap `legal=None` ticks no longer clear the retry-cooldown context)
- GameLogger writes hand-end events into the new hand-file so they survive concurrent telemetry uploads
- EfficiencyPolicy defensive guard catches DiscardScorer invariant exceptions on shanten-invalid mid-transition states
- Per-dispatch chat-log annotation (`schedState`/`curState`/`path`) so regressions are one log line away
- Engine: shanten · ukeire · yaku · fu · scoring (116 tests)
- Policy: efficiency · Bayesian opponent model · evolutionary weight tuner · Tenhou log parser (86 tests)

## In progress

- NA / `EmjL` parity re-verification against v0.1.0.11 — the chi-accept fixes should apply identically to both variants but no NA tester has confirmed since the rework ([#30](https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues/30))
- `OurSeat` / `OurRiichi` / `DealerSeat` addon offsets — cross-install scan of the `agent_b64` v=2 memdumps produced candidate offsets at `+0x009e`/`+0x00a6`/`+0x00ae`; needs in-game validation against a controlled multi-hand session
- Two stuck-state RE captures: post-MinKan transient and post-2nd-consecutive-pon (see [`dispatch-protocol.md`](dispatch-protocol.md))

## Planned

- Plugin-layer replay-harness fixture corpus — every captured `FireCallback` frame becomes a replayable scenario in CI ([#38](https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues/38))
- JP / OC client verification (variant dumps + capture logs needed)
- Multi-chi variant selection — currently always picks the leftmost button
- Opponent open-meld struct decode from `agent_b64` for the opponent danger model
- Per-discard tedashi vs. tsumogiri bit (currently all opp discards are treated as tedashi)
- Tuner re-run against novice-table corpus once collected — current weights are Tenhou-trained against expert opponents

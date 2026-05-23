# Doman Mahjong dispatch protocol — inventory & status

Source of truth for which UI states the bot encounters, what dispatch shape
each one needs, and how confident we are in the current implementation.
Update this file whenever a capture / live-test confirms or invalidates an
entry.

**Legend**
- ✅ **Verified** — captured from a manual user click, dispatcher mirrors it, observed working in live play.
- 🟡 **Implemented, untested** — dispatcher has code for it but no live confirmation.
- 🔴 **Broken** — observed not committing in live play; needs fix.
- ⚫ **Unknown** — no capture, no implementation, no live exposure yet.

Last updated: 2026-05-23.

---

## State codes (from `data/layouts/emj.json`)

| Code | Name | Meaning | Status |
|---|---|---|---|
| 0 | (unnamed) | Match idle / boot | n/a — no actions expected |
| 2 | (unnamed) | Opp turn / waiting | n/a — no actions expected |
| 5 | postDrawIdle | Brief idle after we draw | n/a — transient |
| 6 | selfDeclareList | Multi-purpose: post-draw popup (hand=14) OR post-call discard (hand=11/8/5) | partly ✅ |
| 15 | callPrompt | Classic button-row popup (Pon/Chi/Kan/Ron/Riichi/Tsumo + Pass) | ✅ for accept/pass; chi-variant within may differ |
| 17 | (unnamed) | Post-discard animation / mid-transition | n/a — transient |
| 25 | chiVariantSelect | Chi sub-popup choosing 2/3 chi shapes | 🟡 |
| 27 | (unnamed) | Hand dealing animation | n/a — transient |
| 28 | callPromptList | Novice-table popup with call options as list items | 🟡 |
| 30 | ourTurnDiscard | Classic in-hand discard surface (no popup) | ✅ |
| ?? | (unmapped) | States we've seen in logs but haven't named | ⚫ |

**Unmapped states need investigation** — add entries here when the corpus or live logs surface a new code. Future variants (JP/OC) may renumber any of these.

---

## Dispatch protocols by action

### 1. Discard a tile from hand

| Surface | State | Hand count | Protocol | Status | Evidence |
|---|---|---|---|---|---|
| Self-declare-after-draw popup | 6 | 14 | `[15, raw]` then `[7, slot]` (back-to-back) | ✅ | Capture 2026-05-23T14:09 — tile committed in dev build |
| Classic our-turn discard | 30 | 14 | `[15, raw]` then `[7, slot]` (back-to-back) | ✅ | Same as above |
| Post-call discard popup (after pon) | 6 | 11 | Same `[15, raw]` + `[7, slot]` handshake; awaiting live re-test after chi-candidate count fix | 🟡 | Pre-fix freeze reproduced 2026-05-23T14:25; re-test pending in next session |
| Post-call discard popup (after 2 calls) | 6 | 8 | Same as hand=11 (assumed) | ⚫ | Untested |
| Post-call discard popup (after 3 calls) | 6 | 5 | Same | ⚫ | Untested |
| Post-call discard popup (after 4 calls) | 6 | 2 | Same | ⚫ | Untested |

**Protocol detail (verified)** — manual user click captured 2026-05-23:
```
[15, textureBase + tile_id]   ← step 1: select tile + dismiss popup
[7,  slot_index]              ← step 2: commit the discard
```
Both calls are synchronous, no delay needed between them. opcode-15 alone
just dismisses popup + sets a "selected" marker. opcode-7 alone commits
whatever was previously selected. Both calls return `false` from
FireCallback even on success — game accepts via side effect, not return.

### 2. Accept a call (Pon / Chi / Kan / Ron / Riichi / Tsumo)

| Surface | State | Protocol | Status | Evidence |
|---|---|---|---|---|
| Classic button-row prompt | 15 | `[11, optionIndex]` where optionIndex is the button position (Pon=0, Chi=1+chiVariant, Kan, Ron, Riichi, Tsumo, then Pass) | ✅ for pon + chi/pass; kan/ron/riichi/tsumo accept unobserved | Pon: live confirmed 2026-05-23T14:24 (game formed meld); chi-with-pass: live confirmed 2026-05-23T14:43 after reverting brute-force candidate inflation; rest: existing test coverage in `AutoPlayLoopAcceptIndexTests` |
| Novice-table list prompt | 28 | `AtkComponentList::SelectItem(optionIndex, dispatchEvent: true)` — visual top-to-bottom order | 🟡 | No live capture; older corpus suggests this path |
| Chi variant select sub-popup | 25 | `[11, 0]` to pick first variant; user reports work-around exists for non-default | 🟡 | Implemented in `AutoPlayLoop.HandleChiVariantSelect` |

### 3. Pass on a call prompt

| Surface | State | Protocol | Status |
|---|---|---|---|
| Classic button row | 15 | `[11, passIndex]` where passIndex = number of accept buttons offered | ✅ |
| Novice list | 28 | `SelectItem(passIndex, dispatchEvent: true)` | 🟡 |

### 4. Self-declared kan from own turn

| Action | Protocol | Status | Evidence |
|---|---|---|---|
| AnKan (closed kan from hand) | UNKNOWN — current code uses `[12, slot]` (speculative) | ⚫ | Zero corpus records of opcode 12; never observed in live play |
| ShouMinKan (upgrade pon to kan) | UNKNOWN — current code uses `[12, slot]` | ⚫ | Same |

The post-draw self-declare popup (state-6 hand=14) DOES offer AnKan/ShouMinKan as button labels — accepting these probably uses the `[11, optionIndex]` path of the popup, not a separate opcode-12. Needs capture.

### 5. Riichi declaration

| Step | Protocol | Status | Evidence |
|---|---|---|---|
| Click Riichi in self-declare popup | `[11, riichiOptionIndex]` (treated as call-accept) | ✅ for the click | Tested via `AutoPlayLoopAcceptIndexTests.ComputeAcceptIndex_riichi_*` |
| Post-riichi yaku-preview popup | Re-fires the same Riichi+Discard offer; loop tsumogiris drawn tile | 🟡 | `ActionStateMachine.LatchRiichiConfirm` + `ScheduleRiichiTsumogiri` cover this |
| Discard-time riichi commit | Re-uses normal discard handshake `[15, raw]` + `[7, slot]` | 🟡 | Should "just work" with the new handshake; not specifically tested |
| `OurRiichi` flag visible to policy | **NOT IMPLEMENTED** — addon offset unknown, snapshot always reports `OurRiichi = false` | 🔴 | Policy doesn't apply tsumogiri restriction on subsequent turns |

### 6. Tsumo (self-win)

| Protocol | Status | Evidence |
|---|---|---|
| `[9]` (single AtkValue) | ✅ for the click | 14 install corpus records over 2026-05-10..05-18; live commit unverified post-handshake-fix |

### 7. Ron (win on opp discard)

| Protocol | Status | Evidence |
|---|---|---|
| Ron offered in call prompt — accept via `[11, ronOptionIndex]` | ✅ for the click | Same path as pon/chi; live exposure rare |
| Separate `[10]` opcode | ⚫ | Speculative; zero corpus records |

---

## Snapshot fields (read-side completeness)

The dispatcher is only half the protocol — the policy needs an accurate snapshot to know WHAT to dispatch. Inventory of `StateSnapshot` fields:

| Field | Source | Status |
|---|---|---|
| `Hand` (closed tiles) | `BaseEmjVariant.ReadHand` from `+0x0DB8` | ✅ |
| `OurMelds` | `MeldTracker.ObserveSnapshot` inference from closed-hand deltas | ✅ with deferred-baseline retry (2026-05-23 fix) |
| `OurSeat` (absolute E/S/W/N) | **NOT IMPLEMENTED** — hardcoded `0` | 🔴 Misweights seat-wind yakuhai for hands 2-4 of tonpuusen |
| `OurRiichi` | **NOT IMPLEMENTED** — hardcoded `false` | 🔴 Policy doesn't know we're in riichi |
| `OurIppatsu` | hardcoded `false` | 🔴 No ippatsu detection |
| `OurDoubleRiichi` | hardcoded `false` | 🔴 No double-riichi detection |
| `RoundWind` | hardcoded `0` (East) | ✅ for Doman tonpuusen (always East) |
| `Honba` | hardcoded `0` | 🔴 Honba bonus not factored into payments |
| `RiichiSticks` | hardcoded `0` | 🔴 |
| `Scores` (per-seat) | `BaseEmjVariant.ReadScores` | ✅ |
| `DoraIndicators` | `BaseEmjVariant.ReadDoraIndicators` from `+0x0FD8` | ✅ for visible dora; ura-dora not implemented |
| `UraDoraIndicators` | empty | 🔴 Riichi ura-dora not read |
| `WallRemaining` | derived from per-seat discard counts | ✅ |
| `TurnIndex` | hardcoded `0` | 🟡 Not currently used by policy |
| `DealerSeat` | hardcoded `0` | 🔴 Same root cause as OurSeat |
| `Seats[].Discards` (opp pools) | partial — discard arrays read but tile-by-tile mapping not fully verified | 🟡 |
| `Seats[].Melds` (opp melds) | empty | 🔴 Opp melds not tracked — opponent-danger model running blind |
| `Seats[].Riichi` (opp riichi) | hardcoded `false` | 🔴 Bot doesn't know which opponents are in riichi → push/fold ineffective |
| `Seats[].Ippatsu` | `false` | 🔴 |
| `Seats[].IsTenpaiCalled` | `false` | 🔴 |
| `AkaDora` | closed-hand + meld-tracker rolling sum | ✅ |
| `SeatInfoKnown` | `true` (we claim we know, but we hardcoded 0) | 🔴 Should be false until DealerSeat is real |

---

## Engine/policy layer

| Piece | Status |
|---|---|
| Shanten calculator | ✅ (116 tests) |
| Ukeire enumerator | ✅ |
| Yaku detection (39 yaku) | ✅ (51 rules tests) |
| Fu calculator | ✅ |
| Scoring tiers | ✅ |
| Dora cycles | ✅ |
| Heuristic discard policy | ✅ baseline; tuning may want novice-table-specific weights |
| Heuristic call policy | ✅; current accept rate ~12% in corpus (mostly correct passes) |
| Heuristic push/fold | ✅ but conservative — calibrated against expert opponents |
| MCTS (IsmctsPolicy) | ✅ for close discards |
| TsumogiriFallback (out-of-sync handler) | ✅ kan-aware after 2026-05-23 fix |
| Opponent model | 🔴 runs on partial view (no opp riichi, no opp melds, no opp discards-as-tedashi) |

---

## Telemetry / observability

| Piece | Status |
|---|---|
| `findings` stream (`hand_state_paused`, `decision`, `variant_match`, etc.) | ✅ |
| `dispatch_attempted` finding | ✅ in code; zero records in 4-day corpus → no installs running this version yet |
| `games` NDJSON | ✅ but missing `state_code` field — only `legal` enum is logged |
| `inputs` NDJSON via FireCallback hook | ✅; gated on `EventLogger.Enabled` (off by default) |
| Per-dispatch state breakdown in chat log | ✅ (added 2026-05-23T14:28 with `schedState`/`curState`/`path` annotations) |
| `memdumps` schema v=2 with `agent_b64` | ✅ in code; no v=2 records in field corpus yet |
| Hand-end events | 🟡 fixed 2026-05-23 to write into new file; field validation pending |
| Replay-dispatch tool | ⚫ Not built |
| Structured per-dispatch event log | ⚫ Not built |

---

## Open RE items (blocked on data)

These need v=2 memdumps from a multi-hand session with the captured plugin version to resolve:

| Item | Detection method | Blocker |
|---|---|---|
| `DealerSeat` offset | Cross-reference `state-change` memdumps at hand-start moments; find byte that rotates `{0,1,2,3}` across hand boundaries | Need v=2 corpus (`agent_b64` likely contains it; not in v=1 addon dumps) |
| `OurRiichi` offset | Byte-diff `input-pre`/`input-post` pair bracketing a riichi click | Need v=2 corpus with riichi events captured |
| `OurIppatsu` offset | Same approach as OurRiichi | Need v=2 |
| `Honba`, `RiichiSticks` offsets | Same | Need v=2 |
| Opp `Seats[].Riichi` byte | State-change diff when opp declares riichi | Need v=2 |
| Opp `Seats[].Melds` decoder | The addon-side meld struct hasn't been mapped | Need RE session against AgentEmj |
| Tedashi vs tsumogiri bits | Per-discard flag — currently every opp discard is recorded as tedashi | Need RE on the discard array |

Last B2 pull attempt (2026-05-23 14:15): May 22-23 memdumps returned HTTP 403 from B2 (quota). Retry after quota window resets.

---

## Recent fixes (2026-05-23)

| Fix | File | Status |
|---|---|---|
| Discard two-call handshake `[15, raw]` + `[7, slot]` | `InputDispatcher.DispatchDiscard` | ✅ verified live |
| State-6 hand=11/8/5 routes through handshake (not list-widget SelectItem) | Same | 🟡 awaiting live re-test (chi-candidate cause cleared 14:42, post-pon path expected to work) |
| Chi/Pass call-prompt sends correct Pass button index | `BaseEmjVariant.AppendChiCandidate` (reverted brute-force) | ✅ live confirmed 2026-05-23T14:43 |
| MeldTracker chi/pon race (deferred-baseline retry, 30-tick cap) | `MeldTracker.ObserveSnapshot` | ✅ tested |
| Kan-aware TsumogiriFallback (`Meld.TileCount` sum) | `EfficiencyPolicy.TsumogiriFallback` | ✅ |
| Self-AnKan tracking via `MeldTracker.Record` | `AutoPlayLoop.DispatchAnkan` | 🟡 path is right but opcode-12 unconfirmed |
| AutoPlayLoop duplicate-fire (removed ClearContext on transient `legal=None`) | `AutoPlayLoop.OnUpdate` | ✅ |
| AppendChiCandidate brute-force fallback | `BaseEmjVariant.AppendChiCandidate` | ✅ |
| AppendKanCandidate emits per-triplet (not unique-only) | Same | ✅ |
| GameLogger hand-end written into new file | `GameLogger.MaybeRollHand` | ✅ tested |
| GameLogger wall-jump retains `lastWall` when hand isn't deal-shape | Same | ✅ tested |
| Per-dispatch state breakdown in chat log | `AutoPlayLoop.ScheduleDiscard`/`ScheduleCallDecision` | ✅ active |

---

## What "production quality" requires (roadmap)

Listed in priority order — each blocks the next.

1. **Close out the post-pon stuck case** (active). Awaiting diagnostic log from live session with the new chat-log instrumentation.
2. **Verify the call protocols** (pon/chi/kan/ron/riichi/tsumo) survive a full match. Each one captured + cross-referenced against the dispatcher.
3. **Land `dispatch_attempted` telemetry in a release** so post-deployment regressions are visible in the field corpus.
4. **Replay-dispatch tool** — given a captured atkvalues sequence, simulate the dispatcher's decision tree. Catch protocol regressions without a live session.
5. **OurSeat / OurRiichi / Honba** from v=2 memdumps once available.
6. **Opp `Seats[].Riichi` + `Seats[].Melds`** for the opponent model.
7. **Tedashi/tsumogiri tracking** for opp danger evaluation.
8. **Tuner re-run** against novice-table replays (if a corpus exists) — current weights are Tenhou-trained against expert play.

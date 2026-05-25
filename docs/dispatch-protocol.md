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

Last updated: 2026-05-24.

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
| Post-call discard popup (after pon) | 6 | 11 | Same `[15, raw]` + `[7, slot]` handshake | ✅ | Live-confirmed 2026-05-23T15:13 after chi-candidate count fix |
| Post-MinKan transient (rinshan pending) | 6 | 10 | UNKNOWN — closed shows 10 with `melds=1 (MinKan)`, shanten total = 10 + 3·1 = 13 ≠ 14. Either the rinshan replacement tile hasn't propagated to the hand-array yet, or the addon expects a different action here. | 🔴 | Live freeze 2026-05-23T15:29..32: 50+ DiscardScorer exceptions in 3 min because variant emitted Discard but shanten math invalid. **Action required**: capture a manual MinKan + rinshan + discard via `/mjauto capture minkantest` to identify the expected dispatch. Defensive guard 2026-05-23T15:35 catches the exception and returns Pass cleanly so the bot stops spamming; user has to manually click to unblock until the protocol is known. |
| Post-2nd-pon stuck (consecutive pon offers within ~1 sec) | 6 | 10 melds=1 | UNKNOWN — 2nd pon dispatched [11, 0], game state cycled 15→22→9→6 but the meld never registered in our snapshot. Two indistinguishable possibilities: (a) game refused the 2nd pon and dismissed popup, OR (b) game accepted but addon hand-array kept stale entries while MeldTracker missed the meld. Reproduced 2026-05-23T15:13 and again 2026-05-23T15:42. | 🔴 | **Action required**: when next stuck, run `/mjauto capture pon2test`, manually click a tile, paste capture log content. The captured FireCallback shape disambiguates (a) vs (b). |
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
| AnKan (closed kan from hand) | `[11, ankanOptionIndex]` via call-prompt button row | 🟡 | Routed via `DispatchCallOption(ComputeAcceptIndex(AnKan, ...))` since v0.1.1.1; same path family as Pon/Chi which are corpus-confirmed. Live verification pending. The pre-fix `[12, slot]` path is removed. |
| ShouMinKan (upgrade pon to kan) | `[11, shouminkanOptionIndex]` via call-prompt button row | 🟡 | Same as AnKan — routed via the button-row path; index computation in `AutoPlayLoop.ComputeAcceptIndex`. Live verification pending. |

The post-draw self-declare popup (state-6 hand=14) offers AnKan/ShouMinKan as button labels, so the button-row click path matches what a manual user click would do. Button-order assumption: Pon → Chi → AnKan → MinKan → ShouMinKan → Ron → Riichi → Tsumo → Pass. AnKan/MinKan/ShouMinKan are grouped because the addon button row keeps kan variants adjacent.

### 5. Riichi declaration

| Step | Protocol | Status | Evidence |
|---|---|---|---|
| Riichi/Pass popup — policy declines | Closed hand stays clickable; bot must fall through to discard handshake instead of pressing `[11, passIdx]` | 🟡 awaiting live re-test in next session | Live freeze 2026-05-23T15:03: `auto-pass[opt=1] → Ok` looped indefinitely because variant didn't emit `Discard` flag at state-6 hand=14 popup. Fixed in BaseEmjVariant + AutoPlayLoop dispatch-routing 2026-05-23T15:08. |
| Riichi/Pass popup — policy accepts | `[11, riichiOptionIndex]` then `LatchRiichiConfirm(chosenTile)` then next-tick tsumogiri on the latched tile. | 🟡 unverified live | Pre-fix used the dead opcode-8 path; corrected 2026-05-23T15:08. Tile-latching added 2026-05-24 — previously tsumogiri'd slot 13 (last drawn) regardless of the policy's choice, which would chombo if the wait IS the drawn tile. |
| Post-riichi yaku-preview popup | Re-fires the same Riichi+Discard offer; loop tsumogiris the latched tile (or slot 13 if no tile was latched) | 🟡 | `ActionStateMachine.LatchRiichiConfirm` + `ScheduleRiichiTsumogiri` cover this; subsequent latches with `null` preserve the originally-chosen tile |
| `OurRiichi` flag visible to policy | **NOT IMPLEMENTED** — addon offset unknown, snapshot always reports `OurRiichi = false` | 🔴 | Policy doesn't apply tsumogiri restriction on subsequent turns |

### 6. Tsumo (self-win)

| Protocol | Status | Evidence |
|---|---|---|
| `[9]` (single AtkValue) | 🟡 dispatcher path now wires correctly | 14 install corpus records over 2026-05-10..05-18; pre-fix v0.1.0.10 routed Tsumo through opcode-11 (button row) and never committed — live freeze 2026-05-23T15:37 on a Tsumo,Pass popup; routing fixed 2026-05-23T15:40 awaiting live verification |

### 7. Ron (win on opp discard)

| Protocol | Status | Evidence |
|---|---|---|
| Ron offered in call prompt — accept via `[11, ronOptionIndex]` | ✅ | Same path as pon/chi; live-confirmed working in suggestion mode and from manual clicks (issue #39 log shows the user clicking Ron themselves committed cleanly). Now also the auto-play path since v0.1.1.1. |
| Separate `[10]` opcode | ⛔ removed | v0.1.0.11..v0.1.1.0 shipped this as a speculative dispatch by analogy to Tsumo opcode-9; field bug #39 (2026-05-24) showed `FireCallback(1, [Int=10]) → false (HookFailed)` followed by the game corrupting into the DRAW screen with no legal recovery. Removed in v0.1.1.1. |

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

### Agent_b64 scan results (2026-05-23, 9 v=2 installs sampled)

Cross-install scan against agent_b64 (8192 bytes) found per-install variation at multiple offsets in {0..3} value range. Top byte candidates:

| Offset | Variation pattern across 9 installs | Notes |
|---|---|---|
| **+0x009e** | 0,0,2,3,3,3,0,0,2 | Covers {0,2,3}. Cluster with +0x00a6, +0x00ae — likely same struct. |
| **+0x00a6** | 0,0,2,3,0,3,0,0,2 | Same cluster as +0x009e. |
| **+0x00ae** | 0,0,2,3,0,3,0,0,3 | Same cluster. |
| +0x0866 | 0,0,0,2,0,0,0,1,2 | Covers {0,1,2}. |
| +0x0c0d | 2,2,1,2,1,2,2,0,2 | Covers {0,1,2}. |
| +0x0cb8 | 0,1,0,0,0,0,2,0,1 | Covers {0,1,2}. |

None showed all four values {0,1,2,3} — sample size of 9 installs is too small for clean rotation. The +0x009e/+0x00a6/+0x00ae triplet is the strongest candidate for some per-seat field (consecutive byte fields with correlated per-install values). Validation requires either:
- Multi-hand within-install rotation pattern (blocked: corpus pre-v0.1.0.10 has many spurious hand-rolls polluting the boundary signal)
- Live test: patch variant profile to read from +0x009e and observe if it matches the dealer seat in a controlled session

Last B2 pull attempt (2026-05-23 15:00): May 22-23 memdumps still returned HTTP 403 (24h quota window not reset yet). May 20-21 v=2 corpus already analyzed — sufficient for cross-install but not for within-install boundary detection until the pre-v0.1.0.10 wall-jump pollution clears from future corpora.

---

## Recent fixes (2026-05-24) — issue #39 architectural cleanup

Triggered by field report [#39](https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues/39): auto-play declared Ron, addon rejected the dispatch, game corrupted into state-29 (DRAW screen) with no recovery. Log captured the exact failure mode:

```
21:03:39.185  call body: state=15 flags=Ron, Pass choice=Ron
21:03:39.187  auto-ron → HookFailed              ← FireCallback(1, [Int=10]) returned false
21:03:41.419  curState=29 flags=None             ← addon transitioned to DRAW
21:03:51.455  STUCK at state=29 ... for 10s. Manual click required.
```

Root cause: the v0.1.0.11 release added dedicated agari opcodes for Tsumo (9, corpus-confirmed) AND Ron (10, **speculative** — zero corpus records). The Ron path shipped on analogy without evidence and the live addon rejects opcode-10 outright. Same speculative-opcode bug class was latent in AnKan/ShouMinKan (opcode 12) and the dead Riichi opcode-8.

**Architectural fix** — kill the speculative opcodes entirely. The only dedicated callback retained is opcode-9 (Tsumo). Everything else (Ron, AnKan, ShouMinKan, Riichi declaration) now flows through the corpus-confirmed call-prompt button-row path (opcode 11), the same path that already works for Pon/Chi/Pass.

| Fix | File | Status |
|---|---|---|
| Removed `DispatchRon`, `DispatchRiichi`, `DispatchKan` and the speculative `Opcode.Ron/Riichi/Kan` constants | `InputDispatcher.cs` | ✅ |
| Ron at state-15 button row routes through `DispatchCallOption(ComputeAcceptIndex(Ron, …))` — same opcode-11 path that handles Pon/Chi | `AutoPlayLoop.DispatchAccept` | 🟢 high confidence — issue-#39 reporter confirmed manual Ron click commits cleanly (which uses this same `[11, idx]` shape via the addon's UI handler); failure mode of the new path is strictly better than the old one (worst case is a no-op rather than addon corruption); live auto-play verification pending |
| AnKan/ShouMinKan at state-6 self-declare popup route through opcode-11 button row (button-order assumption: …Chi → AnKan → MinKan → ShouMinKan → Ron → Riichi → Tsumo…) | `AutoPlayLoop.DispatchAnkan` + `ComputeAcceptIndex` | 🟡 button-order is best-guess inference; if the actual order differs the worst case is the wrong button clicked (no-op or unrelated action), NOT addon corruption. Live capture needed to pin the exact order. |
| Riichi tsumogiri now honors the policy's chosen discard (latched alongside `LatchRiichiConfirm`) instead of always firing slot 13 (last drawn). Prevents chombo when the wait IS the drawn tile and a different held tile must be discarded to declare. | `AutoPlayLoop.ScheduleRiichiTsumogiri` + `ActionStateMachine.LatchRiichiConfirm` | ✅ tile-latch verified by unit tests; live verification pending |
| Regression coverage: Ron with Pon offered, AnKan ordering at self-declare popup, ShouMinKan with AnKan offered, Riichi index after AnKan | `tests/AutoPlayLoopAcceptIndexTests.cs` (+5) | ✅ |
| FSM tile-latch invariants: store on latch, preserve on null re-latch (post-confirm popup), clear on `ClearRiichiConfirm`, clear on hand-transition `ObserveWall` | `tests/ActionStateMachineTests.cs` (+4) | ✅ |

**Confidence level**: HIGH that the game-breaking bug is gone. The pre-fix code emitted a callback shape the addon explicitly rejects (`HookFailed`); the new code emits the corpus-confirmed shape that's already in active use for other actions. The remaining 🟡 entries are about whether downstream effects work optimally, not whether the addon-rejection-into-DRAW failure mode can recur — it can't.

What live verification still needs to confirm:
- Auto-play Ron at state-15 commits the winning hand (not just clicks the right button).
- AnKan button order at state-6 matches the assumed `…Chi → AnKan → MinKan → ShouMinKan…` layout.
- Riichi tile-latch fires the correct discard for non-tsumogiri Riichi cases (where `best.Discard != hand[13]`).

---

## Recent fixes (2026-05-23)

| Fix | File | Status |
|---|---|---|
| Discard two-call handshake `[15, raw]` + `[7, slot]` | `InputDispatcher.DispatchDiscard` | ✅ verified live |
| State-6 hand=11/8/5 routes through handshake (not list-widget SelectItem) | Same | 🟡 awaiting live re-test (chi-candidate cause cleared 14:42, post-pon path expected to work) |
| Chi/Pass call-prompt sends correct Pass button index | `BaseEmjVariant.AppendChiCandidate` (reverted brute-force) | ✅ live confirmed 2026-05-23T14:43 |
| Riichi/Pass popup at state-6 hand=14: variant now emits Discard flag too; dispatcher routes Riichi-decline through tile-click handshake and Riichi-accept through opcode-11 popup path (not dead opcode-8) | `BaseEmjVariant.BuildLegalActions` + `AutoPlayLoop.DispatchDiscardOrRiichi` | 🟡 awaiting live re-test 2026-05-23T15:08 |
| State-6 hand%3≠2 fallthrough no longer forces Discard (reverted 15:19 over-aggressive emit) | `BaseEmjVariant.BuildLegalActions` | ✅ shanten-invalid mid-transition states (post-MinKan-pre-rinshan, MeldTracker mid-race) now correctly return None instead of triggering DiscardScorer throws |
| EfficiencyPolicy defensive guard catches DiscardScorer ArgumentException, returns Pass cleanly | `EfficiencyPolicy.Choose` | ✅ defense-in-depth so any future variant-vs-scorer invariant mismatch can't crash dispatch in a 3-second retry loop |
| Tsumo at self-declare popup routes through dedicated opcode 9 (corpus-confirmed); Ron / AnKan / ShouMinKan / Riichi declaration routed through opcode-11 button row | `AutoPlayLoop.DispatchAccept`, `AutoPlayLoop.DispatchAnkan` | ✅ Ron path corrected 2026-05-24 after issue #39 — opcode-10 was rejected by the addon and bricked the game into DRAW; reverted to the corpus-confirmed button-row path |
| MeldTracker chi/pon race (deferred-baseline retry, 30-tick cap) | `MeldTracker.ObserveSnapshot` | ✅ tested |
| Kan-aware TsumogiriFallback (`Meld.TileCount` sum) | `EfficiencyPolicy.TsumogiriFallback` | ✅ |
| Self-AnKan tracking via `MeldTracker.Record` | `AutoPlayLoop.DispatchAnkan` | 🟡 meld-recording is correct; dispatch now uses opcode-11 button-row click (2026-05-24) instead of the removed speculative opcode-12 |
| AutoPlayLoop duplicate-fire (removed ClearContext on transient `legal=None`) | `AutoPlayLoop.OnUpdate` | ✅ |
| AppendChiCandidate brute-force fallback | `BaseEmjVariant.AppendChiCandidate` | ✅ |
| AppendKanCandidate emits per-triplet (not unique-only) | Same | ✅ |
| GameLogger hand-end written into new file | `GameLogger.MaybeRollHand` | ✅ tested |
| GameLogger wall-jump retains `lastWall` when hand isn't deal-shape | Same | ✅ tested |
| Per-dispatch state breakdown in chat log | `AutoPlayLoop.ScheduleDiscard`/`ScheduleCallDecision` | ✅ active |

---

## What "production quality" requires (roadmap)

Listed in priority order — each blocks the next.

1. **MinKan post-claim transient state** (🔴 BLOCKER). State-6 hand=10 melds=1 freezes the bot until the user manually unblocks. We don't yet know whether the rinshan replacement tile is supposed to land in the hand-array, or if the addon expects a different dispatch shape entirely. **Capture needed**: manual MinKan declaration + rinshan + first discard via `/mjauto capture minkantest`.
2. **Post-2nd-call MeldTracker miss** (🔴). When two consecutive pons happen quickly, MeldTracker can miss the second one (race condition past the 30-tick deferral cap). Need either: a longer deferral, an alternative meld-detection signal (e.g. AgentEmj meld bytes when those are RE'd), or graceful recovery from `closed + 3·melds.Count ≠ 14`.
3. **Verify remaining 🟡 call protocols** survive a full match. Each one captured + cross-referenced against the dispatcher: multi-chi variant select, Riichi accept full cycle, Tsumo, AnKan/ShouMinKan button order at state-6, state-28 novice list prompt. (Ron auto-play routing fixed 2026-05-24 — live commit verification still pending but failure mode is no longer game-breaking.)
4. **Land `dispatch_attempted` telemetry in v0.1.0.10** so post-deployment regressions are visible in the field corpus (already in code, awaiting field penetration as installs update).
5. **Replay-dispatch tool** — given a captured atkvalues sequence, simulate the dispatcher's decision tree. Catch protocol regressions without a live session.
6. **OurSeat / OurRiichi / Honba** from v=2 memdumps once available. Cross-install scan complete (candidates at +0x009e/+0x00a6/+0x00ae); needs in-game validation.
7. **Opp `Seats[].Riichi` + `Seats[].Melds`** for the opponent model. Major contributor to push/fold accuracy against novice tables.
8. **Tedashi/tsumogiri tracking** for opp danger evaluation.
9. **Tuner re-run** against novice-table replays (if a corpus exists) — current weights are Tenhou-trained against expert play, suspected reason for 0 wins observed in 5-hand session 2026-05-23T12:19..28.

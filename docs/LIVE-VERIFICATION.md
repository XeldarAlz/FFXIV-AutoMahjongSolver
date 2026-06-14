# Live verification checklist

Every value the auto-play loop depends on was reverse-engineered offline and is flagged
"re-verify live." This document maps each one to the exact `/amjs` command that confirms it and
the output you should expect. Work through it **in order** — reading is verified before actuation,
passive observation before active inputs.

## Ground rules

- **Casual tables only.** Never ranked. A mis-fired callback can wedge the addon, and unattended
  automation of a ranked PvP mode carries account risk.
- Open the Dalamud log first: `/xllog`. Filter to `[AMJS]` (that's `AmjsConstants.LogPrefix`).
  All command output lands there, not in the game chat.
- Have the plugin built in your dev Dalamud and loaded. `/amjs` toggles the window; the debug
  verbs below are the verification surface.
- Keep this file open and tick boxes / fill the "observed" column as you go. When something
  disagrees with the expected value, the constant to edit is named in the last column.

## Commands at a glance

| Command | Backed by | What it verifies |
|---|---|---|
| `/amjs values` | `EmjAddonDumper.DumpValues` | `AtkValues[0]` state code, `[7]` call label, option indices |
| `/amjs dump` | `EmjAddonDumper.Dump` | Hand-tile node IDs, button/image NodeList indices, tile decode |
| `/amjs tree` | `EmjAddonDumper.DumpTree` | Full node tree — discover correct IDs, find meld containers |
| `/amjs melds` | `EmjAddonDumper.DumpMelds` | Meld decode (only after `MeldGroupNodeIds` is populated) |
| `/amjs capture on` | `EmjCaptureLogger` | The FireCallback protocol, captured from **manual** play |
| `/amjs discard <slot>` | `EmjActuator.DiscardSlot` | Discard handshake actually drives the game |
| `/amjs advance` | `EmjActuator.Advance` | Result/new-hand advance opcodes |
| `/amjs auto on` | `AutoPlayController` | Full loop integration |

---

## Phase 1 — Addon + state codes (passive)

The loop is a state machine keyed on `AtkValues[0]`. Every other phase assumes these codes are
right, so pin them down first.

- [ ] Sit at a casual table. Run `/amjs values` and confirm it prints `AtkValues count=… state=…`
      (not "Emj addon not present"). This confirms the addon name `"Emj"`.
- [ ] Run `/amjs values` at each moment below and record the `state=` value:

| Moment | Expected `[0]` | Observed | Constant if wrong |
|---|---|---|---|
| Just after you draw (self-action surface) | `6` | | `EmjActuator.StateAfterDraw` |
| After a tile is highlighted, mid-discard | `30` | | `EmjActuator.StateDiscardTurn` |
| A call is offered to you (pon/chi/kan/ron) | `15` | | `AutoPlayController.DoCall` case |
| Agari / result modal | `29` | | `EmjActuator.Advance` (→ op 14) |
| End-of-hand transitional | `32` | | `EmjActuator.Advance` (→ op 17) |
| New-hand acknowledge | `2` | | `EmjActuator.Advance` (→ op 9) |

> Note the `6 → 30` shift: highlighting your own tile moves the surface from 6 to 30 on the same
> turn. The loop treats them as one turn (`AutoPlayController.cs` `sameTurn`). Confirm both appear
> within a single discard and that no other code sneaks in between.

---

## Phase 2 — Hand-tile nodes + NodeList indices (passive)

`/amjs dump` walks `HandTileNodeIds` + `DrawnTileNodeId`, and for each drills
component → button (`NodeList[0]`) → image (`NodeList[4]`) and prints the texture path it decodes.

- [ ] On your turn (state 6/30, 14 tiles visible) run `/amjs dump`.
- [ ] Every one of the 14 nodes prints `type=… visible=True` (not `<not found>`). The 13 closed
      slots are `HandTileNodeIds` (`134, 1340001…1340012`); the drawn tile is `DrawnTileNodeId` 135.
- [ ] Under each node, a line like `[button 4] type=… tex='ui/icon/076000/0760xx…' -> <tile>`
      appears, and `<tile>` matches the tile you actually hold in that position.
- [ ] The decoded 14 tiles, read left to right, match your on-screen hand (drawn tile last).

If a node is `<not found>` or the tile image is at a different `[button N]` index than `4`:

- [ ] Run `/amjs tree`, find the real hand-tile container IDs and the child index the `0760xx`
      image actually sits at, and update:
  - `AmjsConstants.HandTileNodeIds` / `DrawnTileNodeId` (the node IDs)
  - `AmjsConstants.HandButtonNodeListIndex` (button position, currently `0`)
  - `AmjsConstants.HandImageNodeListIndex` (image position, currently `4`)

---

## Phase 3 — Tile icon decode (passive)

The `0760xx` → tile-index map lives in `EmjTiles.TryFromIconId`. Confirm the ranges against known
tiles in the `dump`/`tree` output.

- [ ] `1m`→`076041`, `9m`→`076049`; `1p`→`076050`, `9p`→`076058`; `1s`→`076059`, `9s`→`076067`.
- [ ] Honors `1z..7z` (E,S,W,N,Haku,Hatsu,Chun) → `076068…076074`.
- [ ] A red five decodes with `(aka)`: `5m`→`076075`, `5p`→`076076`, `5s`→`076077`.
- [ ] No held tile prints a `tex=` path that fails to decode (would mean an icon outside the ranges
      or a path the regex `0760\d{2}(?=[_.])` misses).

Wrong mapping → fix `EmjTiles.TryFromIconId` ranges (or the regex in `EmjTiles.IconNumber`).

---

## Phase 4 — Call label + option indices (passive)

`AutoPlayController.DoCall` reads the offered-action string at `AtkValues[CallLabelIndex]` (`7`) and
accepts with `Call(0)` if it contains "Ron", else passes with `Call(1)`.

- [ ] When a call is offered (state 15) run `/amjs values`. Confirm `[0]=15`.
- [ ] Confirm index `[7]` is a `String` whose text names the offered action, and that a **winning**
      offer's label contains `Ron` (case-insensitive). Record the exact label text.
- [ ] Note which `String`/option indices correspond to accept vs pass — Phase 5 confirms the
      `[11, option]` numbers, but eyeball them here.

If the label is at a different index than `7`, update `AutoPlayController.CallLabelIndex`. If the
winning label isn't the substring "Ron" (localization, different wording), update the
`label.Contains("Ron", …)` test.

---

## Phase 5 — FireCallback protocol (passive, the critical one)

`/amjs capture` hooks `FireCallback` and logs the real arguments your **manual** inputs send. This
is the ground truth the actuator's hardcoded opcodes must match. The actuator claims:

- discard = `[15, iconId]` (highlight) then `[7, slot]` (commit) — `EmjActuator.Commit`
- call/self-action pick = `[11, option]` — `EmjActuator.Call`
- advance = single-int `[op]` with `op` from state (Phase 7)

Procedure:

- [ ] `/amjs capture on`. Play the hand manually.
- [ ] **Discard** a tile by hand → expect two log lines: `[15]…=<iconId>` then `[7]…=<slot>`.
      Confirm the icon matches the discarded tile and the slot matches its hand position.
      Confirm op `15` and `7` (= `OpHighlight`, `OpDiscardCommit`).
- [ ] **Pass** an offered call → record the `[11, option]` value (expected `1`).
- [ ] **Accept** a call (pon/chi/kan, and separately ron) → record the `[11, option]` (expected `0`
      for ron/tsumo accept). Capture also auto-dumps the popup `AtkValues` on op `11`/`12`, so the
      option→label map is in the log.
- [ ] **Tsumo** on a complete drawn hand → confirm the op matches `Call(0)` (`[11, 0]`).
- [ ] **Advance** each result screen manually → record the single-int op fired at states `29`, `32`,
      `2`.
- [ ] `/amjs capture off`.

Reconcile against the constants:

| Action | Code expects | Observed | Constant if wrong |
|---|---|---|---|
| Highlight | `[15, icon]` | | `EmjActuator.OpHighlight` |
| Discard commit | `[7, slot]` | | `EmjActuator.OpDiscardCommit` |
| Call/self pick | `[11, opt]` | | `EmjActuator.OpCallOption` |
| Accept (ron/tsumo) | `option 0` | | `AutoPlayController` `Call(0)` |
| Pass | `option 1` | | `AutoPlayController` `Call(1)` |
| Advance @29 | `op 14` | | `EmjActuator.Advance` |
| Advance @32 | `op 17` | | `EmjActuator.Advance` |
| Advance @2 | `op 9` | | `EmjActuator.Advance` |

---

## Phase 6 — Meld container nodes (passive, only if you'll enable calling)

`MeldGroupNodeIds` is **empty**, so `meldCount` is always 0. That is correct for the current bot,
which auto-passes every call and therefore keeps its hand concealed. You only need this phase
before enabling offensive calling — otherwise skip it.

- [ ] Manually call a pon or chi in a casual hand so an open meld is on the board.
- [ ] Run `/amjs tree`. Find the container node(s) whose subtree decodes to the called tiles
      (the dump prints `-> <tile>` under image nodes).
- [ ] Record those node IDs and set `AmjsConstants.MeldGroupNodeIds`. Rebuild.
- [ ] Run `/amjs melds` → confirms it decodes the meld(s) correctly (chii/pon/kan classification
      via `Meld.TryClassify`). With the array empty it just prints the "configure me" hint.

---

## Phase 7 — Single-action actuation (active, low risk)

Now confirm the captured protocol actually drives the game, not just matches the logs.

- [ ] On your turn, `/amjs discard 13` (slot 13 = drawn tile = tsumogiri). The drawn tile should
      leave and the turn should pass. Log shows `discard: fired [15, …] + [7, 13]`.
- [ ] Try a middle slot, e.g. `/amjs discard 5`, and confirm the correct tile leaves.
- [ ] On a result screen, `/amjs advance` → it should progress (agari modal → end-of-hand →
      next hand). Run it at each result state and confirm the matching op fires.

If a discard logs success but nothing happens on screen, the opcode/handshake is wrong — go back to
Phase 5. If the wrong tile leaves, the slot→node mapping is wrong — go back to Phase 2.

---

## Phase 8 — Full loop integration

- [ ] `/amjs auto on` at the start of your turn on a casual table. Watch a complete hand:
  - discards fire on a humanized delay (`TurnDelayMin/MaxMs`)
  - offered calls are passed; an offered ron/tsumo is accepted
  - the result screen advances and the next hand begins
- [ ] Confirm it does **not** double-act across the `6 → 30` same-turn transition.
- [ ] Let it run a full hanchan. Confirm it survives end-of-match (and note what it does there —
      stop vs. requeue is currently undefined and may be a follow-up).
- [ ] `/amjs auto off` to stop. Toggling also works via the window.

A stall on your turn most often means `TryReadHand` returned fewer tiles than
`14 - 3*meldCount` (`AutoPlayController.DoTurn`) — i.e. a node/decode regression from Phase 2/3, or
a meld the reader can't see (Phase 6).

---

## Constant → source map

| Constant | File |
|---|---|
| `EmjAddon`, `HandTileNodeIds`, `DrawnTileNodeId`, `HandButtonNodeListIndex`, `HandImageNodeListIndex`, `MeldGroupNodeIds` | `AutoMahjongSolver/Core/AmjsConstants.cs` |
| Icon-id ranges, aka ids, path regex | `AutoMahjongSolver/Core/Game/EmjTiles.cs` |
| `OpHighlight`/`OpDiscardCommit`/`OpCallOption`, states `6`/`30`, advance opcodes | `AutoMahjongSolver/Core/Game/EmjActuator.cs` |
| `CallLabelIndex`, state codes in the loop, `Call(0)`/`Call(1)` accept/pass, "Ron" test | `AutoMahjongSolver/Core/AutoPlayController.cs` |

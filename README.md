<p align="center">
  <img src="Mahjong.Plugin.Dalamud/images/icon.png" width="120" alt="Doman Mahjong Solver icon">
</p>

<h1 align="center">Doman Mahjong Solver (ALPHA)</h1>

<p align="center">
  A helper for <b>Doman Mahjong</b> at the Gold Saucer.<br>
  Hints while you play — or let it play for you.
</p>

<p align="center">
  <a href="https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/XeldarAlz/FFXIV-DomanMahjongSolver?label=release&color=blue"></a>
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0--or--later-green"></a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-FFXIV%20%7C%20Dalamud-orange">
</p>

---

## What it does

You sit at a mahjong table. A small window shows up, watches your hand, and suggests the best tile to discard and why. Three modes, one click each:

- **Off** — plugin sleeps.
- **Hints** — shows the best discard + top alternatives with a reason. You click every move. *100% safe.*
- **Auto-play** — plays for you with natural pacing that looks like a person thinking.

## Client compatibility

The mahjong addon ships under different names and memory layouts per region. EU is the reference variant; NA has the same texture base + offsets in the layout JSON but hasn't been re-verified against v0.1.0.11. JP and OC need verification dumps.

| Feature | EU (`Emj`) | NA (`EmjL`) | JP | OC |
|---|---|---|---|---|
| Window detection | Yes | Yes | Untested | Untested |
| Hand / score reading | Yes | Needs re-verification ([#30](https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues/30)) | Untested | Untested |
| Discard (state-30 in-hand) | Yes | Probably yes | Untested | Untested |
| Discard (state-6 self-declare popup) | Yes | Probably yes | Untested | Untested |
| Post-call discard popup | Yes | Probably yes | Untested | Untested |
| Pon / Chi / Kan acceptance | Yes | Likely yes after v0.1.0.11 ([#30](https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues/30)) | Untested | Untested |
| Riichi / Tsumo / Ron commit | Yes (v0.1.0.11) | Untested | Untested | Untested |

If you're on JP or OC and willing to help verify: seat at a Doman table, run `/mjauto variant dump`, and attach the file to a new issue. That's the input needed to wire each client up.

Known edge cases that may stall the bot (manual click resumes play):
- Post-MinKan transient (between kan declaration and rinshan draw)
- Two consecutive Pon offers within ~1 second

Both have capture commands documented in [`docs/dispatch-protocol.md`](docs/dispatch-protocol.md) — running `/mjauto capture <label>` against either scenario produces the data needed to fix it for everyone.

## Install

In-game: `/xlsettings` → **Experimental** → paste into **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/XeldarAlz/FFXIV-DomanMahjongSolver/main/repo/repo.json
```

Tick the checkbox, save. Then `/xlplugins` → search **Doman Mahjong Solver** → Install. Open with `/mjauto` and accept the short notice.

## Using it

`/mjauto` opens the main window. At a live table it fills in scores for all four seats, your current hand, the top 3 discard candidates with short reasoning, and the last action the plugin took. Under **Settings**: delay slider (how long it "thinks" before each click) and a developer-tools toggle.

**If the plugin misclicks a call prompt** (rare, but complex multi-chi menus can confuse it):

- Click the right option yourself in-game — the plugin resumes on the next turn.
- Or from chat: `/mjauto pass <N>` where `<N>` is the button index (0 = leftmost, rightmost is always Pass).

**If the plugin stalls** (the popup is open and the bot stops acting):

- Switch to **Off** mode to stop the bot retrying.
- Click the right option manually — the bot resumes from the next stable state when you re-enable Auto-play.
- If you can reproduce it, run `/mjauto capture <stuck-state-name>` then click manually — the captured `FireCallback` payload lands in `pluginConfigs/Mahjong.Plugin.Dalamud/emj-captures.log` and is the fastest way to get a fix shipped.

## Problems?
- [Open an issue](https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues) with the error text plus the last ~30 lines of the chat log (each dispatch is annotated with `schedState`/`curState`/`path` so a single log paste is usually enough to pin a regression).

## License

**AGPL-3.0-or-later.** Source is open; derivatives must be too.

---

<details>
<summary><b>For developers</b></summary>

### Build & test

```bash
dotnet build Mahjong.Plugin.Dalamud.sln
dotnet test  Mahjong.Plugin.Dalamud.sln
```

**593 tests** across seven suites:

- **Mahjong.Core** (58) — value-type semantics, defensive-copy contract.
- **Mahjong.Rules** (51) — yaku rules + scoring tiers + dora cycles + conflict declarations.
- **Mahjong.Plugin.Game** (51) — `Result<T,E>`, JSON layout loader, `ActionStateMachine` transitions, config migrators.
- **Mahjong.Engine** (116) — decomposition, shanten, ukeire, fu, scoring (via `Scorer + RiichiRuleSet`), yaku detection.
- **Mahjong.Replay** (17) — Tenhou parser + golden-file regression suite.
- **Mahjong.Plugin.Dalamud** (214) — config service, discard capture strategies, MeldTracker (incl. chi/pon race deferral), AutoPlayLoop accept-index computation, GameLogger dedup + hand-end, findings log, telemetry adapters.
- **Mahjong.Policy** (86) — every sub-policy in isolation, MCTS pool semantics, weight bundle defaults, JSON weight provider, structured `Decision<T>` rationale.

Every project except `Mahjong.Plugin.Dalamud` itself is Dalamud-free and portable. See [`docs/architecture.md`](docs/architecture.md) for the layered overview and extension points, and [`docs/dispatch-protocol.md`](docs/dispatch-protocol.md) for the source-of-truth inventory of every Doman Mahjong popup/state and its dispatch shape (verified ✅ / awaiting verification 🟡 / broken 🔴 / unknown ⚫).

### Layout

```
FFXIV-DomanMahjongSolver/
├── Mahjong.Core/                value types — Tile, Meld, Hand, Decomposition, ...
├── Mahjong.Rules/               IRuleSet + 38 IYakuRule + scoring/dora/fu rules
├── Mahjong.Policy.Abstractions/ contracts — IPolicy + sub-policies, IRandomSource, weights
├── Mahjong.Plugin.Game/         plugin contracts + LayoutProfile + ActionStateMachine
├── Mahjong.Replay/              Tenhou parser + golden-file regression harness
├── Mahjong.Engine/              decomposition · shanten · ukeire · Scorer
├── Mahjong.Policy/              heuristic + ISMCTS implementations · weight tuner
├── Mahjong.Tuner/               offline weight optimization (console exe)
├── Mahjong.Plugin.Dalamud/      the Dalamud plugin (thin shell)
│
├── data/
│   ├── layouts/                 per-variant addon offset profiles (JSON)
│   ├── replays/                 Tenhou logs + golden snapshots for regression
│   └── weights/                 tuner output — versioned weight bundles
│
├── docs/
│   ├── architecture.md          layered overview · extension points
│   ├── dispatch-protocol.md     popup-by-popup dispatch shapes (verified/broken)
│   └── ruleset.md               Doman vs Riichi rules spec
│
├── server/                      Cloudflare Worker + Backblaze B2 telemetry sink
├── tests/                       per-project test suites (593 tests)
├── tools/                       Node + Python + PowerShell scripts:
│                                  - B2 telemetry pull / analysis (b2-*.mjs, analyze-*.mjs)
│                                  - cross-install offset RE scanners (scan-*.mjs)
│                                  - per-variant capture helpers (scan_tiles.py)
├── repo/repo.json               Dalamud plugin manifest (CI-checked against Directory.Build.props)
└── .github/workflows/           CI (build · test · format · version sync) · auto-tag · release
```

### Releasing

Bump `<Version>` in [`Directory.Build.props`](Directory.Build.props) (single source of truth) **and** `AssemblyVersion` + `TestingAssemblyVersion` in `repo/repo.json`. CI's `guards` job fails fast if the three values don't match. Merge to main → `auto-tag` workflow creates the `vX.Y.Z` tag → `release` workflow builds and uploads `latest.zip`. On first run per version, the release tag sometimes needs a one-time manual re-push (GitHub won't let workflow-pushed tags trigger other workflows) — `gh workflow run release.yml --ref vX.Y.Z` is the standard recovery.

After the release publishes, set proper release notes via `gh release edit vX.Y.Z --notes "$(cat <<EOF ... EOF)"` — the boilerplate "Full Changelog: ..." is not enough for an alpha plugin where users rely on the release page to understand what changed.

### Roadmap

The end goal is full intelligent automation across **all clients** (EU, NA, JP, OC) — addon detection, tile reading, hint overlay, auto-discard, and full call acceptance (Pon / Chi / Kan / Riichi / Tsumo / Ron) at parity on every variant.

#### Shipped

- Multi-variant addon resolution (`Emj` + `EmjL`) with auto-detect on load
- Per-variant tile encoding (texture base + akadora 5m/5p/5s flip) handled for both
- Hand / score / discard-count readout from addon memory
- Hints mode with reasoning + top-3 alternatives
- Auto-discard via the two-callback handshake `[15, textureId]` + `[7, slotIndex]` (corpus-verified 2026-05-23, the cause of the prior 0 % hand-end rate)
- Call-prompt acceptance via `FireCallback` opcode 11 with per-action accept-button index computation
- Tsumo via the dedicated agari opcode 9 (corpus-confirmed, 14 installs)
- Ron via the dedicated agari opcode 10 (routed but unverified in live play)
- Riichi declaration: opcode-11 popup accept + FSM riichi-confirm latch + tsumogiri (replaces the dead opcode-8 path)
- Akadora-aware scoring: red 5s in closed hand and open melds contribute to dora count
- MeldTracker chi/pon/minkan inference from closed-hand deltas + opp-discard increments, with a 30-tick deferred-baseline retry to ride out memory-write races
- Kan-aware TsumogiriFallback (each meld contributes its actual `TileCount`)
- Self-AnKan tracked via `MeldTracker.Record` so suggestions don't pause for the rest of the hand
- AutoPlayLoop FSM duplicate-fire guard (animation-gap `legal=None` ticks no longer clear the retry-cooldown context)
- GameLogger writes hand-end events into the new hand-file so they survive concurrent telemetry uploads
- EfficiencyPolicy defensive guard catches DiscardScorer invariant exceptions on shanten-invalid mid-transition states
- Per-dispatch chat-log annotation (`schedState`/`curState`/`path`) so regressions are one log line away
- Engine: shanten · ukeire · yaku · fu · scoring (116 tests)
- Policy: efficiency · ISMCTS w/ progressive widening · Bayesian opponent model · evolutionary weight tuner · Tenhou log parser (86 tests)

#### In progress

- NA / `EmjL` parity re-verification against v0.1.0.11 — the chi-accept fixes should apply identically to both variants but no NA tester has confirmed since the rework ([#30](https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues/30))
- `OurSeat` / `OurRiichi` / `DealerSeat` addon offsets — cross-install scan of the `agent_b64` v=2 memdumps produced candidate offsets at `+0x009e`/`+0x00a6`/`+0x00ae`; needs in-game validation against a controlled multi-hand session
- Two stuck-state RE captures: post-MinKan transient and post-2nd-consecutive-pon ([`docs/dispatch-protocol.md`](docs/dispatch-protocol.md))

#### Planned

- Plugin-layer replay-harness fixture corpus — every captured `FireCallback` frame becomes a replayable scenario in CI ([#38](https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues/38))
- JP / OC client verification (variant dumps + capture logs needed)
- Multi-chi variant selection — currently always picks the leftmost button
- Opponent open-meld struct decode from `agent_b64` for the opponent danger model
- Per-discard tedashi vs. tsumogiri bit (currently all opp discards are treated as tedashi)
- Tuner re-run against novice-table corpus once collected — current weights are Tenhou-trained against expert opponents

</details>

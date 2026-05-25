# Contributing

Thanks for taking an interest. This is a small solo project, but PRs are welcome and I'll review them.

## Quick start

```bash
git clone https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver.git
cd FFXIV-DomanMahjongSolver
dotnet restore Mahjong.Plugin.Dalamud.sln
dotnet build   Mahjong.Plugin.Dalamud.sln
dotnet test    Mahjong.Plugin.Dalamud.sln
```

You need the .NET 10 SDK. The plugin requires Dalamud at runtime; CI pulls a Dalamud dev build automatically and that's enough to compile. See `.github/workflows/ci.yml` to reproduce CI locally.

## Test suite

**593 tests** across seven suites:

- **Mahjong.Core** (58): value-type semantics, defensive-copy contract.
- **Mahjong.Rules** (51): yaku rules + scoring tiers + dora cycles + conflict declarations.
- **Mahjong.Plugin.Game** (51): `Result<T,E>`, JSON layout loader, `ActionStateMachine` transitions, config migrators.
- **Mahjong.Engine** (116): decomposition, shanten, ukeire, fu, scoring (via `Scorer + RiichiRuleSet`), yaku detection.
- **Mahjong.Replay** (17): Tenhou parser + golden-file regression suite.
- **Mahjong.Plugin.Dalamud** (214): config service, discard capture strategies, MeldTracker (incl. chi/pon race deferral), AutoPlayLoop accept-index computation, GameLogger dedup + hand-end, findings log, telemetry adapters.
- **Mahjong.Policy** (86): every sub-policy in isolation, weight bundle defaults, JSON weight provider, structured `Decision<T>` rationale.

Every project except `Mahjong.Plugin.Dalamud` itself is Dalamud-free and portable.

## Project layout

```
FFXIV-DomanMahjongSolver/
├── Mahjong.Core/                value types: Tile, Meld, Hand, Decomposition, ...
├── Mahjong.Rules/               IRuleSet + 38 IYakuRule + scoring/dora/fu rules
├── Mahjong.Policy.Abstractions/ contracts: IPolicy + sub-policies, IRandomSource, weights
├── Mahjong.Plugin.Game/         plugin contracts + LayoutProfile + ActionStateMachine
├── Mahjong.Replay/              Tenhou parser + golden-file regression harness
├── Mahjong.Engine/              decomposition · shanten · ukeire · Scorer
├── Mahjong.Policy/              heuristic policy implementations · weight tuner
├── Mahjong.Tuner/               offline weight optimization (console exe)
├── Mahjong.Plugin.Dalamud/      the Dalamud plugin (thin shell)
│
├── data/
│   ├── layouts/                 per-variant addon offset profiles (JSON)
│   ├── replays/                 Tenhou logs + golden snapshots for regression
│   └── weights/                 tuner output: versioned weight bundles
│
├── docs/
│   ├── architecture.md          layered overview · extension points
│   ├── dispatch-protocol.md     popup-by-popup dispatch shapes (verified/broken)
│   ├── roadmap.md               shipped / in progress / planned
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

Rule of thumb: if logic can live in `Mahjong.Engine`, `Mahjong.Rules`, or `Mahjong.Policy`, put it there and test it. Keep `Mahjong.Plugin.Dalamud/` focused on glue: reading addons, dispatching clicks, drawing windows.

See [`docs/architecture.md`](docs/architecture.md) for the layered overview and extension points, and [`docs/dispatch-protocol.md`](docs/dispatch-protocol.md) for the source-of-truth inventory of every Doman Mahjong popup/state and its dispatch shape.

## Before you open a PR

1. `dotnet build` cleanly.
2. `dotnet test` passes. If you changed engine or policy behavior, add or update tests.
3. Keep the diff focused. One concern per PR.
4. Match the existing style: terse and direct. No heavy abstractions "for later."
5. If your change affects what a user sees or types, update the README.

## Good first issues

Check the issue tracker for anything labeled `good first issue`. If nothing's there, [`docs/roadmap.md`](docs/roadmap.md) lists open work: especially JP / OC client verification and the remaining stuck-state captures.

## Releasing (maintainers)

Bump `<Version>` in [`Directory.Build.props`](Directory.Build.props) (single source of truth) **and** `AssemblyVersion` + `TestingAssemblyVersion` in `repo/repo.json`. CI's `guards` job fails fast if the three values don't match. Merge to main → `auto-tag` workflow creates the `vX.Y.Z` tag → `release` workflow builds and uploads `latest.zip`.

On first run per version, the release tag sometimes needs a one-time manual re-push (GitHub won't let workflow-pushed tags trigger other workflows); `gh workflow run release.yml --ref vX.Y.Z` is the standard recovery.

After the release publishes, set proper release notes via `gh release edit vX.Y.Z --notes "$(cat <<EOF ... EOF)"`. The boilerplate "Full Changelog: ..." is not enough for an alpha plugin where users rely on the release page to understand what changed.

## Reporting bugs

Use the bug report template. Include plugin version, mode, and repro steps. Each dispatch is annotated with `schedState`/`curState`/`path` so a single log paste from the chat log is usually enough to pin a regression.

## Security

Please don't file public issues for security problems; see [SECURITY.md](SECURITY.md).

## Code of conduct

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Be decent.

## License

By contributing, you agree your contributions are licensed under AGPL-3.0-or-later, the same as the project.

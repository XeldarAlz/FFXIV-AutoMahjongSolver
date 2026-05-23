# Tuner

Console executable for offline weight optimization. Two algorithms:

- **Evolutionary tuner** (default) — (μ/μ, λ)-ES with diagonal-covariance
  Gaussian proposals. Faster convergence, occasional drift.
- **Coordinate descent** (`coord` subcommand) — perturbs one weight at a
  time. Slower per iteration but every step is a monotone improvement.
- **Verify** (`verify` subcommand) — head-to-head match between two weight
  sets at high hand counts to confirm a tuner result is real, not noise.

## Usage

```bash
# Evolutionary tuner — defaults pop=8, gens=10, hands=50, seed=42
dotnet run --project Tuner -c Release

# Coordinate descent
dotnet run --project Tuner -c Release -- coord 30 200 4242

# Verify a tuned set vs. the current default
dotnet run --project Tuner -c Release -- verify 500 1234
```

## Output

JSON written to `data/weights/{evo|coord}-{timestamp}.json`. Drop into
`Mahjong.Policy/Tuning/JsonWeightProvider` to use the tuned values.

The pre-Phase-3 workflow (emit C# source you paste into
`DiscardScorer.Weights.Default`) is gone — JSON weights are loadable at
runtime, hot-swappable, and version-stamped via `WeightBundle.SchemaVersion`.

## Reproducibility

Tuning runs are deterministic under a fixed seed. `Settings.Seed` propagates
into a single `SeededRandomSource(seed)` used by every stochastic component
(HandSimulator, SelfPlayRunner). Re-running with the same seed produces
bit-identical output.

## Riichi rules

Tuner runs use `RiichiRuleSet` — matches the Tenhou corpora most policies
are calibrated against. See `docs/ruleset.md` for why mixing rulesets
corrupts tuning data.

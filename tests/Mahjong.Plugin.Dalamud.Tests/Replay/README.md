# Replay fixture corpus (Track 0)

Plugin-layer regression suite driven against captured addon-memory + AtkValues frames. Every reproducible failure mode lives here as a JSON fixture; CI gates on the whole directory.

## Layout

- `AddonMemoryBuilder.cs` — fluent builder for synthetic byte buffers (`WithScores`, `WithHand`, `WithDiscardCounts`, …). Hand-authored fixtures use this; telemetry-captured fixtures carry raw base64 directly.
- `ReplayFixture.cs` — JSON schema. One file per scenario under `fixtures/`.
- `FixtureLoader.cs` — JSON → `ReplayFixture` (snake_case → C#).
- `BuildSnapshotFromMemoryTests.cs` — inline-builder smoke tests, no JSON. Use for synthetic invariants.
- `ReplayFixtureTests.cs` — `[Theory]` over `fixtures/**/*.json`. Add a new file → discovered automatically on next test run.

## Adding a new fixture

### From telemetry

Use `tools/extract-fixture.mjs <tag_id>` (Phase 3, not yet shipped) to pull an `input-pre` memdump from B2 and emit the fixture JSON.

### Hand-authored synthetic

Edit `ReplayFixtureTests.Regenerate_synthetic_seed_fixtures`, add a `WriteFixture(...)` call with an `AddonMemoryBuilder` chain and `ReplayExpected` assertions. Run:

```powershell
$env:MJ_REGEN_FIXTURES="1"; dotnet test --filter "FullyQualifiedName~Regenerate_synthetic_seed_fixtures"
```

The JSON lands under `fixtures/`. Commit it alongside the generator change so reviewers can see both.

## Schema

```jsonc
{
  "name": "state30_our_turn_emj",
  "description": "Optional context line.",
  "variant": "Emj",                                // matches data/layouts/<variant>.json
  "addon_memory_base64": "...",                    // raw 0..AddonMemorySize bytes (padded if shorter)
  "atk_values": [                                  // marshalled AtkValue slots, position-significant
    { "type": "Int",    "int": 30 },
    { "type": "String", "string": "Pon" }
  ],
  "call_modal_visible": false,
  "list_widget_labels": null,                       // novice-table list prompt: ["Pon", "Pass"]
  "expected": {
    "state_code": 30,
    "hand": "1234m456p789s1234z",                  // Tiles.Parse expression; sorted-multiset compare
    "legal_flags": ["Discard"],                    // OR-combined ActionFlags names
    "score_self": 25000,
    "wall_remaining": 70,
    "aka_dora": 0,
    "meld_count": 0
  }
}
```

Every `expected.*` field is optional — null skips the assertion. Use it to pin only what the scenario is actually about.

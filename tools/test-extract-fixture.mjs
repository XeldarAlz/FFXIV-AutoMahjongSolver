// Smoke-test extract-fixture.mjs end-to-end: synthesizes a minimal memdump
// record matching the production schema, runs extract-fixture, and verifies
// the resulting fixture is loadable shape.
//
// Run: node tools/test-extract-fixture.mjs

import { mkdtemp, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";

const tmp = await mkdtemp(join(tmpdir(), "mj-extract-test-"));
try {
  // Build a synthetic addon-memory buffer with self_score=25000 (offset 0x0500).
  const addon = Buffer.alloc(0x1300);
  addon.writeInt32LE(25000, 0x0500);
  addon.writeInt32LE(25000, 0x07E0);
  addon.writeInt32LE(25000, 0x0AC0);
  addon.writeInt32LE(25000, 0x0DA0);

  // Build two AtkValue slots: [Int=30, Int=0]. 16 bytes each, type at +0, value at +8.
  const atk = Buffer.alloc(32);
  atk.writeInt32LE(3, 0);    // Type = Int (3)
  atk.writeInt32LE(30, 8);   // Value = 30
  atk.writeInt32LE(3, 16);   // Type = Int (3)
  atk.writeInt32LE(0, 24);   // Value = 0

  const record = {
    t: "2026-05-25T12:00:00.000Z",
    seq: 42,
    v: 2,
    reason: "input-pre",
    addon_addr: 0,
    addon_b64: addon.toString("base64"),
    atk_addr: 0,
    atk_count: 2,
    atk_b64: atk.toString("base64"),
    variant: "Emj",
    hash: "deadbeefdeadbeef",
  };

  const ndjsonPath = join(tmp, "memdumps-test.ndjson");
  await writeFile(ndjsonPath, JSON.stringify(record) + "\n");

  const outDir = join(tmp, "fixtures");
  const result = spawnSync("node", [
    "tools/extract-fixture.mjs",
    ndjsonPath,
    "42",
    "--name", "smoke_extract_test",
    "--out", outDir,
  ], { encoding: "utf8" });

  if (result.status !== 0) {
    console.error("extract-fixture failed:");
    console.error(result.stdout);
    console.error(result.stderr);
    process.exit(1);
  }
  console.log(result.stdout);

  const fixturePath = join(outDir, "smoke_extract_test.json");
  const fixture = JSON.parse(await readFile(fixturePath, "utf8"));

  const assertions = [
    ["fixture.name", fixture.name, "smoke_extract_test"],
    ["fixture.variant", fixture.variant, "Emj"],
    ["fixture.expected.state_code", fixture.expected.state_code, 30],
    ["fixture.atk_values[0].type", fixture.atk_values[0].type, "Int"],
    ["fixture.atk_values[0].int", fixture.atk_values[0].int, 30],
    ["fixture.atk_values[1].int", fixture.atk_values[1].int, 0],
    ["fixture.call_modal_visible", fixture.call_modal_visible, false], // state-30 isn't a call prompt state
  ];

  let failed = 0;
  for (const [path, actual, expected] of assertions) {
    if (actual === expected) {
      console.log(`  ✓ ${path} = ${JSON.stringify(actual)}`);
    } else {
      console.error(`  ✗ ${path} expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
      failed++;
    }
  }

  // Verify the addon_memory_base64 decodes back to the original
  const decoded = Buffer.from(fixture.addon_memory_base64, "base64");
  if (decoded.readInt32LE(0x0500) === 25000) {
    console.log(`  ✓ fixture.addon_memory_base64 round-trips self_score at 0x0500`);
  } else {
    console.error(`  ✗ fixture.addon_memory_base64 self_score corrupted: ${decoded.readInt32LE(0x0500)}`);
    failed++;
  }

  if (failed > 0) {
    console.error(`\n${failed} assertion(s) failed`);
    process.exit(1);
  }
  console.log("\nAll assertions passed.");
} finally {
  await rm(tmp, { recursive: true, force: true });
}

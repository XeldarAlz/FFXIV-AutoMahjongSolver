// Validate the OurSeat candidate offsets pinned by find_seat_offsets.py
// (+0x130, +0x1248, +0x12BC) against a local memdump corpus, WITHOUT
// requiring the games stream.
//
// Method: each candidate offset is read as an int32 across every
// snapshot. The "seat" field — if it really is the player's seat —
// will:
//   1. Stay constant within a hand (player doesn't change seats mid-hand).
//   2. Change at hand boundaries (Doman dealer rotates every hand).
//   3. Take values in [0..3].
//
// Hand boundaries can be detected without the games stream: discard
// count for a given seat resets to 0 when a new hand starts. We use
// "self_discard_count drops to 0 from a non-zero value" as the
// hand-boundary marker.
//
// For each candidate we report: distinct values seen, distribution,
// run-length stats (mean run within a hand), and changes-at-boundary
// rate. A real seat field has narrow distribution {0..3}, long runs
// within hands, and changes ≥50% of the time at boundaries.
//
// Usage: node tools/check-seat-offsets-local.mjs <memdump-dir>

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const dir = process.argv[2];
if (!dir) {
  console.error("Usage: node tools/check-seat-offsets-local.mjs <memdump-dir>");
  process.exit(2);
}

const CANDIDATES = [
  { name: "0x0130", offset: 0x0130 },
  { name: "0x1248", offset: 0x1248 },
  { name: "0x12BC", offset: 0x12BC },
  // A few extras to cross-check: the value of stable byte runs at
  // 0x00FE..0x011F (per deep-analysis.md) and 0x01A5..0x01C3 might
  // contain dealer / round / seat info too.
  { name: "0x00FE", offset: 0x00FE },
  { name: "0x0100", offset: 0x0100 },
  { name: "0x01A5", offset: 0x01A5 },
];
const SELF_DC_BYTE = 0x04FE;

async function readNdjson(path) {
  const raw = await readFile(path);
  const body = (raw[0] === 0x1f && raw[1] === 0x8b) ? gunzipSync(raw) : raw;
  return body.toString("utf8").split("\n").filter(Boolean)
    .map((l) => { try { return JSON.parse(l); } catch { return null; } })
    .filter(Boolean);
}

async function loadAll(d) {
  const all = [];
  const stack = [d];
  while (stack.length) {
    const cur = stack.pop();
    for (const e of await readdir(cur, { withFileTypes: true })) {
      const p = join(cur, e.name);
      if (e.isDirectory()) stack.push(p);
      else if (e.isFile() && e.name.endsWith(".gz"))
        for (const r of await readNdjson(p)) all.push(r);
    }
  }
  return all;
}

const records = (await loadAll(dir))
  .filter((r) => typeof r.addon_b64 === "string" && typeof r.seq === "number")
  .map((r) => ({ ...r, buf: Buffer.from(r.addon_b64, "base64") }))
  .filter((r) => r.buf.length >= 0x12C4)
  .sort((a, b) => a.seq - b.seq);

console.log(`loaded ${records.length} usable records`);

// Detect hand boundaries: self discard count drops from non-zero to 0
// (or any seat's dc resets). Within-hand transitions are seq pairs where
// no dc resets.
const boundaries = []; // record indices where prev dc > 0 and curr dc == 0
for (let i = 1; i < records.length; i++) {
  const prev = records[i - 1].buf[SELF_DC_BYTE];
  const curr = records[i].buf[SELF_DC_BYTE];
  if (prev > 0 && curr === 0) boundaries.push(i);
}
console.log(`hand boundaries (self dc resets): ${boundaries.length}`);

const fmtPct = (n) => (n * 100).toFixed(1) + "%";

for (const cand of CANDIDATES) {
  const values = records.map((r) => r.buf.readInt32LE(cand.offset));
  const byteValues = records.map((r) => r.buf[cand.offset]);
  const distinct = new Set(values);
  const inRange04 = byteValues.filter((v) => v >= 0 && v <= 3).length;

  // Run-length within hands: from one hand boundary to the next, how
  // often does this value change?
  let totalRuns = 0;
  let totalChanges = 0;
  let prevBoundary = 0;
  for (const b of boundaries) {
    let runs = 1;
    for (let i = prevBoundary + 1; i < b; i++) {
      if (values[i] !== values[i - 1]) runs++;
    }
    totalRuns += runs;
    prevBoundary = b;
  }
  // Boundary-change rate: at each boundary, did the value change vs.
  // the last snapshot of the prior hand?
  let boundaryChanges = 0;
  for (const b of boundaries) {
    if (b > 0 && values[b] !== values[b - 1]) boundaryChanges++;
  }
  // Distribution: top 5 values + their counts
  const counts = new Map();
  for (const v of values) counts.set(v, (counts.get(v) ?? 0) + 1);
  const top5 = [...counts.entries()].sort((a, b) => b[1] - a[1]).slice(0, 5);

  console.log(`\n=== ${cand.name} (+${cand.offset.toString(16)}) ===`);
  console.log(`  distinct int32 values: ${distinct.size}`);
  console.log(`  byte values in 0..3: ${inRange04}/${byteValues.length} (${fmtPct(inRange04 / byteValues.length)})`);
  console.log(`  boundary-change rate: ${boundaryChanges}/${boundaries.length} (${fmtPct(boundaryChanges / Math.max(1, boundaries.length))})`);
  console.log(`  top 5 int32 values (count): ${top5.map(([v, c]) => `${v}=${c}`).join(", ")}`);

  const byteCounts = new Map();
  for (const v of byteValues) byteCounts.set(v, (byteCounts.get(v) ?? 0) + 1);
  const top5Byte = [...byteCounts.entries()].sort((a, b) => b[1] - a[1]).slice(0, 5);
  console.log(`  top 5 byte values (count):  ${top5Byte.map(([v, c]) => `${v}=${c}`).join(", ")}`);
}

console.log("\n=== Interpretation hints ===");
console.log(`A real seat field will show:`);
console.log(`  - distinct values ≤ 4 (or for byte view, all in 0..3)`);
console.log(`  - high boundary-change rate (≥50% — seat rotates every hand)`);
console.log(`  - balanced byte distribution (each value 20-30% across many hands)`);

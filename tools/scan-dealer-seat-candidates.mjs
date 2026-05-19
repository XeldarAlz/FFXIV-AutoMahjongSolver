// Wide scan: find every byte/int32 offset in addon_b64 whose value
// rotates through a small set of small ints (0..3 or 1..4) AND changes
// at hand boundaries (wall-jump detected). That's the signature of a
// dealer-seat / hand-number field.
//
// Detection method:
//   1. Hand boundaries from wall resets: wall jumps up by ≥10 in one
//      tick (a fresh hand was dealt). More reliable than dc resets
//      because the addon updates wall in lockstep with deal.
//   2. For each byte offset 0..addonLen, the score = (boundary-change
//      rate) × (1 if all values within hand are constant else 0)
//      × (1 if value range is {0..3} or {1..4} else 0).
//   3. Top 20 candidates printed with their distinct values, top-5
//      counts, and boundary-change rate.
//
// For each surviving candidate, also report whether it's distinct
// from the obvious noise (count bytes, score fields).
//
// Usage: node tools/scan-dealer-seat-candidates.mjs <memdump-dir>

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const dir = process.argv[2];
if (!dir) {
  console.error("Usage: node tools/scan-dealer-seat-candidates.mjs <memdump-dir>");
  process.exit(2);
}

const ADDON_LEN_MIN = 0x107E;
const WALL_JUMP_THRESHOLD = 10;

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
  .filter((r) => r.buf.length >= ADDON_LEN_MIN)
  .sort((a, b) => a.seq - b.seq);

console.log(`loaded ${records.length} records`);
const addonLen = records[0].buf.length;
console.log(`addon buf length: ${addonLen} (0x${addonLen.toString(16)})`);

// Wall remaining is encoded by the engine but not in the addon directly —
// fall back to per-seat total discard counts (sum across the 4 count
// bytes). Wall = 70 - total_discards. Wall jumps from low → ~70 means
// new hand.
const TOTAL_DC_OFFSETS = [0x04FE, 0x07DE, 0x0ABE, 0x0D9E];
const wallEstimate = (buf) => {
  let total = 0;
  for (const off of TOTAL_DC_OFFSETS) total += buf[off];
  return 70 - total;
};

const walls = records.map((r) => wallEstimate(r.buf));
const boundaries = []; // indices i where walls[i] - walls[i-1] >= WALL_JUMP_THRESHOLD
for (let i = 1; i < walls.length; i++) {
  if (walls[i] - walls[i - 1] >= WALL_JUMP_THRESHOLD) boundaries.push(i);
}
console.log(`hand boundaries (wall jumps ≥${WALL_JUMP_THRESHOLD}): ${boundaries.length}`);
if (boundaries.length < 3) {
  console.error("not enough boundaries to score candidates");
  process.exit(1);
}

// For efficiency: pre-build per-offset byte arrays once. Then evaluate
// each offset cheaply.
console.log(`scanning ${addonLen} byte offsets for dealer-seat-like signature...`);

const candidates = [];
const KNOWN_NOISE_OFFSETS = new Set([
  // Per-seat dc count bytes (always 0..30, change every discard — noisy)
  0x04FE, 0x07DE, 0x0ABE, 0x0D9E,
]);

for (let off = 0; off < addonLen; off++) {
  if (KNOWN_NOISE_OFFSETS.has(off)) continue;

  // Pull the byte values from every record into a typed array
  const vals = new Uint8Array(records.length);
  for (let i = 0; i < records.length; i++) vals[i] = records[i].buf[off];

  // Range filter: must stay in {0..4}
  let inRange = 0, oor = 0;
  for (const v of vals) {
    if (v <= 4) inRange++;
    else { oor++; if (oor > 50) break; } // early reject very noisy bytes
  }
  if (oor > 50) continue;
  if (inRange < records.length * 0.95) continue;

  // Distinct count cap
  const distinct = new Set(vals);
  if (distinct.size < 2 || distinct.size > 5) continue;

  // Within-hand stability: between consecutive boundaries the value
  // should be roughly constant (allow ±1 changes from animations).
  let totalIntraChanges = 0;
  let prevB = 0;
  for (const b of boundaries) {
    for (let i = prevB + 1; i < b; i++) {
      if (vals[i] !== vals[i - 1]) totalIntraChanges++;
    }
    prevB = b;
  }
  // Boundary-change rate: at each boundary, did the value change
  // between the snapshot before and several after?
  let boundaryChanges = 0;
  for (const b of boundaries) {
    // Look at value 3 snapshots after the boundary vs 3 before
    const before = b >= 3 ? vals[b - 3] : vals[Math.max(0, b - 1)];
    const after = b + 3 < vals.length ? vals[b + 3] : vals[Math.min(vals.length - 1, b + 1)];
    if (before !== after) boundaryChanges++;
  }
  const boundaryChangeRate = boundaryChanges / boundaries.length;

  if (boundaryChangeRate < 0.5) continue;

  // Score: prefer high boundary-change rate, low intra-hand chatter,
  // narrow distinct count.
  const intraRatio = totalIntraChanges / records.length;
  const score = boundaryChangeRate - 2 * intraRatio - 0.05 * distinct.size;

  candidates.push({
    offset: off,
    distinct: distinct.size,
    boundaryChanges,
    boundaryChangeRate,
    intraChanges: totalIntraChanges,
    intraRatio,
    score,
    distribution: distributionOf(vals),
  });
}

candidates.sort((a, b) => b.score - a.score);

console.log(`\n${candidates.length} byte-offset candidates passed filters`);
const fmt = (n) => "0x" + n.toString(16).padStart(4, "0");

console.log(`\nTop 20:`);
console.log("offset   | distinct | bcr   | intra | score | distribution");
for (const c of candidates.slice(0, 20)) {
  console.log(
    `${fmt(c.offset)}   |     ${c.distinct}    | ${(c.boundaryChangeRate * 100).toFixed(0).padStart(3)}%  | ${c.intraChanges.toString().padStart(5)} | ${c.score.toFixed(3)} | ${c.distribution}`
  );
}

function distributionOf(arr) {
  const counts = new Map();
  for (const v of arr) counts.set(v, (counts.get(v) ?? 0) + 1);
  return [...counts.entries()].sort((a, b) => b[1] - a[1]).map(([v, c]) => `${v}=${c}`).join(",");
}

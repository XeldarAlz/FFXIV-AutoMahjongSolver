// Int32 sibling to scan-dealer-seat-candidates.mjs. Looks for 4-byte fields
// whose value sits in {0..3} and rotates predictably across hand boundaries.
// Same wall-jump detection. Usage:
//   node tools/scan-dealer-int32.mjs <memdump-dir>

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const dir = process.argv[2];
if (!dir) {
  console.error("Usage: node tools/scan-dealer-int32.mjs <memdump-dir>");
  process.exit(2);
}

const WALL_JUMP_THRESHOLD = 10;
const TOTAL_DC_OFFSETS = [0x04FE, 0x07DE, 0x0ABE, 0x0D9E];

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
  .filter((r) => r.buf.length >= 0x1300)
  .sort((a, b) => a.seq - b.seq);

console.log(`loaded ${records.length} records`);
const addonLen = records[0].buf.length;
console.log(`addon buf length: ${addonLen} (0x${addonLen.toString(16)})`);

const wallEstimate = (buf) => {
  let total = 0;
  for (const off of TOTAL_DC_OFFSETS) total += buf[off];
  return 70 - total;
};

const walls = records.map((r) => wallEstimate(r.buf));
const boundaries = [];
for (let i = 1; i < walls.length; i++) {
  if (walls[i] - walls[i - 1] >= WALL_JUMP_THRESHOLD) boundaries.push(i);
}
console.log(`hand boundaries: ${boundaries.length}`);
if (boundaries.length < 2) process.exit(1);

// For each int32-aligned offset, gather the value at each boundary's first
// stable tick (5 ticks after the boundary, to ride out the deal animation).
const samples = []; // [{offset, vals}]
const maxOff = addonLen - 4;
for (let off = 0; off < maxOff; off += 4) {
  const vals = [];
  for (const b of boundaries) {
    const sampleIdx = Math.min(b + 5, records.length - 1);
    const v = records[sampleIdx].buf.readInt32LE(off);
    vals.push(v);
  }
  // Range filter: every sampled value must sit in {0..3}
  if (!vals.every((v) => v >= 0 && v <= 3)) continue;
  // Must vary across boundaries
  const distinct = new Set(vals);
  if (distinct.size < 2) continue;
  samples.push({ offset: off, vals, distinct: distinct.size });
}

samples.sort((a, b) => b.distinct - a.distinct);

console.log(`\nint32 candidates holding values in {0..3} with ≥2 distinct across hand boundaries: ${samples.length}`);
console.log("offset   | distinct | per-boundary sample (first 12)");
for (const c of samples.slice(0, 30)) {
  const fmt = "0x" + c.offset.toString(16).padStart(4, "0");
  console.log(`${fmt}   |    ${c.distinct}     | [${c.vals.slice(0, 12).join(",")}]`);
}

// Same but for byte offsets (sometimes the dealer is a single byte, not int32).
const byteSamples = [];
for (let off = 0; off < addonLen; off++) {
  const vals = [];
  for (const b of boundaries) {
    const sampleIdx = Math.min(b + 5, records.length - 1);
    vals.push(records[sampleIdx].buf[off]);
  }
  if (!vals.every((v) => v >= 0 && v <= 3)) continue;
  const distinct = new Set(vals);
  if (distinct.size < 3) continue; // require ≥3 distinct to filter out boolean noise
  byteSamples.push({ offset: off, vals, distinct: distinct.size });
}
byteSamples.sort((a, b) => b.distinct - a.distinct);

console.log(`\nbyte candidates with ≥3 distinct values in {0..3} at sample points: ${byteSamples.length}`);
console.log("offset   | distinct | per-boundary sample (first 12)");
for (const c of byteSamples.slice(0, 30)) {
  const fmt = "0x" + c.offset.toString(16).padStart(4, "0");
  console.log(`${fmt}   |    ${c.distinct}     | [${c.vals.slice(0, 12).join(",")}]`);
}

// Scan addon for byte/int32 fields cycling through wind tile-id range
// {27,28,29,30} at hand boundaries. The addon might store our seat wind
// as the tile-id of the wind (East=27, South=28, West=29, North=30)
// rather than a 0..3 index. Same boundary detection as
// scan-dealer-seat-candidates.mjs (wall jumps), just different value
// filter.

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const dir = process.argv[2];
if (!dir) {
  console.error("Usage: node tools/scan-wind-id-candidates.mjs <memdump-dir>");
  process.exit(2);
}

const ADDON_LEN_MIN = 0x107E;
const WALL_JUMP_THRESHOLD = 10;
const WIND_IDS = new Set([27, 28, 29, 30]);

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

const TOTAL_DC_OFFSETS = [0x04FE, 0x07DE, 0x0ABE, 0x0D9E];
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
console.log(`${records.length} records, ${boundaries.length} hand boundaries`);

const addonLen = records[0].buf.length;
const candidates = [];

for (let off = 0; off < addonLen; off++) {
  const vals = new Uint8Array(records.length);
  for (let i = 0; i < records.length; i++) vals[i] = records[i].buf[off];

  // Value-range filter: must be majority {27..30}
  let inWindRange = 0;
  for (const v of vals) if (WIND_IDS.has(v)) inWindRange++;
  if (inWindRange < records.length * 0.9) continue;

  // Distinct check
  const distinct = new Set(vals);
  if (distinct.size < 2 || distinct.size > 5) continue;

  // Boundary-change rate
  let boundaryChanges = 0;
  for (const b of boundaries) {
    const before = b >= 3 ? vals[b - 3] : vals[Math.max(0, b - 1)];
    const after = b + 3 < vals.length ? vals[b + 3] : vals[Math.min(vals.length - 1, b + 1)];
    if (before !== after) boundaryChanges++;
  }
  const bcr = boundaryChanges / boundaries.length;

  candidates.push({
    offset: off,
    distinct: distinct.size,
    bcr,
    inWindRange,
    distribution: distributionOf(vals),
  });
}

candidates.sort((a, b) => b.bcr - a.bcr || b.inWindRange - a.inWindRange);

console.log(`\n${candidates.length} byte candidates with wind-id range`);
console.log("\nTop 20:");
console.log("offset   | distinct | bcr   | in-wind | distribution");
const fmt = (n) => "0x" + n.toString(16).padStart(4, "0");
for (const c of candidates.slice(0, 20)) {
  console.log(`${fmt(c.offset)}   |     ${c.distinct}    | ${(c.bcr * 100).toFixed(0).padStart(3)}%  | ${c.inWindRange.toString().padStart(5)}   | ${c.distribution}`);
}

function distributionOf(arr) {
  const counts = new Map();
  for (const v of arr) counts.set(v, (counts.get(v) ?? 0) + 1);
  return [...counts.entries()].sort((a, b) => b[1] - a[1]).map(([v, c]) => `${v}=${c}`).join(",");
}

// Refined agent_b64 scanner. Looks for:
//  - DealerSeat: byte/int32 offsets that produce a clean rotation `(boundary_idx % 4)`
//    pattern across hand boundaries.
//  - OurRiichi: byte that goes 0→1 around a paired input, STAYS 1 for many subsequent
//    state-change records, then resets to 0 at the next hand boundary.

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const dir = process.argv[2];
if (!dir) {
  console.error("Usage: node tools/scan-agent-offsets2.mjs <memdump-dir>");
  process.exit(2);
}

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

const allRecords = await loadAll(dir);
const v2 = allRecords.filter((r) => r.v === 2 && typeof r.agent_b64 === "string");
for (const r of v2) {
  r.agent = Buffer.from(r.agent_b64, "base64");
  if (typeof r.addon_b64 === "string") r.addon = Buffer.from(r.addon_b64, "base64");
}
v2.sort((a, b) => (a.seq ?? 0) - (b.seq ?? 0));
console.log(`v=2 records: ${v2.length}`);
if (v2.length === 0) process.exit(0);

const agentLen = v2[0].agent.length;
const wallEstimate = (buf) => {
  if (!buf || buf.length < 0x0E00) return -1;
  let t = 0;
  for (const o of TOTAL_DC_OFFSETS) t += buf[o];
  return 70 - t;
};
const withAddon = v2.filter((r) => r.addon).map((r) => ({ ...r, wall: wallEstimate(r.addon) }));

// Find hand boundaries (wall jump up) AND keep a sample index a few ticks past.
const boundaries = [];
for (let i = 1; i < withAddon.length; i++) {
  if (withAddon[i].wall - withAddon[i - 1].wall >= 10) boundaries.push(i);
}
console.log(`boundaries: ${boundaries.length}`);

// Take sample at boundary + 10 ticks (well past deal animation).
function sampleAtBoundary(idx) {
  return Math.min(idx + 10, withAddon.length - 1);
}

// DealerSeat: int32 offsets where values rotate { match (i % 4) OR (i % 4 + k) mod 4 OR
// reverse cycles }. For each candidate offset, score how well its sample-sequence matches
// any rotation pattern.

function rotationScore(vals) {
  // Try each starting offset k in 0..3 and each direction
  let best = 0;
  for (let k = 0; k < 4; k++) {
    for (const dir of [1, -1]) {
      let hits = 0;
      for (let i = 0; i < vals.length; i++) {
        const expected = ((i * dir + k) % 4 + 4) % 4;
        if (vals[i] === expected) hits++;
      }
      if (hits > best) best = hits;
    }
  }
  return best / vals.length;
}

console.log("\n## DealerSeat int32 candidates (rotation score ≥0.6)");
const intCandidates = [];
for (let off = 0; off < agentLen - 4; off += 4) {
  const vals = boundaries.map((b) => withAddon[sampleAtBoundary(b)].agent.readInt32LE(off));
  if (!vals.every((v) => v >= 0 && v <= 3)) continue;
  const s = rotationScore(vals);
  if (s < 0.6) continue;
  intCandidates.push({ offset: off, score: s, vals });
}
intCandidates.sort((a, b) => b.score - a.score);
for (const c of intCandidates.slice(0, 15)) {
  console.log(`  +0x${c.offset.toString(16).padStart(4, "0")}  score=${c.score.toFixed(2)}  vals=[${c.vals.slice(0, 20).join(",")}]`);
}

console.log("\n## DealerSeat byte candidates (rotation score ≥0.6)");
const byteCandidates = [];
for (let off = 0; off < agentLen; off++) {
  const vals = boundaries.map((b) => withAddon[sampleAtBoundary(b)].agent[off]);
  if (!vals.every((v) => v >= 0 && v <= 3)) continue;
  const s = rotationScore(vals);
  if (s < 0.6) continue;
  byteCandidates.push({ offset: off, score: s, vals });
}
byteCandidates.sort((a, b) => b.score - a.score);
for (const c of byteCandidates.slice(0, 15)) {
  console.log(`  +0x${c.offset.toString(16).padStart(4, "0")}  score=${c.score.toFixed(2)}  vals=[${c.vals.slice(0, 20).join(",")}]`);
}

// OurRiichi: find bytes that flip 0→1 on an input-post AND stay 1 for ≥20 subsequent
// records until the next boundary, then reset to 0.
console.log("\n## OurRiichi candidates (stay-set-after-flip pattern)");
const flipBytes = new Map(); // offset -> { flips, persistedHands }
const idxByReason = (reason) => v2.map((r, i) => ({ ...r, idx: i })).filter((r) => r.reason === reason);
const inputPosts = idxByReason("input-post");

for (const post of inputPosts) {
  const postIdx = v2.indexOf(post);
  if (postIdx < 1) continue;
  const prev = v2.slice(0, postIdx).reverse().find((r) => r.reason === "input-pre");
  if (!prev) continue;
  if (prev.agent.length !== post.agent.length) continue;

  // Find next boundary index (in withAddon ordering — but we're using v2 ordering here,
  // so approximate: find next state-change with wall jump up). For simplicity, scan
  // forward up to 200 records and look for the value staying 1.
  for (let off = 0; off < post.agent.length; off++) {
    if (prev.agent[off] === 0 && post.agent[off] === 1) {
      // Check persistence: how many records from postIdx forward have value 1 at this offset?
      let persisted = 0;
      const lookahead = Math.min(postIdx + 200, v2.length);
      for (let j = postIdx + 1; j < lookahead; j++) {
        if (v2[j].agent && v2[j].agent[off] === 1) persisted++;
        else break;
      }
      if (persisted >= 20) {
        const entry = flipBytes.get(off) ?? { flips: 0, totalPersist: 0 };
        entry.flips++;
        entry.totalPersist += persisted;
        flipBytes.set(off, entry);
      }
    }
  }
}

const ourRiichiCandidates = [...flipBytes.entries()]
  .map(([off, m]) => ({ offset: off, flips: m.flips, avgPersist: Math.round(m.totalPersist / m.flips) }))
  .sort((a, b) => b.flips - a.flips);

console.log(`  ${ourRiichiCandidates.length} offsets with 0→1 flips that persisted ≥20 records:`);
for (const c of ourRiichiCandidates.slice(0, 20)) {
  console.log(`  +0x${c.offset.toString(16).padStart(4, "0")}  flips=${c.flips}  avg_persist=${c.avgPersist}`);
}

console.log("\nDone.");

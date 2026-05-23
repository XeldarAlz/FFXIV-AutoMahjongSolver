// Scan AgentEmj bytes (memdump v=2 schema's `agent_b64`) for offsets that
// rotate {0,1,2,3} across hand boundaries — DealerSeat candidate — and for
// byte offsets that flip 0→1 around the riichi-declaration moment — OurRiichi.
//
// Usage:
//   node tools/scan-agent-offsets.mjs <memdump-dir>
//
// Outputs:
//   - Top DealerSeat candidates: byte/int32 offsets in agent_b64 that take
//     all of {0,1,2,3} across multiple hand boundaries.
//   - Riichi-declaration candidates: any input-pre / input-post pair whose
//     agent_b64 differs at a byte position that goes 0→1.

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const dir = process.argv[2];
if (!dir) {
  console.error("Usage: node tools/scan-agent-offsets.mjs <memdump-dir>");
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
console.log(`loaded ${allRecords.length} memdump records`);

const v2 = allRecords.filter((r) => r.v === 2 && typeof r.agent_b64 === "string" && r.agent_b64.length > 0);
console.log(`v=2 with agent_b64: ${v2.length}`);
if (v2.length === 0) {
  console.log("No v=2 agent_b64 records — scanner can't run. Need a corpus from an install on a recent plugin build.");
  process.exit(0);
}

// Materialize agent bytes
for (const r of v2) {
  r.agent = Buffer.from(r.agent_b64, "base64");
  if (typeof r.addon_b64 === "string") r.addon = Buffer.from(r.addon_b64, "base64");
}
v2.sort((a, b) => (a.seq ?? 0) - (b.seq ?? 0));
const agentLen = v2[0].agent.length;
console.log(`agent buf length: ${agentLen} (0x${agentLen.toString(16)})`);

// Compute hand boundaries using addon wall (sum of discard-count bytes).
function wallEstimate(addonBuf) {
  if (!addonBuf || addonBuf.length < 0x0E00) return -1;
  let total = 0;
  for (const off of TOTAL_DC_OFFSETS) total += addonBuf[off];
  return 70 - total;
}
const withWall = v2.filter((r) => r.addon).map((r) => ({ ...r, wall: wallEstimate(r.addon) }));
const boundaries = [];
for (let i = 1; i < withWall.length; i++) {
  if (withWall[i].wall - withWall[i - 1].wall >= 10) boundaries.push(i);
}
console.log(`hand boundaries (wall jumps ≥10): ${boundaries.length}`);

if (boundaries.length >= 2) {
  // For each int32-aligned offset in agent, sample 5 ticks past each boundary
  // and look for values in {0..3} that vary across boundaries.
  console.log("\n## DealerSeat candidates (int32 offsets in agent_b64, values {0..3}, ≥3 distinct across boundaries)");
  const intCandidates = [];
  for (let off = 0; off < agentLen - 4; off += 4) {
    const vals = [];
    for (const b of boundaries) {
      const idx = Math.min(b + 5, withWall.length - 1);
      vals.push(withWall[idx].agent.readInt32LE(off));
    }
    if (!vals.every((v) => v >= 0 && v <= 3)) continue;
    const distinct = new Set(vals);
    if (distinct.size < 3) continue;
    intCandidates.push({ offset: off, vals, distinct: distinct.size });
  }
  intCandidates.sort((a, b) => b.distinct - a.distinct);
  console.log(`  found ${intCandidates.length} candidates`);
  for (const c of intCandidates.slice(0, 20)) {
    console.log(`  +0x${c.offset.toString(16).padStart(4, "0")}  distinct=${c.distinct}  vals=[${c.vals.slice(0, 12).join(",")}]`);
  }

  console.log("\n## DealerSeat candidates (single-byte offsets)");
  const byteCandidates = [];
  for (let off = 0; off < agentLen; off++) {
    const vals = [];
    for (const b of boundaries) {
      const idx = Math.min(b + 5, withWall.length - 1);
      vals.push(withWall[idx].agent[off]);
    }
    if (!vals.every((v) => v >= 0 && v <= 3)) continue;
    const distinct = new Set(vals);
    if (distinct.size < 3) continue;
    byteCandidates.push({ offset: off, vals, distinct: distinct.size });
  }
  byteCandidates.sort((a, b) => b.distinct - a.distinct);
  console.log(`  found ${byteCandidates.length} candidates`);
  for (const c of byteCandidates.slice(0, 20)) {
    console.log(`  +0x${c.offset.toString(16).padStart(4, "0")}  distinct=${c.distinct}  vals=[${c.vals.slice(0, 12).join(",")}]`);
  }
}

// Riichi declaration: find input-pre/input-post pairs near each other and
// see if any byte in agent_b64 flipped 0→1.
console.log("\n## OurRiichi candidates (bytes that flipped 0→1 across input-pre / input-post pair)");
const pre = v2.filter((r) => r.reason === "input-pre");
const post = v2.filter((r) => r.reason === "input-post");
console.log(`  pre=${pre.length}  post=${post.length}`);

const flipCounts = new Map();
let pairs = 0;
for (const p of pre) {
  // Find the post with seq closest after p.seq
  const next = post.find((q) => q.seq > p.seq && q.seq < p.seq + 5);
  if (!next) continue;
  if (p.agent.length !== next.agent.length) continue;
  pairs++;
  for (let off = 0; off < p.agent.length; off++) {
    if (p.agent[off] === 0 && next.agent[off] === 1) {
      flipCounts.set(off, (flipCounts.get(off) ?? 0) + 1);
    }
  }
}
console.log(`  paired input events: ${pairs}`);
console.log(`  bytes with at least one 0→1 flip across paired events:`);
const flips = [...flipCounts.entries()].sort((a, b) => b[1] - a[1]);
for (const [off, c] of flips.slice(0, 30)) {
  console.log(`  +0x${off.toString(16).padStart(4, "0")}  flipped 0→1 in ${c}/${pairs} pairs`);
}

console.log("\nDone.");

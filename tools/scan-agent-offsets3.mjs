// Cross-reference real hand boundaries from the games NDJSON against agent_b64
// values at those moments. Avoids the noisy "wall-jump in memdump corpus"
// boundaries (which include spurious mid-deal rolls fixed in v0.1.0.10).

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const memdumpDir = process.argv[2];
const gamesDir = process.argv[3];
if (!memdumpDir || !gamesDir) {
  console.error("Usage: node tools/scan-agent-offsets3.mjs <memdumpDir> <gamesDir>");
  process.exit(2);
}

async function readNdjson(path) {
  const raw = await readFile(path);
  const body = (raw[0] === 0x1f && raw[1] === 0x8b) ? gunzipSync(raw) : raw;
  return body.toString("utf8").split("\n").filter(Boolean)
    .map((l) => { try { return JSON.parse(l); } catch { return null; } })
    .filter(Boolean);
}
async function loadAll(d) {
  const all = [];
  try {
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
  } catch {}
  return all;
}

// Memdumps with agent buffer
const memdumps = (await loadAll(memdumpDir))
  .filter((r) => r.v === 2 && typeof r.agent_b64 === "string")
  .map((r) => ({ ...r, agent: Buffer.from(r.agent_b64, "base64"), t: new Date(r.t).getTime() }))
  .sort((a, b) => a.t - b.t);
console.log(`v=2 memdumps: ${memdumps.length}`);
if (memdumps.length === 0) process.exit(0);

// Games — collect hand-start timestamps (one per file's first event).
const games = await loadAll(gamesDir);
const handStarts = games
  .filter((e) => e.e === "hand-start")
  .map((e) => new Date(e.t).getTime())
  .sort((a, b) => a - b);
console.log(`hand-start events: ${handStarts.length}`);
if (handStarts.length < 4) {
  console.log("Need ≥4 hand-starts for rotation analysis. Skipping seat scan.");
}

// For each hand-start, find the memdump nearest in time (within 30 sec after).
const samples = [];
for (const ts of handStarts) {
  const candidate = memdumps.find((m) => m.t >= ts + 1000 && m.t <= ts + 30000);
  if (candidate) samples.push({ ts, m: candidate });
}
console.log(`memdump samples aligned with hand-start events: ${samples.length}`);

if (samples.length >= 4) {
  const agentLen = samples[0].m.agent.length;

  // Look for byte offsets where values are in {0,1,2,3} AND we see ≥3 distinct values.
  console.log("\n## Bytes in agent_b64 with values {0..3}, ≥3 distinct across hand-start samples:");
  const candidates = [];
  for (let off = 0; off < agentLen; off++) {
    const vals = samples.map((s) => s.m.agent[off]);
    if (!vals.every((v) => v >= 0 && v <= 3)) continue;
    const distinct = new Set(vals);
    if (distinct.size < 3) continue;
    candidates.push({ offset: off, distinct: distinct.size, vals });
  }
  // Sort by: most distinct first, then by whether values look like they cycle.
  candidates.sort((a, b) => b.distinct - a.distinct);
  console.log(`  ${candidates.length} candidates:`);
  for (const c of candidates.slice(0, 30)) {
    console.log(`  +0x${c.offset.toString(16).padStart(4, "0")}  distinct=${c.distinct}  vals=[${c.vals.slice(0, 16).join(",")}]`);
  }

  console.log("\n## Int32 in agent_b64 with values {0..3}, ≥3 distinct across hand-start samples:");
  const intCandidates = [];
  for (let off = 0; off < agentLen - 4; off += 4) {
    const vals = samples.map((s) => s.m.agent.readInt32LE(off));
    if (!vals.every((v) => v >= 0 && v <= 3)) continue;
    const distinct = new Set(vals);
    if (distinct.size < 3) continue;
    intCandidates.push({ offset: off, distinct: distinct.size, vals });
  }
  intCandidates.sort((a, b) => b.distinct - a.distinct);
  console.log(`  ${intCandidates.length} candidates:`);
  for (const c of intCandidates.slice(0, 30)) {
    console.log(`  +0x${c.offset.toString(16).padStart(4, "0")}  distinct=${c.distinct}  vals=[${c.vals.slice(0, 16).join(",")}]`);
  }
}

console.log("\nDone.");

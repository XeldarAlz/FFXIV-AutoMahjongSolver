// Cross-install scan. For each install with v=2 agent_b64 memdumps,
// take ONE representative sample (mid-session memdump). Compare the
// value at each agent offset across installs. The DealerSeat offset
// should show {0,1,2,3} distribution since different installs are
// at different points in different matches.

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const root = process.argv[2] ?? ".local/by-date";

async function readNdjson(path) {
  const raw = await readFile(path);
  const body = (raw[0] === 0x1f && raw[1] === 0x8b) ? gunzipSync(raw) : raw;
  return body.toString("utf8").split("\n").filter(Boolean)
    .map((l) => { try { return JSON.parse(l); } catch { return null; } })
    .filter(Boolean);
}

async function* walk(dir) {
  for (const e of await readdir(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else if (e.isFile() && e.name.endsWith(".ndjson.gz")) yield p;
  }
}

// Find one representative agent_b64 per install. Prefer state-change records
// in the middle of the file (mid-session, not at the very start).
const byInstall = new Map();
for await (const f of walk(root)) {
  if (!f.includes("memdumps")) continue;
  const install = f.match(/memdumps[\\\/]([^\\\/]+)[\\\/]/)?.[1] ?? "?";
  if (byInstall.has(install)) continue;
  try {
    const records = await readNdjson(f);
    const v2 = records.filter((r) => r.v === 2 && typeof r.agent_b64 === "string" && r.reason === "state-change");
    if (v2.length === 0) continue;
    // Pick the middle record
    const sample = v2[Math.floor(v2.length / 2)];
    byInstall.set(install, Buffer.from(sample.agent_b64, "base64"));
  } catch { /* skip */ }
}

const installs = [...byInstall.entries()];
console.log(`installs with v=2 agent_b64 sample: ${installs.length}`);
if (installs.length < 4) {
  console.log("Need ≥4 installs to see variation. Skipping.");
  process.exit(0);
}

const agentLen = installs[0][1].length;
console.log(`agent buf length: ${agentLen}\n`);

// For each byte offset, gather values across installs. Look for offsets
// where the distribution spans {0,1,2,3}.
console.log("## Byte offsets with values {0..3} and ≥3 distinct across installs:");
const byteCandidates = [];
for (let off = 0; off < agentLen; off++) {
  const vals = installs.map(([_, buf]) => buf[off]);
  if (!vals.every((v) => v >= 0 && v <= 3)) continue;
  const distinct = new Set(vals);
  if (distinct.size < 3) continue;
  byteCandidates.push({ offset: off, distinct: distinct.size, vals });
}
byteCandidates.sort((a, b) => b.distinct - a.distinct);
console.log(`  ${byteCandidates.length} candidates:`);
for (const c of byteCandidates.slice(0, 30)) {
  const list = c.vals.map((v, i) => `${installs[i][0].slice(0, 8)}=${v}`).join(" ");
  console.log(`  +0x${c.offset.toString(16).padStart(4, "0")}  distinct=${c.distinct}  ${list}`);
}

console.log("\n## Int32 offsets with values {0..3} and ≥3 distinct across installs:");
const intCandidates = [];
for (let off = 0; off < agentLen - 4; off += 4) {
  const vals = installs.map(([_, buf]) => buf.readInt32LE(off));
  if (!vals.every((v) => v >= 0 && v <= 3)) continue;
  const distinct = new Set(vals);
  if (distinct.size < 3) continue;
  intCandidates.push({ offset: off, distinct: distinct.size, vals });
}
intCandidates.sort((a, b) => b.distinct - a.distinct);
console.log(`  ${intCandidates.length} candidates:`);
for (const c of intCandidates.slice(0, 30)) {
  const list = c.vals.map((v, i) => `${installs[i][0].slice(0, 8)}=${v}`).join(" ");
  console.log(`  +0x${c.offset.toString(16).padStart(4, "0")}  distinct=${c.distinct}  ${list}`);
}

// Empirical discovery of the per-seat discard-array offsets inside the
// AgentEmj buffer. Same +1-count-transition methodology as
// find-discard-offset.mjs, but scans `agent_b64` (added in
// MemoryDumpRecorder schema v2) and makes no per-seat-block assumption —
// the agent layout is unknown, so we sweep the full buffer for each
// seat's transition and look for the byte/word position that mutates in
// lockstep with each seat's count increment.
//
// Usage:
//   node tools/find-discard-offset-agent.mjs <memdump-dir>
//
// Per seat, the analyzer:
//   1. Collects every clean +1 transition.
//   2. For each transition, lists every byte position in the agent buffer
//      that changed.
//   3. Aggregates per-byte change frequency across the seat's transitions.
//      A genuine discard-array slot mutates on every transition for the
//      matching oldCount value, but ALL slots mutate over the course of a
//      hand. The slot for tile-index N specifically only changes on
//      transitions with oldCount=N — so a byte that mutates on exactly K
//      different oldCount values is a strong candidate for a tile-slot at
//      whichever oldCount it changed on.
//   4. Fits a (start, stride) line where start = agent offset of slot[0]
//      and stride = bytes per tile (typically 1, 2, or 4).
//   5. Emits a Markdown report + JSON patch (in the same shape as
//      find-discard-offset.mjs's report).
//
// Schema gate: skips records lacking `agent_b64` so it's safe to run
// against mixed v1/v2 corpora — pre-v2 records are filtered out, post-v2
// records carry the field.

import { readdir, readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const memdumpDir = process.argv[2];
if (!memdumpDir) {
  console.error("Usage: node tools/find-discard-offset-agent.mjs <memdump-dir>");
  process.exit(2);
}

const SEAT_COUNT_BYTES = [
  { name: "self",     countByte: 0x04FE },
  { name: "shimocha", countByte: 0x07DE },
  { name: "toimen",   countByte: 0x0ABE },
  { name: "kamicha",  countByte: 0x0D9E },
];
const TILE_TEXTURE_BASE = 76041;

async function readNdjson(path) {
  const raw = await readFile(path);
  const body = (raw[0] === 0x1f && raw[1] === 0x8b) ? gunzipSync(raw) : raw;
  return body.toString("utf8")
    .split("\n").filter(Boolean)
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

console.log(`[find-discard-offset-agent] scanning ${memdumpDir}`);
const records = await loadAll(memdumpDir);
console.log(`[find-discard-offset-agent] ${records.length} records loaded`);

// Decode both buffers up-front. We need the addon to read count bytes and
// the agent to scan for discard arrays.
for (const r of records) {
  if (typeof r.addon_b64 === "string") {
    try { r.__addon = Buffer.from(r.addon_b64, "base64"); }
    catch { r.__addon = null; }
  }
  if (typeof r.agent_b64 === "string") {
    try { r.__agent = Buffer.from(r.agent_b64, "base64"); }
    catch { r.__agent = null; }
  }
}
const usable = records
  .filter((r) => r.__addon && r.__addon.length >= 0x107E
    && r.__agent && r.__agent.length > 0
    && typeof r.seq === "number")
  .sort((a, b) => a.seq - b.seq);
console.log(`[find-discard-offset-agent] ${usable.length} usable (have addon ≥ 0x107E + agent_b64 + seq)`);
if (usable.length < 2) {
  console.error("[find-discard-offset-agent] need ≥2 records — collect more memdumps after deploying v2 schema");
  process.exit(1);
}

// Find clean single-seat +1 transitions where the agent buffer length is
// stable (mismatched lengths usually mean the agent pointer churned mid-
// session and we can't safely diff).
const seatCount = (rec, i) => rec.__addon[SEAT_COUNT_BYTES[i].countByte];

const transitions = [];
let pairs = 0;
for (let i = 1; i < usable.length; i++) {
  const a = usable[i - 1], b = usable[i];
  if (a.__agent.length !== b.__agent.length) continue;
  pairs++;
  let only = -1;
  let bad = false;
  for (let s = 0; s < 4; s++) {
    const da = seatCount(a, s), db = seatCount(b, s);
    if (db === da + 1) { if (only >= 0) { bad = true; break; } only = s; }
    else if (db !== da) { bad = true; break; }
  }
  if (bad || only < 0) continue;
  transitions.push({
    seatIndex: only,
    oldCount: seatCount(a, only),
    prev: a, next: b,
    tight: a.reason === "input-pre" && b.reason === "input-post",
  });
}
console.log(`[find-discard-offset-agent] ${pairs} adjacent pairs, ${transitions.length} clean +1 transitions`);

if (transitions.length === 0) {
  console.error("no clean transitions — capture more sessions");
  process.exit(1);
}

// Background noise: bytes that mutate in ≥35% of all adjacent pairs are
// UI/animation churn and excluded from candidate ranking.
const agentLen = usable[0].__agent.length;
const bgChanges = new Uint32Array(agentLen);
for (let i = 1; i < usable.length; i++) {
  const A = usable[i - 1].__agent, B = usable[i].__agent;
  if (A.length !== B.length) continue;
  for (let off = 0; off < A.length; off++)
    if (A[off] !== B[off]) bgChanges[off]++;
}
const NOISE_THRESHOLD = 0.35;
const isNoisy = (off) => pairs > 0 && bgChanges[off] / pairs >= NOISE_THRESHOLD;

// Per-seat, per-transition deltaOffsets in the agent buffer.
const perSeat = SEAT_COUNT_BYTES.map(() => ({ transitions: [] }));
for (const tr of transitions) {
  const A = tr.prev.__agent, B = tr.next.__agent;
  const deltas = [];
  for (let off = 0; off < A.length; off++) {
    if (isNoisy(off)) continue;
    if (A[off] !== B[off]) deltas.push(off);
  }
  perSeat[tr.seatIndex].transitions.push({
    oldCount: tr.oldCount, tight: tr.tight, deltaOffsets: deltas,
  });
}

// Fit (start, stride): for each candidate stride ∈ {1, 2, 4} and each
// observed deltaOffset as a candidate start, count how many transitions
// have a delta inside the predicted slot [start+oldCount*stride,
// start+(oldCount+1)*stride). Same scoring as find-discard-offset.mjs:
// strict (every byte in slot mutated) > loose (any byte in slot mutated).
function fit(transitionsForSeat) {
  if (transitionsForSeat.length === 0) return [];
  const startCands = new Set();
  for (const tr of transitionsForSeat)
    for (const off of tr.deltaOffsets) startCands.add(off);
  if (startCands.size === 0) return [];

  const out = [];
  for (const stride of [1, 2, 4]) {
    for (const startAbs of startCands) {
      const maxOC = Math.max(...transitionsForSeat.map((t) => t.oldCount));
      if (startAbs + (maxOC + 1) * stride > agentLen) continue;
      let hits = 0, strict = 0, tightStrict = 0, tightN = 0;
      for (const tr of transitionsForSeat) {
        const slot0 = startAbs + tr.oldCount * stride;
        let inWin = 0;
        for (const off of tr.deltaOffsets)
          if (off >= slot0 && off < slot0 + stride) inWin++;
        if (inWin > 0) hits++;
        if (inWin === stride) strict++;
        if (tr.tight) { tightN++; if (inWin === stride) tightStrict++; }
      }
      out.push({
        startAbs, stride,
        n: transitionsForSeat.length,
        hits, strict, tightStrict, tightN,
        hitRate: hits / transitionsForSeat.length,
        strictRate: strict / transitionsForSeat.length,
        tightStrictRate: tightN > 0 ? tightStrict / tightN : null,
      });
    }
  }
  out.sort((a, b) => {
    const aT = a.tightStrictRate ?? -1, bT = b.tightStrictRate ?? -1;
    if (aT !== bT) return bT - aT;
    if (a.strictRate !== b.strictRate) return b.strictRate - a.strictRate;
    if (a.hitRate !== b.hitRate) return b.hitRate - a.hitRate;
    return a.stride - b.stride;
  });
  return out;
}

function decode(buf, off, stride) {
  if (stride === 1) {
    const v = buf[off];
    return { raw: v, asTileId: v < 34 ? v : null, asTextureRel: null };
  }
  if (stride === 2) {
    if (off + 2 > buf.length) return null;
    const v = buf.readUInt16LE(off);
    return { raw: v, asTileId: v < 34 ? v : null, asTextureRel: null };
  }
  if (stride === 4) {
    if (off + 4 > buf.length) return null;
    const v = buf.readInt32LE(off);
    const rel = v - TILE_TEXTURE_BASE;
    return { raw: v, asTileId: null, asTextureRel: rel >= 0 && rel < 256 ? rel : null };
  }
  return null;
}

const fmt = (n, w = 4) => "0x" + n.toString(16).padStart(w, "0");

const verdicts = SEAT_COUNT_BYTES.map((seat, s) => {
  const trs = perSeat[s].transitions;
  if (trs.length < 2) return { seat: seat.name, note: `only ${trs.length} transitions` };
  const candidates = fit(trs).slice(0, 5);
  return { seat: seat.name, transitions: trs.length, top: candidates };
});

const last = usable[usable.length - 1];
const lastAgent = last.__agent;

const md = [];
md.push(`# Discard-Array Offset Discovery — AGENT scan`);
md.push(``);
md.push(`Generated ${new Date().toISOString()} from \`${memdumpDir}\``);
md.push(``);
md.push(`- Source field: \`agent_b64\` (MemoryDumpRecorder v2)`);
md.push(`- Agent buffer length: ${agentLen} bytes`);
md.push(`- Records loaded: ${records.length}, usable (have agent + addon + seq): ${usable.length}`);
md.push(`- Adjacent pairs: ${pairs}, clean +1 transitions: ${transitions.length}`);
md.push(`- Tight (input-pre → input-post) brackets: ${transitions.filter((t) => t.tight).length}`);
md.push(`- Noise threshold: ${(NOISE_THRESHOLD * 100).toFixed(0)}% (mutating in ≥ this fraction of pairs)`);
md.push(``);
md.push(`## Per-seat fit (agent offsets)`);
md.push(``);
for (let s = 0; s < 4; s++) {
  const v = verdicts[s];
  md.push(`### ${v.seat}`);
  if (v.note) { md.push(`- ${v.note}`); md.push(``); continue; }
  md.push(`- transitions: ${v.transitions}`);
  if (v.top.length === 0) { md.push(`- no fit`); md.push(``); continue; }
  md.push(``);
  md.push(`| rank | start | stride | strict% | loose% | tight strict% | n (tight n) |`);
  md.push(`|---|---|---|---|---|---|---|`);
  for (let i = 0; i < v.top.length; i++) {
    const c = v.top[i];
    const tt = c.tightStrictRate === null ? "—" : (c.tightStrictRate * 100).toFixed(0) + "%";
    md.push(`| ${i + 1} | \`${fmt(c.startAbs)}\` | ${c.stride} | ${(c.strictRate * 100).toFixed(0)}% | ${(c.hitRate * 100).toFixed(0)}% | ${tt} | ${c.n} (${c.tightN}) |`);
  }
  md.push(``);
  const best = v.top[0];
  const dcNow = lastAgent.length > SEAT_COUNT_BYTES[s].countByte
    ? last.__addon[SEAT_COUNT_BYTES[s].countByte]
    : 0;
  md.push(`Sample decode under best fit (start=\`${fmt(best.startAbs)}\`, stride=${best.stride}, dc=${dcNow}):`);
  md.push("```");
  if (dcNow > 0 && dcNow <= 30) {
    for (let i = 0; i < dcNow; i++) {
      const off = best.startAbs + i * best.stride;
      const dec = decode(lastAgent, off, best.stride);
      if (!dec) continue;
      const tileId = dec.asTileId ?? dec.asTextureRel;
      md.push(`  i=${i.toString().padStart(2)}  off=${fmt(off)}  raw=${dec.raw}  tile_id=${tileId ?? "?"}`);
    }
  } else {
    md.push(`  dc=${dcNow} — no decode`);
  }
  md.push("```");
  md.push(``);
}

const outMd = join(memdumpDir, "_discard-offset-agent-fit.md");
const outJson = join(memdumpDir, "_discard-offset-agent-fit.json");
await writeFile(outMd, md.join("\n"), "utf8");
await writeFile(outJson, JSON.stringify({
  memdumpDir, source: "agent_b64",
  records: records.length, usable: usable.length, pairs,
  cleanTransitions: transitions.length, verdicts,
}, null, 2), "utf8");
console.log(`[find-discard-offset-agent] wrote ${outMd}`);
console.log(`[find-discard-offset-agent] wrote ${outJson}`);
console.log("");
console.log("=== Summary ===");
for (const v of verdicts) {
  if (v.note) { console.log(`  ${v.seat.padEnd(9)}  ${v.note}`); continue; }
  if (!v.top || v.top.length === 0) { console.log(`  ${v.seat.padEnd(9)}  no fit (n=${v.transitions})`); continue; }
  const c = v.top[0];
  console.log(`  ${v.seat.padEnd(9)}  start=${fmt(c.startAbs)}  stride=${c.stride}  strict=${(c.strictRate * 100).toFixed(0)}%  n=${c.n}`);
}

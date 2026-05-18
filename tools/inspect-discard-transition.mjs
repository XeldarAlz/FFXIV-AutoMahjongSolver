// Pick one true single-seat +1 discard transition and dump every byte in
// the addon that changed between (prev, next). No noise filtering, no
// "inside the seat block" gate — shows where the discard data ACTUALLY
// lives when find-discard-offset.mjs comes up empty.
//
// Usage: node tools/inspect-discard-transition.mjs <memdump-dir> [seatName]
//   seatName ∈ self|shimocha|toimen|kamicha, default self.

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const dir = process.argv[2];
const seatName = process.argv[3] ?? "self";
if (!dir) {
  console.error("Usage: node tools/inspect-discard-transition.mjs <dir> [self|shimocha|toimen|kamicha]");
  process.exit(2);
}

const SEATS = [
  { name: "self",     countByte: 0x04FE },
  { name: "shimocha", countByte: 0x07DE },
  { name: "toimen",   countByte: 0x0ABE },
  { name: "kamicha",  countByte: 0x0D9E },
];
const seatIdx = SEATS.findIndex((s) => s.name === seatName);
if (seatIdx < 0) { console.error("bad seat"); process.exit(2); }

async function readNdjson(p) {
  const raw = await readFile(p);
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
  .sort((a, b) => a.seq - b.seq);

console.log(`loaded ${records.length} records`);

// Find clean single-seat +1 transitions for the requested seat where the
// OTHER three seats and the global frame don't move much. We then pick one
// with a small overall diff (less likely to be a hand-roll).
const seatCount = (rec, i) => rec.buf[SEATS[i].countByte];

const candidates = [];
for (let i = 1; i < records.length; i++) {
  const a = records[i - 1], b = records[i];
  if (a.buf.length !== b.buf.length) continue;
  const ca = SEATS.map((_, s) => seatCount(a, s));
  const cb = SEATS.map((_, s) => seatCount(b, s));
  let only = -1;
  let ok = true;
  for (let s = 0; s < 4; s++) {
    if (cb[s] === ca[s] + 1) {
      if (only >= 0) { ok = false; break; }
      only = s;
    } else if (cb[s] !== ca[s]) { ok = false; break; }
  }
  if (!ok || only !== seatIdx) continue;
  // Count total byte diff
  let diffs = 0;
  for (let off = 0; off < a.buf.length; off++)
    if (a.buf[off] !== b.buf[off]) diffs++;
  candidates.push({ i, oldCount: ca[only], newCount: cb[only], diffs, a, b });
}

candidates.sort((x, y) => x.diffs - y.diffs);
console.log(`${candidates.length} ${seatName} +1 transitions, smallest diff = ${candidates[0]?.diffs}`);

if (candidates.length === 0) process.exit(0);

// Pick the 3 cleanest transitions and report their changed byte offsets.
const fmt = (n) => "0x" + n.toString(16).padStart(4, "0");

const allChangedOffsets = new Map(); // off → count among the cleanest 3
for (const c of candidates.slice(0, 3)) {
  const changed = [];
  for (let off = 0; off < c.a.buf.length; off++)
    if (c.a.buf[off] !== c.b.buf[off]) {
      changed.push({ off, was: c.a.buf[off], now: c.b.buf[off] });
      allChangedOffsets.set(off, (allChangedOffsets.get(off) ?? 0) + 1);
    }
  console.log(`\n=== transition oldCount=${c.oldCount} (rec #${c.i}), total ${changed.length} bytes ===`);
  // Show only first 80 to keep output readable
  for (const ch of changed.slice(0, 80)) {
    console.log(`  ${fmt(ch.off)}: ${ch.was.toString(16).padStart(2,"0")} → ${ch.now.toString(16).padStart(2,"0")}`);
  }
  if (changed.length > 80) console.log(`  ... +${changed.length - 80} more`);
}

// Now: which offsets changed in ALL THREE of the cleanest transitions?
const stable = [...allChangedOffsets.entries()]
  .filter(([, n]) => n === 3)
  .map(([off]) => off)
  .sort((a, b) => a - b);
console.log(`\nBytes changing in ALL 3 cleanest transitions: ${stable.length}`);
for (const off of stable) console.log(`  ${fmt(off)}`);

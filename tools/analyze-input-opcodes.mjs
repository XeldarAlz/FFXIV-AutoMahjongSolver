// Categorize all FireCallback invocations in the inputs corpus by their
// (values[0], values.length) tuple. The InputDispatcher's speculative
// opcodes (Riichi=8, Tsumo=9, Ron=10, Kan=12) are marked unconfirmed —
// if the inputs corpus shows them being fired in production AND the
// surrounding context indicates a real agari, they're confirmed.
//
// The inputs stream captures every FireCallback that hits the Mahjong
// addon (BeforeFireCallback hook in InputEventLogger). Records:
//   { t, addon, count, values: [v0, v1?] }
// `count` is the AtkValue array length passed to FireCallback. Values
// are decoded as ints (other types render as the strings).
//
// Usage:
//   node tools/analyze-input-opcodes.mjs <inputs-root>
//
// Where inputs-root has the b2-all/inputs/<install>/inputs-*.gz layout
// or by-date/<date>/inputs/<install>/inputs-*.gz.

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const root = process.argv[2];
if (!root) {
  console.error("Usage: node tools/analyze-input-opcodes.mjs <inputs-root>");
  process.exit(2);
}

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
      else if (e.isFile() && e.name.endsWith(".gz")) {
        for (const r of await readNdjson(p)) all.push({ ...r, __file: p });
      }
    }
  }
  return all;
}

const records = await loadAll(root);
console.log(`loaded ${records.length} input records`);

// Bucket by (opcode v0, count). Skip non-int v0.
const byOpcode = new Map();
for (const r of records) {
  if (!Array.isArray(r.values) || r.values.length === 0) continue;
  const v0 = r.values[0];
  if (typeof v0 !== "number") continue;
  const key = `${v0}|${r.count ?? r.values.length}`;
  if (!byOpcode.has(key)) byOpcode.set(key, []);
  byOpcode.get(key).push(r);
}

const sorted = [...byOpcode.entries()]
  .sort((a, b) => b[1].length - a[1].length);

console.log(`\nOpcode distribution (top 30):`);
console.log("opcode | count | n      | sample v1 values");
for (const [key, recs] of sorted.slice(0, 30)) {
  const [opcode, count] = key.split("|");
  const v1Distinct = new Set();
  for (const r of recs) {
    if (r.values.length > 1) v1Distinct.add(JSON.stringify(r.values[1]));
  }
  const v1Sample = [...v1Distinct].slice(0, 5).join(", ");
  console.log(`  ${opcode.padStart(3)}  |   ${count}   | ${recs.length.toString().padStart(6)} | ${v1Sample.slice(0, 90)}`);
}

// Specifically inspect the speculative opcodes
const speculative = { 8: "Riichi?", 9: "Tsumo?", 10: "Ron?", 12: "Kan?" };
console.log(`\n=== Speculative opcode inspection ===`);
for (const [op, label] of Object.entries(speculative)) {
  const opInt = parseInt(op);
  const recs = records.filter((r) => Array.isArray(r.values) && r.values[0] === opInt);
  if (recs.length === 0) {
    console.log(`\nopcode=${op} (${label}): 0 records — NOT observed in corpus`);
    continue;
  }
  console.log(`\nopcode=${op} (${label}): ${recs.length} records`);
  // Sample 5 records with full context. Filename is what we keyed on,
  // so split on both forward and back slash.
  const installs = new Set(recs.map((r) => {
    const parts = r.__file.split(/[\\/]/);
    const idx = parts.indexOf("inputs");
    return idx >= 0 && idx + 1 < parts.length ? parts[idx + 1] : null;
  }).filter(Boolean));
  console.log(`  installs: ${installs.size} distinct`);
  for (const inst of installs) {
    const n = recs.filter((r) => r.__file.includes(inst)).length;
    console.log(`    ${inst}: ${n} records`);
  }
  console.log(`  sample (first 5):`);
  for (const r of recs.slice(0, 5)) {
    const valStr = (r.values ?? []).map((v) => JSON.stringify(v)).join(",");
    console.log(`    t=${r.t}  count=${r.count}  values=[${valStr}]  result=${r.result ?? "?"}`);
  }
}

// Cross-reference the games stream's action records (riichi / tsumo / ron /
// kan declarations) against the inputs stream's FireCallback captures
// within the same time window. Looks for the opcode that fires within
// ±2 seconds of each action — that's the FireCallback the plugin (or
// game) used to dispatch the action.
//
// Used to confirm or refute the speculative opcodes Riichi=8, Ron=10,
// Kan=12 in InputDispatcher. Tsumo=9 was already confirmed via
// analyze-input-opcodes.mjs's install distribution.
//
// Usage: node tools/cross-ref-action-opcodes.mjs <b2-all-dir>
//
// Where b2-all-dir contains games/<install>/<date>/*.gz and
// inputs/<install>/<date>/*.gz subdirs.

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const root = process.argv[2];
if (!root) {
  console.error("Usage: node tools/cross-ref-action-opcodes.mjs <b2-all-dir>");
  process.exit(2);
}

async function readNdjson(p) {
  const raw = await readFile(p);
  const body = (raw[0] === 0x1f && raw[1] === 0x8b) ? gunzipSync(raw) : raw;
  return body.toString("utf8").split("\n").filter(Boolean)
    .map((l) => { try { return JSON.parse(l); } catch { return null; } })
    .filter(Boolean);
}

async function loadStream(streamName) {
  const all = [];
  const dir = join(root, streamName);
  try {
    const installs = await readdir(dir, { withFileTypes: true });
    for (const inst of installs) {
      if (!inst.isDirectory()) continue;
      const instDir = join(dir, inst.name);
      const stack = [instDir];
      while (stack.length) {
        const cur = stack.pop();
        for (const e of await readdir(cur, { withFileTypes: true })) {
          const p = join(cur, e.name);
          if (e.isDirectory()) stack.push(p);
          else if (e.isFile() && e.name.endsWith(".gz")) {
            for (const r of await readNdjson(p)) all.push({ ...r, __install: inst.name });
          }
        }
      }
    }
  } catch (e) {
    console.error(`could not load ${streamName}: ${e.message}`);
  }
  return all;
}

const games = await loadStream("games");
const inputs = await loadStream("inputs");
console.log(`games: ${games.length} records, inputs: ${inputs.length} records`);

// Find action records of interest in games stream
const ACTIONS = ["riichi", "tsumo", "ron", "ankan", "minkan", "shouminkan"];
const actionRecords = games.filter((r) =>
  r.e === "action" && r.kind && ACTIONS.includes(r.kind.toLowerCase()));
console.log(`action records (riichi/tsumo/ron/kan): ${actionRecords.length}`);

// Histogram by kind
const kindCounts = new Map();
for (const r of actionRecords) {
  const k = r.kind.toLowerCase();
  kindCounts.set(k, (kindCounts.get(k) ?? 0) + 1);
}
console.log("\nAction kind distribution:");
for (const [k, n] of kindCounts) console.log(`  ${k}: ${n}`);

// Index inputs by install + time bucket for fast lookup
const inputsByInstall = new Map();
for (const r of inputs) {
  if (!r.t || !Array.isArray(r.values)) continue;
  const inst = r.__install;
  if (!inputsByInstall.has(inst)) inputsByInstall.set(inst, []);
  inputsByInstall.get(inst).push({ t: Date.parse(r.t), values: r.values, count: r.count });
}
for (const list of inputsByInstall.values()) list.sort((a, b) => a.t - b.t);

console.log(`\n=== Per-action: opcodes seen within ±2000ms ===`);
const WINDOW_MS = 2000;
const opcodeByKind = new Map(); // kind → opcode → count

for (const a of actionRecords) {
  const inst = a.__install;
  const t = Date.parse(a.t);
  const candidates = inputsByInstall.get(inst) ?? [];
  // binary-search the range [t-WINDOW, t+WINDOW]
  const near = candidates.filter((i) => Math.abs(i.t - t) <= WINDOW_MS);
  for (const n of near) {
    const op = n.values[0];
    if (typeof op !== "number") continue;
    const k = a.kind.toLowerCase();
    if (!opcodeByKind.has(k)) opcodeByKind.set(k, new Map());
    const m = opcodeByKind.get(k);
    m.set(op, (m.get(op) ?? 0) + 1);
  }
}

for (const [kind, opmap] of opcodeByKind) {
  const sorted = [...opmap.entries()].sort((a, b) => b[1] - a[1]);
  const top = sorted.slice(0, 6).map(([op, c]) => `op=${op}:${c}`).join(", ");
  console.log(`  ${kind.padEnd(11)} | ${top}`);
}

console.log(`\nInterpretation:`);
console.log(`  An opcode appearing in N% of action windows for kind X is the strong`);
console.log(`  candidate for X's FireCallback opcode. Note opcode 11 often co-occurs`);
console.log(`  because the call-prompt accept fires opcode 11 right before the action`);
console.log(`  resolves, and opcode 7 for any same-tick discard.`);

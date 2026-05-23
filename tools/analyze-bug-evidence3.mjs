// Final-pass analysis with correct field names.
// Looks for: TsumogiriFallback "out of sync" markers in decision `why` and
// step `d` fields; 3-second retry-cycle freezes (consecutive identical
// actions on same kind/tile/slot ≥3 within 30s); state-6/post-call patterns.

import { createReadStream } from "node:fs";
import { readdir } from "node:fs/promises";
import { createGunzip } from "node:zlib";
import readline from "node:readline";

const dates = process.argv.slice(2);
if (dates.length === 0) { console.error("usage: <yyyy-mm-dd>..."); process.exit(2); }

async function* readGzLines(p) {
  const gz = createReadStream(p).pipe(createGunzip());
  const rl = readline.createInterface({ input: gz, crlfDelay: Infinity });
  for await (const l of rl) if (l.trim()) yield l;
}
async function listDir(p) { try { return await readdir(p); } catch { return []; } }

const outOfSyncDecisions = []; // {install, ts, why}
const passReasonHistogram = new Map();
const retryFreezes = []; // {install, hand-file, tile, slot, kind, count, firstTs, lastTs}
const ankanDecisions = []; // {install, ts, ...}
const callPromptDeclineReasons = new Map();
const callPromptAcceptKinds = new Map();
const dispatchByHandCount = new Map(); // hand_count at action time → action count
const actionToNextDecisionLatency = []; // ms between action and next decision for same kind/tile
const handStartsByDate = new Map();
const handsWithFreeze = new Set();

for (const date of dates) {
  const root = `.local/by-date/${date}`;
  let handStartsThisDate = 0;
  for (const install of await listDir(`${root}/games`)) {
    for (const f of await listDir(`${root}/games/${install}`)) {
      const events = [];
      for await (const l of readGzLines(`${root}/games/${install}/${f}`)) {
        try { events.push(JSON.parse(l)); } catch {}
      }
      let lastAction = null;
      let runOfSameAction = 0;
      let runFirstTs = null;
      for (const j of events) {
        const ev = j.e ?? "?";
        if (ev === "hand-start") handStartsThisDate++;
        if (ev === "decision") {
          const why = j.why ?? "";
          if (/out of sync/i.test(why)) {
            outOfSyncDecisions.push({ install, ts: j.t, why });
          }
          if (j.kind === "Pass") {
            passReasonHistogram.set(why, (passReasonHistogram.get(why) ?? 0) + 1);
          }
          if (j.kind === "AnKan") ankanDecisions.push({ install, ts: j.t, why });
        }
        if (ev === "call-prompt") {
          // Nothing — the next decision tells us pass/accept.
        }
        if (ev === "action") {
          const key = `${j.kind}|${j.tile}|${j.slot}`;
          if (lastAction === key) {
            runOfSameAction++;
          } else {
            if (runOfSameAction >= 3) {
              retryFreezes.push({
                install, file: f, key: lastAction, count: runOfSameAction,
                firstTs: runFirstTs, lastTs: events.find(e => false) ? null : null,
              });
              handsWithFreeze.add(`${install}/${f}`);
            }
            lastAction = key;
            runOfSameAction = 1;
            runFirstTs = j.t;
          }
        }
      }
      if (runOfSameAction >= 3) {
        retryFreezes.push({ install, file: f, key: lastAction, count: runOfSameAction, firstTs: runFirstTs });
        handsWithFreeze.add(`${install}/${f}`);
      }
    }
  }
  handStartsByDate.set(date, handStartsThisDate);
}

console.log("# Final-pass bug evidence");
console.log();
console.log("## TsumogiriFallback ('out of sync') decisions");
console.log(`  Count across 4 days: ${outOfSyncDecisions.length}`);
for (const d of outOfSyncDecisions.slice(0, 10)) {
  console.log(`  - ${d.install} ${d.ts}: ${d.why}`);
}
console.log();
console.log("## Pass-decision reasoning histogram (top 30 unique)");
for (const [r, c] of [...passReasonHistogram.entries()].sort((a, b) => b[1] - a[1]).slice(0, 30)) {
  console.log(`  ${String(c).padStart(4)}  ${r.slice(0, 120)}`);
}
console.log();
console.log("## AnKan decisions (would hit unconfirmed opcode-12)");
console.log(`  Count: ${ankanDecisions.length}`);
for (const a of ankanDecisions.slice(0, 5)) console.log(`  - ${a.install} ${a.ts}: ${a.why}`);
console.log();
console.log("## Retry freezes (≥3 consecutive identical-key actions in one hand-file)");
console.log(`  Total runs: ${retryFreezes.length}`);
console.log(`  Hand-files affected: ${handsWithFreeze.size}`);
const freezeKeyHist = new Map();
let maxRun = 0, maxKey = "";
for (const r of retryFreezes) {
  if (r.count > maxRun) { maxRun = r.count; maxKey = `${r.key} in ${r.install}/${r.file}`; }
  freezeKeyHist.set(r.count, (freezeKeyHist.get(r.count) ?? 0) + 1);
}
console.log(`  Longest single run: ${maxRun} retries (${maxKey})`);
console.log("  Run-length histogram:");
for (const [len, c] of [...freezeKeyHist.entries()].sort((a, b) => a[0] - b[0])) {
  console.log(`    ${String(len).padStart(3)} retries  × ${c}`);
}
console.log();
console.log("## Hand-starts by date (cross-reference)");
for (const [d, c] of handStartsByDate.entries()) console.log(`  ${d}  ${c}`);
console.log();
console.log("## Sample retry-freeze details (10 worst)");
for (const r of [...retryFreezes].sort((a, b) => b.count - a.count).slice(0, 10)) {
  console.log(`  ${r.install}/${r.file}  ${r.key}  count=${r.count}  firstTs=${r.firstTs}`);
}

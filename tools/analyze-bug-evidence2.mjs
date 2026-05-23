// Deeper drill: for each install, list per-install skip-reasons and inspect
// the games stream for evidence of the specific bugs.
// - per-install hand_state_paused reason × count
// - games-stream `decision` events with reasoning "out of sync" (TsumogiriFallback)
// - games-stream `action` events with their result string and the state they fired against
// - games-stream `call-prompt` events to track which prompts the bot acted on

import { createReadStream } from "node:fs";
import { readdir } from "node:fs/promises";
import { join } from "node:path";
import { createGunzip } from "node:zlib";
import readline from "node:readline";

const dates = process.argv.slice(2);
if (dates.length === 0) {
  console.error("Usage: node tools/analyze-bug-evidence2.mjs <yyyy-mm-dd> [...]");
  process.exit(2);
}
async function* readGzLines(p) {
  const gz = createReadStream(p).pipe(createGunzip());
  const rl = readline.createInterface({ input: gz, crlfDelay: Infinity });
  for await (const line of rl) if (line.trim()) yield line;
}
async function listDir(p) { try { return await readdir(p); } catch { return []; } }

const byInstall = new Map(); // install -> {pausedReasons, actions:[], decisions:[], states:[]}
const ensure = (i) => { if (!byInstall.has(i)) byInstall.set(i, {
  pausedReasons: new Map(), actions: [], decisions: [], stateSeqLen: 0, lastStateKey: null,
  longestStuck: { state: null, hand: null, run: 0 }, callPromptCount: 0, handStartCount: 0,
  outOfSyncDecisions: 0,
}); return byInstall.get(i); };

for (const date of dates) {
  const root = `.local/by-date/${date}`;
  // findings
  for (const install of await listDir(`${root}/findings`)) {
    const b = ensure(install);
    for (const f of await listDir(`${root}/findings/${install}`)) {
      for await (const line of readGzLines(`${root}/findings/${install}/${f}`)) {
        let j; try { j = JSON.parse(line); } catch { continue; }
        const kind = j.kind ?? "?";
        const data = j.data ?? {};
        if (kind === "hand_state_paused") {
          const r = data.reason ?? "?";
          b.pausedReasons.set(r, (b.pausedReasons.get(r) ?? 0) + 1);
        }
      }
    }
  }
  // games
  for (const install of await listDir(`${root}/games`)) {
    const b = ensure(install);
    for (const f of await listDir(`${root}/games/${install}`)) {
      for await (const line of readGzLines(`${root}/games/${install}/${f}`)) {
        let j; try { j = JSON.parse(line); } catch { continue; }
        const ev = j.e ?? "?";
        if (ev === "hand-start") b.handStartCount++;
        if (ev === "call-prompt") b.callPromptCount++;
        if (ev === "decision") {
          b.decisions.push({ k: j.kind, t: j.tile, r: j.reasoning, ts: j.t });
          if (j.reasoning && /out of sync/i.test(j.reasoning)) b.outOfSyncDecisions++;
        }
        if (ev === "action") b.actions.push({
          kind: j.kind, tile: j.tile, slot: j.slot, result: j.result, why: j.why, ts: j.t,
        });
        if (ev === "state") {
          const k = `${j.state}|${j.hand_count ?? j.hand}`;
          if (k === b.lastStateKey) {
            b.stateSeqLen++;
            if (b.stateSeqLen > b.longestStuck.run) {
              b.longestStuck = { state: j.state, hand: j.hand_count ?? j.hand, run: b.stateSeqLen };
            }
          } else {
            b.lastStateKey = k;
            b.stateSeqLen = 1;
          }
        }
      }
    }
  }
}

// Render
console.log("# Per-install skip reasons + action evidence\n");

// 1. Per-install paused reasons
console.log("## hand_state_paused — by install");
for (const [i, b] of [...byInstall.entries()].sort((a, b) => b[1].pausedReasons.size - a[1].pausedReasons.size)) {
  if (b.pausedReasons.size === 0) continue;
  console.log(`\n  ${i}:`);
  for (const [r, c] of [...b.pausedReasons.entries()].sort((a, b) => b[1] - a[1])) {
    console.log(`    ${String(c).padStart(3)}  ${r}`);
  }
}

// 2. Installs with actions but never hand-end: look at the actions
console.log("\n## Actions per install (kind histogram + dispatch result histogram)");
for (const [i, b] of [...byInstall.entries()].sort((a, b) => b[1].actions.length - a[1].actions.length)) {
  if (b.actions.length === 0) continue;
  const kindH = new Map(), resultH = new Map();
  for (const a of b.actions) {
    kindH.set(a.kind, (kindH.get(a.kind) ?? 0) + 1);
    resultH.set(a.result, (resultH.get(a.result) ?? 0) + 1);
  }
  console.log(`\n  ${i}  (${b.actions.length} actions, ${b.handStartCount} hand-starts, ${b.callPromptCount} call-prompts, longest-stuck=state${b.longestStuck.state}:hand${b.longestStuck.hand}×${b.longestStuck.run}):`);
  console.log(`    kind:    ${[...kindH.entries()].map(([k,c]) => `${k}=${c}`).join(", ")}`);
  console.log(`    result:  ${[...resultH.entries()].map(([k,c]) => `${k}=${c}`).join(", ")}`);
}

// 3. Decisions: out-of-sync count
console.log("\n## Decisions with 'out of sync' reasoning (TsumogiriFallback)");
let totalOutOfSync = 0;
for (const [i, b] of byInstall.entries()) totalOutOfSync += b.outOfSyncDecisions;
console.log(`  Total: ${totalOutOfSync}`);
for (const [i, b] of [...byInstall.entries()].sort((a, b) => b[1].outOfSyncDecisions - a[1].outOfSyncDecisions)) {
  if (b.outOfSyncDecisions === 0) continue;
  console.log(`  ${i}  ${b.outOfSyncDecisions}`);
}

// 4. Decisions distribution
console.log("\n## Decision kind histogram (across all installs)");
const decisionKindH = new Map();
for (const b of byInstall.values()) for (const d of b.decisions) decisionKindH.set(d.k, (decisionKindH.get(d.k) ?? 0) + 1);
for (const [k, c] of [...decisionKindH.entries()].sort((a, b) => b[1] - a[1])) {
  console.log(`  ${k.padEnd(15)} ${c}`);
}

// 5. Pass reasonings deep dive
console.log("\n## Pass reasonings histogram (top 40)");
const passReasonH = new Map();
for (const b of byInstall.values())
  for (const d of b.decisions)
    if (d.k === "Pass") passReasonH.set(d.r ?? "?", (passReasonH.get(d.r ?? "?") ?? 0) + 1);
for (const [r, c] of [...passReasonH.entries()].sort((a, b) => b[1] - a[1]).slice(0, 40)) {
  console.log(`  ${String(c).padStart(5)}  ${r}`);
}

// 6. Longest stuck-state run per install
console.log("\n## Longest consecutive-same-state run per install (freeze indicator)");
for (const [i, b] of [...byInstall.entries()].sort((a, b) => b[1].longestStuck.run - a[1].longestStuck.run).slice(0, 20)) {
  if (b.longestStuck.run < 10) continue;
  console.log(`  ${i}  state=${b.longestStuck.state} hand=${b.longestStuck.hand} run=${b.longestStuck.run}`);
}

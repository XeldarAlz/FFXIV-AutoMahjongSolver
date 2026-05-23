// Targeted analysis: hunt for the specific bug evidence I identified
// against the live corpus. Reads .local/by-date/<date>/ for each given date.
//
// Specifically looks for:
//  - hand_state_paused: reason histogram (auto-play skip reasons)
//  - dispatch_attempted: result × path histogram (state-6 opcode-15 misfires)
//  - decision findings: kind histogram (Pass-out-of-sync count)
//  - snapshot_build_fail / variant_miss
//  - games: action attempts vs hand-ends ratio per install
//  - cross-ref: ticks where hand_state_paused fires immediately after action
//
// Usage:
//   node tools/analyze-bug-evidence.mjs 2026-05-20 2026-05-21 2026-05-22 2026-05-23

import { createReadStream } from "node:fs";
import { readdir } from "node:fs/promises";
import { join } from "node:path";
import { createGunzip } from "node:zlib";
import readline from "node:readline";

const dates = process.argv.slice(2);
if (dates.length === 0) {
  console.error("Usage: node tools/analyze-bug-evidence.mjs <yyyy-mm-dd> [...]");
  process.exit(2);
}

async function* readGzLines(path) {
  const gz = createReadStream(path).pipe(createGunzip());
  const rl = readline.createInterface({ input: gz, crlfDelay: Infinity });
  for await (const line of rl) if (line.trim()) yield line;
}
async function listDir(p) {
  try { return await readdir(p); } catch { return []; }
}

const skipReasonCounts = new Map();
const dispatchByLabel = new Map(); // label -> { Ok, HookFailed, ... }
const dispatchByPath = new Map(); // path -> count
const dispatchByResultByLabel = new Map(); // "label:result:path" -> count
const decisionKindCounts = new Map();
const decisionByFlags = new Map(); // flagsInt -> { kindCounts: Map }
const findingKindCounts = new Map();
const variantMatches = new Map(); // to.Name -> count
const variantMisses = [];
const snapshotFails = [];
const handStatePausedByInstall = new Map(); // install -> { reasons: Map, ticks: [] }
const stalledStates = new Map(); // "state=X hand=Y flags=Z" -> count
const passReasons = new Map(); // raw reason text -> count (for "policy returned Pass — not dispatching")
const installsSeen = new Set();
const installPolicyTier = new Map(); // install -> tier (from games stream decisions)

// Games-level signals
const handsStartedByInstall = new Map();
const handsEndedByInstall = new Map();
const actionsByInstall = new Map();
const callPromptsByInstall = new Map();
const handsWithZeroActions = new Map();    // install -> count of started hands that had 0 actions before next start

// State sequence detection in games — look for "stuck at same state for >N consecutive state events"
const stuckStateRunsByInstall = new Map(); // install -> array of {state, count}

for (const date of dates) {
  const root = `.local/by-date/${date}`;
  // findings
  for (const install of await listDir(`${root}/findings`)) {
    installsSeen.add(install);
    for (const f of await listDir(`${root}/findings/${install}`)) {
      for await (const line of readGzLines(`${root}/findings/${install}/${f}`)) {
        let j; try { j = JSON.parse(line); } catch { continue; }
        const kind = j.kind ?? j.k ?? "?";
        findingKindCounts.set(kind, (findingKindCounts.get(kind) ?? 0) + 1);

        const data = j.data ?? j.d ?? {};
        if (kind === "hand_state_paused") {
          const reason = data.reason ?? "?";
          skipReasonCounts.set(reason, (skipReasonCounts.get(reason) ?? 0) + 1);
          const bucket = handStatePausedByInstall.get(install) ?? { reasons: new Map(), ticks: [] };
          bucket.reasons.set(reason, (bucket.reasons.get(reason) ?? 0) + 1);
          bucket.ticks.push({ ts: j.ts ?? j.t, reason, state: data.state, hand: data.hand, flags: data.flags });
          handStatePausedByInstall.set(install, bucket);
          const key = `state=${data.state} hand=${data.hand} flags=${data.flags}`;
          stalledStates.set(key, (stalledStates.get(key) ?? 0) + 1);
        } else if (kind === "dispatch_attempted") {
          const label = data.label ?? "?";
          const result = data.result ?? "?";
          const path = data.path ?? "?";
          dispatchByLabel.set(label, (dispatchByLabel.get(label) ?? 0) + 1);
          dispatchByPath.set(path, (dispatchByPath.get(path) ?? 0) + 1);
          const ck = `${label}|${result}|${path}`;
          dispatchByResultByLabel.set(ck, (dispatchByResultByLabel.get(ck) ?? 0) + 1);
        } else if (kind === "decision") {
          const kindd = data.kind ?? "?";
          decisionKindCounts.set(kindd, (decisionKindCounts.get(kindd) ?? 0) + 1);
          const fk = data.flags ?? "?";
          const bucket = decisionByFlags.get(fk) ?? new Map();
          bucket.set(kindd, (bucket.get(kindd) ?? 0) + 1);
          decisionByFlags.set(fk, bucket);
          if (kindd === "Pass") {
            const r = data.reasoning ?? "(no reasoning)";
            passReasons.set(r, (passReasons.get(r) ?? 0) + 1);
          }
        } else if (kind === "variant_match") {
          const to = data.to ?? "?";
          variantMatches.set(to, (variantMatches.get(to) ?? 0) + 1);
        } else if (kind === "variant_miss") {
          variantMisses.push({ install, ts: j.ts, ...data });
        } else if (kind === "snapshot_build_fail") {
          snapshotFails.push({ install, ts: j.ts, ...data });
        }
      }
    }
  }

  // games
  for (const install of await listDir(`${root}/games`)) {
    installsSeen.add(install);
    const files = await listDir(`${root}/games/${install}`);
    for (const f of files) {
      const events = [];
      for await (const line of readGzLines(`${root}/games/${install}/${f}`)) {
        let j; try { j = JSON.parse(line); } catch { continue; }
        events.push(j);
      }
      let handStartCount = 0, handEndCount = 0, actionCount = 0, callPromptCount = 0;
      for (const e of events) {
        const ev = e.e ?? e.event ?? "?";
        if (ev === "hand-start") handStartCount++;
        else if (ev === "hand-end") handEndCount++;
        else if (ev === "action") actionCount++;
        else if (ev === "call-prompt") callPromptCount++;
        if (ev === "decision" && e.policy) {
          installPolicyTier.set(install, e.policy);
        }
      }
      handsStartedByInstall.set(install, (handsStartedByInstall.get(install) ?? 0) + handStartCount);
      handsEndedByInstall.set(install, (handsEndedByInstall.get(install) ?? 0) + handEndCount);
      actionsByInstall.set(install, (actionsByInstall.get(install) ?? 0) + actionCount);
      callPromptsByInstall.set(install, (callPromptsByInstall.get(install) ?? 0) + callPromptCount);

      // Detect long runs of unchanged state (the freeze symptom in the games stream).
      let lastState = null, lastHand = null, run = 0;
      const runs = stuckStateRunsByInstall.get(install) ?? [];
      for (const e of events) {
        const ev = e.e ?? e.event ?? "?";
        if (ev === "state") {
          const k = `${e.state}|${e.hand_count ?? e.hand}`;
          if (k === lastState) { run++; }
          else {
            if (run >= 20) runs.push({ state_key: lastState, run, version: e.version });
            run = 1;
            lastState = k;
          }
        }
      }
      if (run >= 20) runs.push({ state_key: lastState, run });
      stuckStateRunsByInstall.set(install, runs);
    }
  }
}

console.log("# Bug-evidence cross-reference report");
console.log(`Dates analyzed: ${dates.join(", ")}`);
console.log(`Total installs seen: ${installsSeen.size}`);
console.log();

console.log("## Finding kind histogram (4 days)");
for (const [k, c] of [...findingKindCounts.entries()].sort((a, b) => b[1] - a[1])) {
  console.log(`  - ${k.padEnd(28)} ${c}`);
}
console.log();

console.log("## hand_state_paused — skip-reason histogram (AutoPlay didn't dispatch)");
for (const [r, c] of [...skipReasonCounts.entries()].sort((a, b) => b[1] - a[1])) {
  console.log(`  ${String(c).padStart(5)}  ${r}`);
}
console.log();

console.log("## hand_state_paused — top stuck-state buckets (state, hand, flags)");
for (const [k, c] of [...stalledStates.entries()].sort((a, b) => b[1] - a[1]).slice(0, 30)) {
  console.log(`  ${String(c).padStart(5)}  ${k}`);
}
console.log();

console.log("## dispatch_attempted (auto-play actually fired) — count by label");
for (const [k, c] of [...dispatchByLabel.entries()].sort((a, b) => b[1] - a[1])) {
  console.log(`  - ${k.padEnd(20)} ${c}`);
}
console.log();
console.log("## dispatch_attempted — count by InputDispatcher.LastDiscardPath");
for (const [k, c] of [...dispatchByPath.entries()].sort((a, b) => b[1] - a[1])) {
  console.log(`  - ${k.padEnd(40)} ${c}`);
}
console.log();
console.log("## dispatch_attempted — label × result × path matrix");
for (const [k, c] of [...dispatchByResultByLabel.entries()].sort((a, b) => b[1] - a[1])) {
  console.log(`  ${String(c).padStart(5)}  ${k}`);
}
console.log();

console.log("## Decision: kind histogram from `decision` findings");
for (const [k, c] of [...decisionKindCounts.entries()].sort((a, b) => b[1] - a[1])) {
  console.log(`  - ${k.padEnd(15)} ${c}`);
}
console.log();
console.log("## Decision: Pass reasonings (when policy chose Pass — desync evidence)");
for (const [r, c] of [...passReasons.entries()].sort((a, b) => b[1] - a[1]).slice(0, 30)) {
  console.log(`  ${String(c).padStart(5)}  ${r}`);
}
console.log();
console.log("## Decision: kind histogram per `flags` bitmask");
for (const [flags, m] of [...decisionByFlags.entries()].sort()) {
  const parts = [...m.entries()].sort((a, b) => b[1] - a[1]).map(([k, c]) => `${k}=${c}`).join(", ");
  console.log(`  flags=${flags}: ${parts}`);
}
console.log();

console.log("## Variants");
console.log("  variant_match:");
for (const [k, c] of [...variantMatches.entries()].sort((a, b) => b[1] - a[1])) {
  console.log(`    - ${k.padEnd(10)} ${c}`);
}
if (variantMisses.length > 0) {
  console.log(`  variant_miss (${variantMisses.length}):`);
  for (const v of variantMisses.slice(0, 5)) console.log(`    - ${v.install} addon=${v.addon_name}`);
}
console.log();

console.log("## Snapshot build failures");
if (snapshotFails.length === 0) console.log("  (none)");
else for (const s of snapshotFails.slice(0, 10)) console.log(`  - ${s.install} variant=${s.variant} atk=${s.atk_values_count}`);
console.log();

console.log("## Games — per-install hand/end/action ratios (freeze indicator)");
console.log("  install                                hand-start  hand-end  action  call-prompt   ended-pct  actions/hand");
const installs = new Set([...handsStartedByInstall.keys(), ...handsEndedByInstall.keys(), ...actionsByInstall.keys()]);
for (const i of [...installs].sort((a, b) => (handsStartedByInstall.get(b) ?? 0) - (handsStartedByInstall.get(a) ?? 0))) {
  const hs = handsStartedByInstall.get(i) ?? 0;
  const he = handsEndedByInstall.get(i) ?? 0;
  const ac = actionsByInstall.get(i) ?? 0;
  const cp = callPromptsByInstall.get(i) ?? 0;
  if (hs === 0) continue;
  const endedPct = hs > 0 ? (100 * he / hs).toFixed(1) : "-";
  const actPerHand = hs > 0 ? (ac / hs).toFixed(2) : "-";
  console.log(`  ${i}  ${String(hs).padStart(10)}  ${String(he).padStart(8)}  ${String(ac).padStart(6)}  ${String(cp).padStart(11)}  ${endedPct.padStart(8)}%  ${actPerHand.padStart(11)}`);
}
console.log();

console.log("## Stuck state runs (≥20 consecutive same state events in games stream)");
let totalStuck = 0;
for (const [install, runs] of stuckStateRunsByInstall.entries()) {
  if (runs.length === 0) continue;
  for (const r of runs) {
    if (r.run >= 50) {
      console.log(`  ${install}  state=${r.state_key} run=${r.run}`);
      totalStuck++;
      if (totalStuck > 30) break;
    }
  }
  if (totalStuck > 30) break;
}
if (totalStuck === 0) console.log("  (no runs ≥50 observed; threshold may be too high)");
console.log();

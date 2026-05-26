// Aggregate the plugin's per-hand game logs into a play-stats summary:
// per-hand win rate, deal-in rate, tsumo/ron split, avg score delta, and
// per-hanchan placement (1st/2nd/3rd/4th rate, avg rank).
//
// Usage:
//   node tools/analyze-play-stats.mjs [games-dir]
//
// Default games-dir resolves to the standard Dalamud config path on Windows:
//   %APPDATA%\XIVLauncher\pluginConfigs\Mahjong.Plugin.Dalamud\games
//
// Each .ndjson file is one hand. Hanchans are reconstructed by chaining
// hand-end.scores_after to the next hand-start.scores within the same play
// session. A hanchan is "complete" only when all four dealer seats (0..3)
// were observed, which under Doman East-only means an E1..E4 cycle ran.
// Incomplete hanchans contribute to per-hand stats but are excluded from
// placement averages so a mid-session attach doesn't pollute rank numbers.

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";

const DEFAULT_GAMES_DIR = process.env.APPDATA
  ? join(process.env.APPDATA, "XIVLauncher", "pluginConfigs",
         "Mahjong.Plugin.Dalamud", "games")
  : null;

const gamesDir = process.argv[2] ?? DEFAULT_GAMES_DIR;
if (!gamesDir) {
  console.error("usage: node analyze-play-stats.mjs <games-dir>");
  process.exit(1);
}

const files = (await readdir(gamesDir))
  .filter(f => f.startsWith("game-") && f.endsWith(".ndjson"))
  .sort();

if (files.length === 0) {
  console.error(`no game-*.ndjson files in ${gamesDir}`);
  process.exit(2);
}

// Each file may contain the PREVIOUS hand's hand-end (written right before the
// new hand-start by GameLogger.MaybeRollHand). Collect events globally, sort
// by timestamp, then pair each hand-start with the next hand-end.
const startEvents = [];
const endEvents = [];
for (const f of files) {
  const text = await readFile(join(gamesDir, f), "utf8");
  for (const line of text.split(/\r?\n/)) {
    if (!line) continue;
    let j;
    try { j = JSON.parse(line); } catch { continue; }
    if (j.e === "hand-start") startEvents.push({ ...j, _file: f, _t: Date.parse(j.t) });
    else if (j.e === "hand-end") endEvents.push({ ...j, _file: f, _t: Date.parse(j.t) });
  }
}
startEvents.sort((a, b) => a._t - b._t);
endEvents.sort((a, b) => a._t - b._t);

const hands = [];
let endIdx = 0;
for (let i = 0; i < startEvents.length; i++) {
  const start = startEvents[i];
  const nextStart = startEvents[i + 1];
  let end = null;
  while (endIdx < endEvents.length && endEvents[endIdx]._t < start._t) endIdx++;
  if (endIdx < endEvents.length) {
    const candidate = endEvents[endIdx];
    if (!nextStart || candidate._t <= nextStart._t) {
      end = candidate;
      endIdx++;
    }
  }
  hands.push({ file: start._file, start, end, t: start._t });
}

// Chain hanchans: hand[N].end.scores_after must equal hand[N+1].start.scores
// for them to share a hanchan. A gap longer than 30 min also forces a break.
const SCORE_CHAIN_TOL = 0;
const SESSION_GAP_MS = 30 * 60 * 1000;

function scoresMatch(a, b) {
  if (!a || !b || a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (Math.abs(a[i] - b[i]) > SCORE_CHAIN_TOL) return false;
  return true;
}

const hanchans = [];
let current = null;
let prev = null;
for (const h of hands) {
  const continues =
    prev !== null &&
    prev.end !== null &&
    Array.isArray(prev.end.scores_after) &&
    Array.isArray(h.start.scores) &&
    scoresMatch(prev.end.scores_after, h.start.scores) &&
    (h.t - prev.t) <= SESSION_GAP_MS;

  if (!continues) {
    if (current) hanchans.push(current);
    current = { hands: [], ourSeat: h.start.seat, dealersSeen: new Set() };
  }
  current.hands.push(h);
  current.dealersSeen.add(h.start.dealer);
  prev = h;
}
if (current) hanchans.push(current);

// A real hand-end must have deltas that sum to ~0 (no money is created). When
// the plugin attaches before the addon writes real scores, the first
// computed delta is start-from-zero (sum > 0) which falsely classifies as
// "draw". Drop those so the stats reflect real play only.
function isRealHandEnd(end) {
  if (!end || !Array.isArray(end.deltas)) return false;
  const sum = end.deltas.reduce((a, b) => a + b, 0);
  return Math.abs(sum) <= 100;
}

let tsumoCount = 0, ronCount = 0, dealInCount = 0, drawCount = 0;
let scoreDeltaTotal = 0;
let scoredHands = 0;
let initArtifactHands = 0;
for (const h of hands) {
  if (!h.end) continue;
  if (!isRealHandEnd(h.end)) { initArtifactHands++; continue; }
  scoredHands++;
  const seat = h.start.seat;
  const delta = (h.end.deltas?.[seat]) ?? 0;
  scoreDeltaTotal += delta;
  if (h.end.kind === "tsumo" && h.end.winner === seat) tsumoCount++;
  else if (h.end.kind === "ron" && h.end.winner === seat) ronCount++;
  else if (h.end.kind === "ron" && h.end.loser === seat) dealInCount++;
  else if (h.end.kind === "draw") drawCount++;
}
const winCount = tsumoCount + ronCount;

// Per-hanchan placement uses the LAST hand-end of a hanchan that observed
// all four dealer seats. Anything shorter is incomplete and excluded.
const ranks = [0, 0, 0, 0];
let rankedHanchans = 0;
let incompleteHanchans = 0;
for (const hc of hanchans) {
  if (hc.dealersSeen.size < 4) { incompleteHanchans++; continue; }
  const last = [...hc.hands].reverse().find(h => h.end && Array.isArray(h.end.scores_after));
  if (!last) { incompleteHanchans++; continue; }
  const scores = last.end.scores_after;
  const seat = hc.ourSeat;
  const sortedDesc = [...scores].map((v, i) => ({ v, i }))
    .sort((a, b) => b.v - a.v || a.i - b.i);
  const rank = sortedDesc.findIndex(x => x.i === seat) + 1;
  if (rank < 1 || rank > 4) { incompleteHanchans++; continue; }
  ranks[rank - 1]++;
  rankedHanchans++;
}

function pct(n, total) {
  if (total === 0) return "  -  ";
  return ((n / total) * 100).toFixed(1).padStart(4) + "%";
}

console.log(`games-dir: ${gamesDir}`);
console.log(`files:     ${files.length} (.ndjson)`);
console.log(`hands:     ${hands.length} parsed, ${scoredHands} real, ${initArtifactHands} init-artifact (dropped)`);
console.log(`hanchans:  ${hanchans.length} chained, ${rankedHanchans} complete (E1-E4 dealer cycle), ${incompleteHanchans} incomplete`);
console.log("");
console.log("=== per-hand stats ===");
console.log(`  scored hands:    ${scoredHands}`);
console.log(`  tsumo:           ${tsumoCount}  (${pct(tsumoCount, scoredHands)})`);
console.log(`  ron:             ${ronCount}  (${pct(ronCount, scoredHands)})`);
console.log(`  total wins:      ${winCount}  (${pct(winCount, scoredHands)})`);
console.log(`  deal-ins:        ${dealInCount}  (${pct(dealInCount, scoredHands)})`);
console.log(`  draws:           ${drawCount}  (${pct(drawCount, scoredHands)})`);
console.log(`  avg score/hand:  ${scoredHands ? (scoreDeltaTotal / scoredHands).toFixed(1).padStart(8) : "    -   "}`);
console.log("");
if (rankedHanchans > 0) {
  const avgRank = (1 * ranks[0] + 2 * ranks[1] + 3 * ranks[2] + 4 * ranks[3]) / rankedHanchans;
  console.log("=== per-hanchan stats ===");
  console.log(`  1st: ${ranks[0]}  (${pct(ranks[0], rankedHanchans)})`);
  console.log(`  2nd: ${ranks[1]}  (${pct(ranks[1], rankedHanchans)})`);
  console.log(`  3rd: ${ranks[2]}  (${pct(ranks[2], rankedHanchans)})`);
  console.log(`  4th: ${ranks[3]}  (${pct(ranks[3], rankedHanchans)})`);
  console.log(`  avg rank:        ${avgRank.toFixed(3)}`);
  // Reference: avg rank 2.5 is dead-even; 2.48-2.52 is the heuristic-bot band,
  // sub-2.45 means measurable edge, sub-2.40 is Tokujou-stable equivalent.
} else {
  console.log("=== per-hanchan stats ===");
  console.log("  no complete hanchans yet — need at least one continuous E1-E4 run");
}

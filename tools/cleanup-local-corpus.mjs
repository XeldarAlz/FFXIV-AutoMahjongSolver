// Age out .local/ artifacts older than N days. Default 7. Dry-run by default;
// pass --apply to actually delete.
//
// Targets:
//   - .local/by-date/<YYYY-MM-DD>/   — pulled telemetry corpora
//   - .local/b2-manifest-*.ndjson    — intermediate filtered manifests
//   - .local/worker-events-*.ndjson  — Cloudflare Worker log dumps
//
// Spares:
//   - .local/b2-manifest.ndjson (the master, refresh on demand)
//   - .local/secrets.ps1 (credentials)
//   - .local/*.md (notes — keep for analysis context)
//
// Usage:
//   node tools/cleanup-local-corpus.mjs              # dry-run, 7-day default
//   node tools/cleanup-local-corpus.mjs --days 14    # dry-run, 14-day cutoff
//   node tools/cleanup-local-corpus.mjs --apply      # actually delete

import { readdir, stat, rm } from "node:fs/promises";
import { join, basename } from "node:path";

const args = process.argv.slice(2);
const apply = args.includes("--apply");
const daysIdx = args.indexOf("--days");
const cutoffDays = daysIdx >= 0 && args[daysIdx + 1]
  ? parseInt(args[daysIdx + 1], 10)
  : 7;
if (!Number.isFinite(cutoffDays) || cutoffDays < 1) {
  console.error(`Invalid --days value: ${args[daysIdx + 1]}`);
  process.exit(2);
}

const cutoffMs = Date.now() - cutoffDays * 24 * 3600 * 1000;
const cutoffDate = new Date(cutoffMs);

console.log(`Cleanup mode: ${apply ? "APPLY (deletes files)" : "DRY-RUN (no changes)"}`);
console.log(`Cutoff: keep files modified on or after ${cutoffDate.toISOString().slice(0, 10)}`);
console.log();

let totalBytesToDelete = 0;
const toDelete = [];

async function dirSize(p) {
  let sum = 0;
  try {
    for (const e of await readdir(p, { withFileTypes: true })) {
      const f = join(p, e.name);
      const s = await stat(f);
      if (s.isDirectory()) sum += await dirSize(f);
      else sum += s.size;
    }
  } catch { /* unreadable, skip */ }
  return sum;
}

// 1. by-date/<YYYY-MM-DD> directories where the date is older than cutoff
const byDateRoot = ".local/by-date";
try {
  const entries = await readdir(byDateRoot, { withFileTypes: true });
  for (const e of entries) {
    if (!e.isDirectory()) continue;
    const m = e.name.match(/^(\d{4})-(\d{2})-(\d{2})$/);
    if (!m) continue;
    const dirDate = new Date(`${e.name}T00:00:00Z`);
    if (dirDate >= cutoffDate) continue;
    const p = join(byDateRoot, e.name);
    const bytes = await dirSize(p);
    toDelete.push({ kind: "by-date", path: p, bytes, age: e.name });
    totalBytesToDelete += bytes;
  }
} catch (e) {
  console.log(`(no .local/by-date dir: ${e.message})`);
}

// 2. Intermediate filtered manifests (.local/b2-manifest-*.ndjson) older than cutoff.
//    Spare the master b2-manifest.ndjson — it's the source of truth for pulls.
const localRoot = ".local";
const intermediateRe = /^b2-manifest-.+\.ndjson$/;
try {
  for (const e of await readdir(localRoot, { withFileTypes: true })) {
    if (!e.isFile()) continue;
    if (!intermediateRe.test(e.name)) continue;
    const p = join(localRoot, e.name);
    const s = await stat(p);
    if (s.mtime >= cutoffDate) continue;
    toDelete.push({ kind: "intermediate-manifest", path: p, bytes: s.size, age: s.mtime.toISOString().slice(0, 10) });
    totalBytesToDelete += s.size;
  }
} catch { /* skip */ }

// 3. Worker event dumps
const workerLogRe = /^worker-events-.+\.ndjson$/;
try {
  for (const e of await readdir(localRoot, { withFileTypes: true })) {
    if (!e.isFile()) continue;
    if (!workerLogRe.test(e.name)) continue;
    const p = join(localRoot, e.name);
    const s = await stat(p);
    if (s.mtime >= cutoffDate) continue;
    toDelete.push({ kind: "worker-events", path: p, bytes: s.size, age: s.mtime.toISOString().slice(0, 10) });
    totalBytesToDelete += s.size;
  }
} catch { /* skip */ }

// 4. Legacy pre-by-date pulls (.local/memdumps/, .local/b2-all/, .local/memdumps_*).
//    These predate the by-date/<date>/ structure and were created by earlier
//    versions of the b2-pull-*.mjs scripts. Always old; never refreshed.
//    Always re-pullable from B2 within the lifecycle window. Use directory
//    mtime as the age signal — the contained files are typically immutable.
const legacyDirs = ["memdumps", "b2-all"];
for (const name of legacyDirs) {
  const p = join(localRoot, name);
  try {
    const s = await stat(p);
    if (s.mtime >= cutoffDate) continue;
    const bytes = await dirSize(p);
    toDelete.push({ kind: "legacy-pull", path: p, bytes, age: s.mtime.toISOString().slice(0, 10) });
    totalBytesToDelete += bytes;
  } catch { /* not present, skip */ }
}

if (toDelete.length === 0) {
  console.log("Nothing to clean up.");
  process.exit(0);
}

console.log("Targets:");
const fmtMB = (b) => (b / 1024 / 1024).toFixed(2);
for (const t of toDelete.sort((a, b) => b.bytes - a.bytes)) {
  console.log(`  [${t.kind.padEnd(20)}] ${fmtMB(t.bytes).padStart(8)} MB  ${t.age}  ${t.path}`);
}
console.log(`\nTotal: ${toDelete.length} target(s), ${fmtMB(totalBytesToDelete)} MB`);

if (!apply) {
  console.log("\nDry-run only. Re-run with --apply to delete.");
  process.exit(0);
}

let deleted = 0;
let freed = 0;
for (const t of toDelete) {
  try {
    await rm(t.path, { recursive: true, force: true });
    deleted++;
    freed += t.bytes;
  } catch (e) {
    console.error(`Failed to remove ${t.path}: ${e.message}`);
  }
}
console.log(`\nDeleted ${deleted}/${toDelete.length} target(s), freed ${fmtMB(freed)} MB.`);

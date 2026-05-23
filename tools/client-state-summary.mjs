// Quick state-per-client summary using the inputs stream (which records
// the resolved addon name per FireCallback). Counts unique installs per
// variant in the recent window and prints sample install IDs.

import { createReadStream } from "node:fs";
import { readdir } from "node:fs/promises";
import { join } from "node:path";
import { createGunzip } from "node:zlib";
import readline from "node:readline";

const cutoffDate = process.argv[2] ?? "2026-05-20";

async function* readGzLines(p) {
  const gz = createReadStream(p).pipe(createGunzip());
  const rl = readline.createInterface({ input: gz, crlfDelay: Infinity });
  for await (const l of rl) if (l.trim()) yield l;
}

const byAddonByInstall = new Map(); // addon -> Set<install>
const byInstallActions = new Map(); // install -> { addon, count }

async function* walk(dir) {
  for (const e of await readdir(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else if (e.isFile() && e.name.endsWith(".ndjson.gz")) yield p;
  }
}

for await (const f of walk(".local/by-date")) {
  if (!f.includes("inputs")) continue;
  const m = f.match(/by-date[\\\/](\d{4}-\d{2}-\d{2})[\\\/]inputs[\\\/]([^\\\/]+)/);
  if (!m) continue;
  const date = m[1];
  const install = m[2];
  if (date < cutoffDate) continue;
  for await (const line of readGzLines(f)) {
    const idx = line.indexOf('"addon":"');
    if (idx < 0) continue;
    const end = line.indexOf('"', idx + 9);
    const addon = line.slice(idx + 9, end);
    if (!byAddonByInstall.has(addon)) byAddonByInstall.set(addon, new Set());
    byAddonByInstall.get(addon).add(install);
    const cur = byInstallActions.get(install) ?? { addon, count: 0 };
    cur.count++;
    cur.addon = addon;
    byInstallActions.set(install, cur);
  }
}

console.log(`# Per-variant install summary (from inputs stream, ${cutoffDate}+)`);
console.log();
console.log("Variant   | Distinct installs | Total input events");
for (const [addon, set] of byAddonByInstall) {
  let total = 0;
  for (const inst of set) total += byInstallActions.get(inst)?.count ?? 0;
  console.log(`${addon.padEnd(10)}|    ${String(set.size).padStart(8)} (installs) |  ${String(total).padStart(6)} (events)`);
}

console.log();
console.log("# Top installs by event count");
for (const [inst, info] of [...byInstallActions.entries()].sort((a, b) => b[1].count - a[1].count).slice(0, 15)) {
  console.log(`  ${inst}  ${info.addon.padEnd(6)}  ${info.count} events`);
}

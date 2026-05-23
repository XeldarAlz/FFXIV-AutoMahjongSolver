// Walk all downloaded memdumps and report schema-version distribution
// per install. Lets us know which installs are already on v=2 with
// agent_b64, without needing fresh B2 pulls.

import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { gunzipSync } from "node:zlib";

const root = process.argv[2] ?? ".local/by-date";

async function* walk(dir) {
  for (const e of await readdir(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else if (e.isFile() && e.name.endsWith(".ndjson.gz")) yield p;
  }
}

const counts = new Map(); // key: `${install}|v=N|hasAgent=true/false` -> count

for await (const f of walk(root)) {
  if (!f.includes("memdumps")) continue;
  const install = f.match(/memdumps[\\\/]([^\\\/]+)[\\\/]/)?.[1] ?? "?";
  try {
    const raw = await readFile(f);
    const body = (raw[0] === 0x1f && raw[1] === 0x8b) ? gunzipSync(raw) : raw;
    const first = body.toString("utf8").split("\n", 1)[0];
    if (!first) continue;
    const r = JSON.parse(first);
    const hasAgent = !!r.agent_b64;
    const key = `${install}|v=${r.v}|agent=${hasAgent}`;
    counts.set(key, (counts.get(key) ?? 0) + 1);
  } catch { /* skip */ }
}

console.log("install                                   schema");
for (const [k, c] of [...counts.entries()].sort()) {
  console.log(`  ${k.padEnd(60)} files=${c}`);
}

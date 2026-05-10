// One-time PII scrub for the mahjong-telemetry bucket.
//
// Walks the findings/ prefix, decompresses each object, and flags any whose
// NDJSON `data.dir` value matches a Windows-path / Unix-home shape. By
// default the tool prints the flagged objects and exits — pass --commit to
// actually delete them. Touching anything outside findings/ is impossible
// by construction (the LIST scope and the per-key check both filter on it).
//
// Usage:
//   $env:B2_KEY_ID = "..."             # must have writeKeys (delete) on the bucket
//   $env:B2_APPLICATION_KEY = "..."
//   node tools/b2-scrub-pii.mjs        # dry run — list what would be deleted
//   node tools/b2-scrub-pii.mjs --commit
//
// The standing read-only key (`mahjong-analysis-read`) cannot delete. Generate
// a temporary write-capable key at https://secure.backblaze.com/app_keys.htm
// scoped to bucket=mahjong-telemetry, capabilities=listFiles,readFiles,
// deleteFiles. Revoke immediately after running.

import { AwsClient } from "../server/node_modules/aws4fetch/dist/aws4fetch.esm.mjs";
import { request as undiciRequest } from "../server/node_modules/undici/index.js";
import { gunzipSync } from "node:zlib";

const REGION = "eu-central-003";
const ENDPOINT = "https://s3.eu-central-003.backblazeb2.com";
const BUCKET = "mahjong-telemetry";
const PREFIX = "findings/";

const args = process.argv.slice(2);
const COMMIT = args.includes("--commit");

const keyId = process.env.B2_KEY_ID;
const appKey = process.env.B2_APPLICATION_KEY;
if (!keyId || !appKey) { console.error("B2_KEY_ID / B2_APPLICATION_KEY must be set"); process.exit(2); }

const aws = new AwsClient({ accessKeyId: keyId, secretAccessKey: appKey, service: "s3", region: REGION });

// Bypass Node's auto-decompression so the .gz body lands intact.
async function rawFetch(url, init = {}) {
  const signed = await aws.sign(url, init);
  const headers = {};
  for (const [k, v] of signed.headers.entries()) headers[k] = v;
  const res = await undiciRequest(signed.url, { method: signed.method, headers, body: init.body });
  const chunks = [];
  for await (const c of res.body) chunks.push(c);
  return { status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks) };
}

// Heuristic for "this string is an absolute Windows or Unix path".
const PATH_RE = /(?:[A-Z]:[\\/])|(?:^|"|\\u002F)\/home\//i;

async function listAll() {
  const all = [];
  let token = null;
  while (true) {
    const params = new URLSearchParams({ "list-type": "2", prefix: PREFIX, "max-keys": "1000" });
    if (token) params.set("continuation-token", token);
    const r = await rawFetch(`${ENDPOINT}/${BUCKET}?${params}`);
    if (r.status >= 300) { console.error(`LIST ${r.status}: ${r.body.toString("utf8").slice(0, 300)}`); process.exit(1); }
    const xml = r.body.toString("utf8");
    const blockRe = /<Contents>([\s\S]*?)<\/Contents>/g;
    const tag = (b, n) => { const m = b.match(new RegExp(`<${n}>([^<]*)</${n}>`)); return m ? m[1] : null; };
    let m;
    while ((m = blockRe.exec(xml))) {
      const key = tag(m[1], "Key");
      if (!key || !key.startsWith(PREFIX)) continue; // belt-and-braces
      all.push({ key, size: parseInt(tag(m[1], "Size") ?? "0", 10) });
    }
    const truncated = /<IsTruncated>true<\/IsTruncated>/.test(xml);
    if (!truncated) break;
    const tm = xml.match(/<NextContinuationToken>([^<]+)<\/NextContinuationToken>/);
    if (!tm) break;
    token = tm[1];
  }
  return all;
}

async function scanObject(key) {
  const r = await rawFetch(`${ENDPOINT}/${BUCKET}/${key}`);
  if (r.status >= 300) return { key, error: `GET ${r.status}` };
  let body;
  try { body = (r.body[0] === 0x1f && r.body[1] === 0x8b) ? gunzipSync(r.body) : r.body; }
  catch (e) { return { key, error: `gunzip: ${e.message}` }; }
  const text = body.toString("utf8");
  // Match whole NDJSON file, not per-line, so embedded paths within nested
  // values are still caught.
  const leakHits = [];
  for (const line of text.split("\n")) {
    if (!line) continue;
    if (PATH_RE.test(line)) leakHits.push(line.length > 200 ? line.slice(0, 200) + "…" : line);
  }
  return { key, leak: leakHits.length > 0, hits: leakHits };
}

async function deleteObject(key) {
  // Belt-and-braces: refuse to delete anything outside findings/.
  if (!key.startsWith(PREFIX)) throw new Error(`refusing to delete out-of-scope key: ${key}`);
  const r = await rawFetch(`${ENDPOINT}/${BUCKET}/${key}`, { method: "DELETE" });
  return r.status >= 200 && r.status < 300 ? "ok" : `DELETE ${r.status}: ${r.body.toString("utf8").slice(0, 200)}`;
}

const objs = await listAll();
console.log(`[scrub] ${objs.length} object(s) under ${PREFIX}`);

const flagged = [];
for (const o of objs) {
  const result = await scanObject(o.key);
  if (result.error) { console.error(`[scrub] skip ${o.key}: ${result.error}`); continue; }
  if (!result.leak) continue;
  flagged.push(o.key);
  console.log(`[leak] ${o.key}`);
  for (const h of result.hits) console.log(`         ${h}`);
}

console.log(`\n[scrub] ${flagged.length} object(s) flagged for deletion`);

if (!COMMIT) {
  console.log("[scrub] dry run — pass --commit to delete");
  process.exit(0);
}

let deleted = 0, failed = 0;
for (const k of flagged) {
  const r = await deleteObject(k);
  if (r === "ok") { deleted++; console.log(`[del]  ${k}`); }
  else { failed++; console.error(`[fail] ${k}: ${r}`); }
}
console.log(`[scrub] deleted=${deleted} failed=${failed}`);

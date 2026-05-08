// Cloudflare Worker — telemetry ingest for the Mahjong plugin.
//
// Validates incoming uploads, enforces per-install rate limits, and writes
// the gzipped payload to a Backblaze B2 bucket via the S3-compatible API,
// keyed as {stream}/{install_id}/{date}/{filename}.gz.
//
// Deployment:
//   wrangler deploy
//   (see wrangler.toml for KV binding + B2 vars + secrets the Worker expects)
//
// The plugin sends:
//   X-Install-Id        — anonymous GUID, one per install
//   X-Plugin-Version    — semver of the plugin build
//   X-Plugin-Hash       — first 16 hex of SHA-256(plugin.dll)
//   X-Game-Version      — FFXIV client build hash
//   X-Client-Region     — Dalamud ClientLanguage (English/Japanese/...)
//   X-Os-Platform       — Win32NT / Unix / etc.
//   X-Schema-Version    — envelope schema version (currently 1)
//   X-Stream            — one of: games, errors, findings, memdumps, discards, inputs, sigprobes
//   X-Filename          — original filename on the client (already-shipped files
//                         drop a .shipped sidecar locally so we won't see them twice)
//   Content-Encoding    — gzip
//   Body                — gzipped NDJSON or binary blob

import { AwsClient } from "aws4fetch";

export interface Env {
  RATE_LIMIT_KV: KVNamespace;
  // B2 S3-compatible storage settings.
  B2_REGION: string;        // e.g. "us-west-002"
  B2_BUCKET: string;        // e.g. "mahjong-telemetry"
  B2_ENDPOINT: string;      // e.g. "https://s3.us-west-002.backblazeb2.com"
  B2_KEY_ID: string;        // secret — set via `wrangler secret put`
  B2_APPLICATION_KEY: string; // secret — set via `wrangler secret put`
}

const ALLOWED_STREAMS = new Set([
  "games", "errors", "findings", "memdumps", "discards", "inputs", "sigprobes",
]);

// Per-install rolling 24-hour upload cap. Memdumps dominate; everything else
// is small. 200 MB/day/install is generous enough that no honest user hits
// it, low enough that a runaway client can't bankrupt the bucket.
const MAX_BYTES_PER_DAY = 200 * 1024 * 1024;

// Hard per-request size cap. Pre-gzip the plugin keeps individual files
// well under 1 MB; allow 10 MB to leave headroom for memdump rolls.
const MAX_REQUEST_BYTES = 10 * 1024 * 1024;

const GUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export default {
  async fetch(req: Request, env: Env, _ctx: ExecutionContext): Promise<Response> {
    if (req.method === "GET" && new URL(req.url).pathname === "/health")
      return new Response("ok", { status: 200 });

    if (req.method !== "POST")
      return reject("method_not_allowed", 405, { method: req.method });

    const url = new URL(req.url);
    if (url.pathname !== "/v1/upload")
      return reject("not_found", 404, { path: url.pathname });

    // ---- Header validation ----
    const installId = req.headers.get("X-Install-Id") ?? "";
    if (!GUID_RE.test(installId))
      return reject("invalid_install_id", 400, { install_id: installId });

    const stream = req.headers.get("X-Stream") ?? "";
    if (!ALLOWED_STREAMS.has(stream))
      return reject("invalid_stream", 400, { install_id: installId, stream });

    const filename = (req.headers.get("X-Filename") ?? "").replace(/[^a-zA-Z0-9._-]/g, "_");
    if (!filename || filename.length > 200)
      return reject("invalid_filename", 400, { install_id: installId, stream, filename });

    const schemaVersion = parseInt(req.headers.get("X-Schema-Version") ?? "0", 10);
    if (!Number.isFinite(schemaVersion) || schemaVersion < 1 || schemaVersion > 10)
      return reject("invalid_schema_version", 400, { install_id: installId, stream, schema_version: schemaVersion });

    // ---- Size cap (cheap pre-check on Content-Length) ----
    const declaredLen = parseInt(req.headers.get("Content-Length") ?? "0", 10);
    if (declaredLen > MAX_REQUEST_BYTES)
      return reject("payload_too_large", 413, { install_id: installId, stream, declared_bytes: declaredLen, limit: MAX_REQUEST_BYTES });

    // ---- Per-install daily rate limit (KV) ----
    const today = new Date().toISOString().substring(0, 10);
    const rateKey = `bytes:${installId}:${today}`;
    let usedBytes = 0;
    try {
      const usedRaw = await env.RATE_LIMIT_KV.get(rateKey);
      usedBytes = usedRaw ? parseInt(usedRaw, 10) : 0;
    } catch (err) {
      return fail("kv_get_failed", err, { install_id: installId, stream, rate_key: rateKey });
    }
    if (usedBytes + declaredLen > MAX_BYTES_PER_DAY)
      return reject("rate_limited", 429, { install_id: installId, stream, used_bytes: usedBytes, declared_bytes: declaredLen, limit: MAX_BYTES_PER_DAY });

    // ---- Read body. Cloudflare auto-decompresses on Content-Encoding: gzip
    // for *response* paths but NOT request bodies — we keep the gzip on the
    // wire and store as-is to save bytes and let analyzers decompress on
    // demand. ----
    const body = await req.arrayBuffer();
    if (body.byteLength > MAX_REQUEST_BYTES)
      return reject("payload_too_large", 413, { install_id: installId, stream, body_bytes: body.byteLength, limit: MAX_REQUEST_BYTES });

    // ---- Sign + PUT to B2 (S3-compatible) ----
    const key = `${stream}/${installId}/${today}/${filename}.gz`;
    const aws = new AwsClient({
      accessKeyId: env.B2_KEY_ID,
      secretAccessKey: env.B2_APPLICATION_KEY,
      service: "s3",
      region: env.B2_REGION,
    });
    const putUrl = `${env.B2_ENDPOINT.replace(/\/$/, "")}/${env.B2_BUCKET}/${key}`;
    let resp: Response;
    try {
      resp = await aws.fetch(putUrl, {
        method: "PUT",
        body,
        headers: {
          "content-type": "application/octet-stream",
          "content-encoding": "gzip",
          "x-amz-meta-install-id": installId,
          "x-amz-meta-plugin-version": req.headers.get("X-Plugin-Version") ?? "",
          "x-amz-meta-plugin-hash": req.headers.get("X-Plugin-Hash") ?? "",
          "x-amz-meta-game-version": req.headers.get("X-Game-Version") ?? "",
          "x-amz-meta-client-region": req.headers.get("X-Client-Region") ?? "",
          "x-amz-meta-os-platform": req.headers.get("X-Os-Platform") ?? "",
          "x-amz-meta-schema-version": schemaVersion.toString(),
          "x-amz-meta-received-at": new Date().toISOString(),
        },
      });
    } catch (err) {
      return fail("b2_put_threw", err, { install_id: installId, stream, key, bytes: body.byteLength });
    }
    if (!resp.ok) {
      const bodyText = await resp.text().catch(() => "");
      return fail(
        "b2_put_failed",
        new Error(`B2 returned ${resp.status}: ${bodyText.substring(0, 500)}`),
        { install_id: installId, stream, key, bytes: body.byteLength, b2_status: resp.status });
    }

    // Update rate-limit counter. 25-hour TTL so the day boundary is never
    // missed if KV propagation lags. Failure here is non-fatal — the upload
    // already succeeded; the worst case is the install gets a slightly higher
    // effective cap for the day.
    try {
      await env.RATE_LIMIT_KV.put(
        rateKey,
        (usedBytes + body.byteLength).toString(),
        { expirationTtl: 25 * 60 * 60 });
    } catch (err) {
      log("warn", "kv_put_failed", { install_id: installId, stream, rate_key: rateKey, error: errString(err) });
    }

    log("info", "accepted", {
      install_id: installId,
      stream,
      key,
      bytes: body.byteLength,
      plugin_version: req.headers.get("X-Plugin-Version") ?? "",
      game_version: req.headers.get("X-Game-Version") ?? "",
    });
    return json({ ok: true, key, bytes: body.byteLength }, 200);
  },
};

// Single structured log line. Emits as JSON so Workers Logs (and Logpush
// downstream) can index on fields like install_id / stream / event without
// regex-parsing free text.
function log(level: "info" | "warn" | "error", event: string, fields: Record<string, unknown>): void {
  const line = JSON.stringify({ level, event, ...fields });
  if (level === "error") console.error(line);
  else if (level === "warn") console.warn(line);
  else console.log(line);
}

function reject(event: string, status: number, fields: Record<string, unknown>): Response {
  log("warn", event, { ...fields, status });
  return json({ error: event }, status);
}

function fail(event: string, err: unknown, fields: Record<string, unknown>): Response {
  log("error", event, { ...fields, error: errString(err) });
  return json({ error: "internal_error" }, 500);
}

function errString(err: unknown): string {
  if (err instanceof Error) return `${err.name}: ${err.message}`;
  return String(err);
}

function json(payload: unknown, status: number): Response {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "content-type": "application/json" },
  });
}

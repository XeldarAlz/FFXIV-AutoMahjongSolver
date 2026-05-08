# Telemetry server

Cloudflare Worker that receives anonymous diagnostic uploads from the
Mahjong plugin (gameplay logs, error reports, memory dumps, etc.) and
stores them in **Backblaze B2** (S3-compatible) for offline
reverse-engineering analysis.

The Worker is the HTTP ingress: it validates headers, enforces a
per-install daily byte cap (KV-backed), and forwards the payload to B2 via
a SigV4-signed PUT. B2 was chosen over R2 because R2 isn't available in
all regions; the Worker code stays small either way.

## What gets sent

Every upload carries:

| Header | Purpose |
| --- | --- |
| `X-Install-Id` | Random GUID minted on first config migration. The only stable handle to a single install. **No PII.** |
| `X-Plugin-Version` | Semver of the plugin build that produced the file. |
| `X-Plugin-Hash` | SHA-256 prefix of the plugin DLL. Lets us tell which build a log came from. |
| `X-Game-Version` | FFXIV client build version string. |
| `X-Client-Region` | Dalamud `ClientLanguage` (English / Japanese / French / German). |
| `X-Os-Platform` | Operating system identifier. |
| `X-Schema-Version` | Envelope schema version. |
| `X-Stream` | One of: `games`, `errors`, `findings`, `memdumps`, `discards`, `inputs`, `sigprobes`. |
| `X-Filename` | Original filename on the client. |
| Body | gzipped NDJSON (or, for `memdumps`, NDJSON with base64-encoded byte arrays). |

The Worker re-emits these on the B2 object as `x-amz-meta-*` custom
metadata so analyzers can filter without unpacking the body.

## Deploy

```powershell
cd server
npm install                       # pulls aws4fetch + wrangler

# 1. Set up Backblaze B2 (one-time, in the B2 web console at
#    https://secure.backblaze.com)
#    a. Sign up for B2 Cloud Storage (free tier: 10 GB storage,
#       1 GB/day download, 2,500 Class C uploads/day).
#    b. Create a private bucket — name suggestion: "mahjong-telemetry".
#       Note the S3 endpoint shown for the bucket; it's region-specific
#       (e.g. https://s3.us-west-002.backblazeb2.com).
#    c. App Keys → "Add a New Application Key":
#         Name:           mahjong-telemetry-worker
#         Bucket access:  mahjong-telemetry only
#         Capabilities:   Read and Write Files (uncheck list-keys etc.)
#       Save the keyID and applicationKey — applicationKey is shown ONCE.

# 2. Edit wrangler.toml [vars] block: set B2_REGION, B2_BUCKET,
#    B2_ENDPOINT to match the bucket you just created.

# 3. Push the B2 credentials as Worker secrets (you'll be prompted to paste):
wrangler login
wrangler secret put B2_KEY_ID
wrangler secret put B2_APPLICATION_KEY

# 4. Create the KV namespace for rate limiting (skip if the id in
#    wrangler.toml already points at one you own):
wrangler kv:namespace create RATE_LIMIT_KV
# Copy the returned id into wrangler.toml (kv_namespaces.id)

# 5. Apply the B2 lifecycle rules (memdumps purge after 30d, sigprobes 60d).
#    Easiest path is the B2 web console:
#      Bucket → Lifecycle Settings → "Use a custom rule"
#      Add the two rules from b2-lifecycle.json (prefix + days settings).
#    Or via b2 CLI (https://www.backblaze.com/docs/cloud-storage-command-line-tools):
b2 authorize-account
b2 update-bucket --lifecycleRulesFile b2-lifecycle.json mahjong-telemetry allPrivate

# 6. Deploy the Worker
wrangler deploy
# Note the printed *.workers.dev URL — that's the upload endpoint.

# 7. Update telemetry-endpoint.json with the URL and commit it. The plugin
#    fetches this file from raw.githubusercontent.com at startup, so a new
#    URL propagates to every install on next plugin load.
```

## Observing the Worker

The Worker emits one structured JSON line per request via `console.log` /
`console.warn` / `console.error`. Levels: `info` for accepted uploads, `warn`
for rejected requests (validation failures, rate limits), `error` for
internal failures (KV/B2 outages).

```powershell
# Live tail (no retention, just the last few minutes)
wrangler tail mahjong-telemetry

# Filter to errors
wrangler tail mahjong-telemetry --status error

# Search the persisted Workers Logs (7-day retention) in the dashboard:
# Workers & Pages → mahjong-telemetry → Logs.
# Field-level filters work because every line is JSON, e.g.
#   $.event = "rate_limited"
#   $.event = "b2_put_failed"
#   $.install_id = "<guid>"
```

`[observability]` is enabled in `wrangler.toml` so logs persist for 7 days
on the free plan; no extra setup needed.

### Logpush (optional, paid plan)

For longer retention than 7 days, Cloudflare Logpush can stream the same
JSON lines to an external sink. **Requires the Workers Paid plan ($5/mo)** —
Logpush is gated to paid plans regardless of destination.

Cheapest sink that works in restricted regions: another B2 bucket reached
via S3-compatible Logpush destination (`s3://...?...&endpoint=...`). The
Cloudflare API call to register the job is documented at
<https://developers.cloudflare.com/logs/get-started/enable-destinations/r2/>;
swap the `r2://` URL for `s3://` against your B2 bucket and pass the B2
keyID/applicationKey as the access-key pair.

If staying on the free plan: `wrangler tail` for live debugging plus the
7-day dashboard window is the whole story.

## Stream layout in B2

Same path layout as before; only the bucket backend changed.

```
mahjong-telemetry/
├── games/{install_id}/{YYYY-MM-DD}/game-*.ndjson.gz
├── errors/{install_id}/{YYYY-MM-DD}/errors-*.ndjson.gz
├── findings/{install_id}/{YYYY-MM-DD}/findings-*.ndjson.gz
├── memdumps/{install_id}/{YYYY-MM-DD}/memdumps-*.ndjson.gz
├── discards/{install_id}/{YYYY-MM-DD}/emj-discards.log.gz
├── inputs/{install_id}/{YYYY-MM-DD}/emj-events.log.gz
└── sigprobes/{install_id}/{YYYY-MM-DD}/sigprobe-*.ndjson.gz
```

## Limits

- Per-request: **10 MB** (rejects with 413).
- Per-install daily: **200 MB** (rejects with 429). KV-backed rolling counter
  with a 25-hour TTL so day boundaries don't drop counts.
- B2 free tier covers ~125 active installs uploading the daily cap (2,500
  Class C ops/day shared across the bucket). Past that the cost is
  pennies — $0.004 per 10K uploads, $6/TB-month storage.

## Pulling data for analysis

```powershell
# Using the b2 CLI:
b2 ls --recursive --withWrapper b2://mahjong-telemetry/memdumps/$INSTALL_ID/
b2 sync b2://mahjong-telemetry/memdumps/$INSTALL_ID/ ./local/memdumps/

# Or with any S3-compatible tool. AWS CLI example:
$env:AWS_ACCESS_KEY_ID = "<keyID>"
$env:AWS_SECRET_ACCESS_KEY = "<applicationKey>"
aws s3 ls --endpoint-url https://s3.us-west-002.backblazeb2.com `
    s3://mahjong-telemetry/memdumps/$INSTALL_ID/
aws s3 sync --endpoint-url https://s3.us-west-002.backblazeb2.com `
    s3://mahjong-telemetry/memdumps/$INSTALL_ID/ ./local/memdumps/
```

## Disabling the pipeline

Edit `telemetry-endpoint.json` and set `enabled: false`. On next plugin
launch, every install picks up the change and stops uploading. Files
already on disk locally remain — they ship on the next launch where
`enabled` flips back to true.

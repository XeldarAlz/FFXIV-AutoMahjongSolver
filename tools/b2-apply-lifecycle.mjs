// Apply server/b2-lifecycle.json to the mahjong-telemetry bucket via
// the S3-compatible PutBucketLifecycleConfiguration call. Avoids the b2
// CLI dependency — uses aws4fetch (already in server/node_modules).
//
// Requires a B2 application key with the `writeBuckets` capability AND
// at-minimum read access on the bucket. The standing
// `mahjong-analysis-read` key in .local/secrets.ps1 does NOT have
// writeBuckets — generate a temporary key at:
//   https://secure.backblaze.com/app_keys.htm
//   - Bucket: mahjong-telemetry
//   - Capabilities: listFiles, readFiles, writeBucketRetentions,
//                   writeBucketEncryption, listBuckets, readBuckets,
//                   writeBuckets
//   - Save the keyID + applicationKey
// Set the env vars temporarily, run this tool, revoke the key.
//
// Usage:
//   $env:B2_KEY_ID = "<write-key-id>"
//   $env:B2_APPLICATION_KEY = "<write-key-secret>"
//   node tools/b2-apply-lifecycle.mjs              # dry-run: show XML, no PUT
//   node tools/b2-apply-lifecycle.mjs --apply       # actually PUT to B2

import { AwsClient } from "../server/node_modules/aws4fetch/dist/aws4fetch.esm.mjs";
import { request as undiciRequest } from "../server/node_modules/undici/index.js";
import { readFile } from "node:fs/promises";

const REGION = "eu-central-003";
const ENDPOINT = "https://s3.eu-central-003.backblazeb2.com";
const BUCKET = "mahjong-telemetry";

const apply = process.argv.includes("--apply");

const keyId = process.env.B2_KEY_ID;
const appKey = process.env.B2_APPLICATION_KEY;
if (!keyId || !appKey) {
  console.error("B2_KEY_ID / B2_APPLICATION_KEY must be set in env");
  process.exit(2);
}

const config = JSON.parse(await readFile("server/b2-lifecycle.json", "utf8"));
if (!Array.isArray(config.lifecycleRules)) {
  console.error("server/b2-lifecycle.json has no lifecycleRules array");
  process.exit(2);
}

// Translate B2's native rule shape into the S3 LifecycleConfiguration XML.
// B2 documents: daysFromUploadingToHiding triggers a hide (delete-marker
// in versioned terms), daysFromHidingToDeleting then prunes. In S3 terms:
// Expiration.Days = daysFromUploadingToHiding; NoncurrentVersionExpiration
// .NoncurrentDays = daysFromHidingToDeleting (the +1 day for "hide-then-delete").
function escapeXml(s) {
  return String(s).replace(/[<>&'"]/g, (c) => ({
    "<": "&lt;", ">": "&gt;", "&": "&amp;", "'": "&apos;", "\"": "&quot;",
  })[c]);
}

const rules = config.lifecycleRules.map((r, i) => {
  const id = `rule-${i}-${r.fileNamePrefix.replace(/\W+/g, "-").replace(/-$/, "")}`;
  const expirationDays = r.daysFromUploadingToHiding;
  const noncurrentDays = r.daysFromHidingToDeleting;
  return `
    <Rule>
      <ID>${escapeXml(id)}</ID>
      <Status>Enabled</Status>
      <Filter>
        <Prefix>${escapeXml(r.fileNamePrefix)}</Prefix>
      </Filter>
      <Expiration>
        <Days>${expirationDays}</Days>
      </Expiration>
      <NoncurrentVersionExpiration>
        <NoncurrentDays>${noncurrentDays}</NoncurrentDays>
      </NoncurrentVersionExpiration>
    </Rule>`;
}).join("");

const xml = `<?xml version="1.0" encoding="UTF-8"?>
<LifecycleConfiguration xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
${rules}
</LifecycleConfiguration>`;

console.log("Lifecycle XML to apply:");
console.log(xml);
console.log();

if (!apply) {
  console.log("Dry-run only. Re-run with --apply to PUT to B2.");
  process.exit(0);
}

const aws = new AwsClient({
  accessKeyId: keyId,
  secretAccessKey: appKey,
  service: "s3",
  region: REGION,
});

const url = `${ENDPOINT}/${BUCKET}?lifecycle`;
const signed = await aws.sign(url, {
  method: "PUT",
  headers: {
    "Content-Type": "application/xml",
  },
  body: xml,
});
const headers = {};
for (const [k, v] of signed.headers.entries()) headers[k] = v;
const res = await undiciRequest(signed.url, {
  method: "PUT",
  headers,
  body: xml,
});
const chunks = [];
for await (const c of res.body) chunks.push(c);
const responseBody = Buffer.concat(chunks).toString("utf8");

console.log(`HTTP ${res.statusCode}`);
console.log(responseBody);
if (res.statusCode >= 300) {
  console.error("PUT failed.");
  process.exit(1);
}
console.log("\nLifecycle rules applied. B2 starts evaluating within ~24 h.");

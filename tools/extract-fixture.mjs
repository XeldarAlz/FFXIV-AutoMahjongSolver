// Convert one telemetry memdump record into a Track 0 replay fixture.
//
// Usage:
//   node tools/extract-fixture.mjs <memdumps.ndjson[.gz]> <seq> [--name <fixture-name>] [--out <dir>]
//
// What gets emitted:
//   - addon_memory_base64: re-encoded from the dump's addon_b64
//   - atk_values: decoded AtkValue[] from atk_b64 + atk_count
//     - Int / UInt / Bool slots get their value
//     - String / String8 / ManagedString slots emit type only (string field is null;
//       telemetry captures the pointer, not the bytes the pointer dereferences to)
//   - call_modal_visible: defaulted to true when state code matches a known call-prompt
//     state for the variant. Tweak by hand if wrong.
//   - expected.state_code: derived from atkValues[0]
//   - Remaining expected.* fields: null. Fill in by hand before committing.
//
// The fixture is written to the project's Replay/fixtures/ unless --out is given.
//
// Layouts:
//   data/layouts/<lower(variant)>.json (e.g. emj.json, emj_l.json) — loaded for
//   the state-code lookup. If you add a new variant, add it to KNOWN_VARIANTS.

import { readFile, writeFile, mkdir } from "node:fs/promises";
import { dirname, join, basename, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { gunzipSync } from "node:zlib";

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(__dirname, "..");
const DEFAULT_OUT = join(
  REPO_ROOT,
  "tests",
  "Mahjong.Plugin.Dalamud.Tests",
  "Replay",
  "fixtures",
);

// Integer values printed by AtkValueTypeProbe in tests/Mahjong.Plugin.Dalamud.Tests/Replay/.
const ATK_TYPES = {
  0: "Undefined",
  1: "Null",
  2: "Bool",
  3: "Int",
  4: "Int64",
  5: "UInt",
  6: "UInt64",
  7: "Float",
  8: "String",
  9: "WideString",
  10: "String8",
  11: "Vector",
  32: "Managed",
  40: "ManagedString",
  43: "ManagedVector",
};
const SIZEOF_ATKVALUE = 16; // Type:int32 at +0, Value union at +8

const KNOWN_VARIANTS = {
  Emj: "emj.json",
  EmjL: "emj_l.json",
};

function parseArgs(argv) {
  const positional = [];
  const opts = { name: null, out: DEFAULT_OUT };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--name") opts.name = argv[++i];
    else if (a === "--out") opts.out = argv[++i];
    else if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
    else positional.push(a);
  }
  return { positional, opts };
}

const { positional, opts } = parseArgs(process.argv.slice(2));
if (positional.length !== 2) {
  console.error("Usage: node tools/extract-fixture.mjs <memdumps.ndjson[.gz]> <seq> [--name <fixture-name>] [--out <dir>]");
  process.exit(2);
}
const [ndjsonPath, seqArg] = positional;
const targetSeq = Number.parseInt(seqArg, 10);
if (!Number.isFinite(targetSeq)) {
  console.error(`seq must be an integer, got: ${seqArg}`);
  process.exit(2);
}

const raw = await readFile(ndjsonPath);
const body = raw[0] === 0x1f && raw[1] === 0x8b ? gunzipSync(raw) : raw;
const lines = body.toString("utf8").split("\n").filter(Boolean);

let record = null;
for (const line of lines) {
  let r;
  try { r = JSON.parse(line); } catch { continue; }
  if (r?.seq === targetSeq) { record = r; break; }
}
if (!record) {
  console.error(`No record with seq=${targetSeq} in ${ndjsonPath} (${lines.length} records scanned)`);
  process.exit(1);
}
if (!record.addon_b64) {
  console.error(`Record seq=${targetSeq} has no addon_b64 — cannot build fixture`);
  process.exit(1);
}

const variant = record.variant || "Emj";
const layoutFile = KNOWN_VARIANTS[variant] ?? KNOWN_VARIANTS.Emj;
const profilePath = join(REPO_ROOT, "data", "layouts", layoutFile);
let profile = null;
try { profile = JSON.parse(await readFile(profilePath, "utf8")); }
catch (e) { console.warn(`[warn] could not load ${profilePath}: ${e.message}`); }

const atkValues = decodeAtkValues(record.atk_b64, record.atk_count ?? 0);
const stateCode = atkValues[0]?.int ?? null;

const callPromptStates = profile
  ? [
      profile.stateCodes?.callPrompt,
      profile.stateCodes?.callPromptList,
      profile.stateCodes?.selfDeclareList,
    ].filter((s) => typeof s === "number")
  : [];
const callModalVisible = stateCode !== null && callPromptStates.includes(stateCode);

const fixtureName = opts.name
  ?? `${variant.toLowerCase()}_seq${targetSeq}_${record.reason ?? "unk"}`.replace(/[^a-z0-9_-]/gi, "_");

const fixture = {
  name: fixtureName,
  description: `Extracted from ${basename(ndjsonPath)} seq=${targetSeq} reason=${record.reason}. Fill in expected.* fields before committing.`,
  variant,
  addon_memory_base64: record.addon_b64,
  atk_values: atkValues.map(slotToFixtureForm),
  call_modal_visible: callModalVisible,
  list_widget_labels: null,
  expected: {
    state_code: stateCode,
    hand: null,
    legal_flags: null,
    score_self: null,
    wall_remaining: null,
    aka_dora: null,
    meld_count: null,
  },
};

await mkdir(opts.out, { recursive: true });
const outPath = join(opts.out, `${fixtureName}.json`);
await writeFile(outPath, JSON.stringify(fixture, null, 2) + "\n");

console.log(`[extract-fixture] wrote ${outPath}`);
console.log(`  variant=${variant}  stateCode=${stateCode}  reason=${record.reason}  atkCount=${atkValues.length}`);
console.log(`  Edit the file to populate expected.* fields, then run the test suite.`);

function decodeAtkValues(b64, count) {
  if (!b64 || count <= 0) return [];
  const buf = Buffer.from(b64, "base64");
  const out = [];
  for (let i = 0; i < count; i++) {
    const off = i * SIZEOF_ATKVALUE;
    if (off + SIZEOF_ATKVALUE > buf.length) break;
    const typeCode = buf.readInt32LE(off);
    const typeName = ATK_TYPES[typeCode] ?? `Unknown(${typeCode})`;
    const slot = { type: typeName };
    switch (typeName) {
      case "Bool":
        slot.bool = buf.readUInt8(off + 8) !== 0;
        break;
      case "Int":
        slot.int = buf.readInt32LE(off + 8);
        break;
      case "UInt":
        slot.uint = buf.readUInt32LE(off + 8);
        break;
      case "Float":
        slot.float = buf.readFloatLE(off + 8);
        break;
      case "String":
      case "String8":
      case "ManagedString":
        // The value at +8 is a pointer into the game's process — meaningless after capture.
        // Leave string null; hand-edit if the prompt's label is needed for the fixture.
        slot.string = null;
        break;
      default:
        break;
    }
    out.push(slot);
  }
  return out;
}

function slotToFixtureForm(slot) {
  const out = { type: slot.type };
  if ("int" in slot && slot.int !== undefined) out.int = slot.int;
  if ("uint" in slot && slot.uint !== undefined) out.uint = slot.uint;
  if ("bool" in slot && slot.bool !== undefined) out.bool = slot.bool;
  if ("string" in slot) out.string = slot.string;
  return out;
}

"""
Compare memdumps across distinct hands to find offsets that:
  - Hold small ints (0..3 candidates for seat/wind/dealer)
  - Differ between hands (so they're game-state, not constants)

Run: python tools/find_seat_offsets.py

Reads memdumps from %APPDATA%/XIVLauncher/pluginConfigs/Mahjong.Plugin.Dalamud/memdumps/
and reports candidate offsets for OurSeat / RoundWind / DealerSeat / Honba.
"""
import json
import base64
import struct
import os
import glob
from collections import defaultdict

MEMDUMP_DIR = os.path.expandvars(
    r"%APPDATA%\XIVLauncher\pluginConfigs\Mahjong.Plugin.Dalamud\memdumps"
)

# Pick one snapshot per hand-start period (hand-rolling happens at wall jumps).
# We approximate by reading the FIRST memdump after each hand-start moment
# in the corresponding game-*-handNN.ndjson file.

GAMES_DIR = os.path.expandvars(
    r"%APPDATA%\XIVLauncher\pluginConfigs\Mahjong.Plugin.Dalamud\games"
)


def load_hand_starts():
    starts = []
    for path in sorted(glob.glob(os.path.join(GAMES_DIR, "game-20260509-083*.ndjson"))):
        with open(path) as f:
            for line in f:
                d = json.loads(line)
                if d.get("e") == "hand-start":
                    starts.append((d["t"], path))
                    break
    return starts


def find_first_memdump_at_or_after(timestamp):
    files = sorted(glob.glob(os.path.join(MEMDUMP_DIR, "memdumps-20260509-08*.ndjson")))
    for path in files:
        with open(path) as f:
            for line in f:
                d = json.loads(line)
                if d["t"] >= timestamp and d.get("addon_b64"):
                    return d
    return None


def extract_int_offsets(addon_bytes, max_offset=0x1300):
    out = {}
    for off in range(0, min(max_offset, len(addon_bytes) - 3), 4):
        v = struct.unpack_from("<i", addon_bytes, off)[0]
        out[off] = v
    return out


def extract_byte_offsets(addon_bytes, max_offset=0x1300):
    out = {}
    for off in range(0, min(max_offset, len(addon_bytes))):
        out[off] = addon_bytes[off]
    return out


def extract_atk_ints(atk_bytes, count):
    out = {}
    for i in range(count):
        offset = i * 16  # AtkValue stride
        if offset + 16 > len(atk_bytes):
            break
        tp = atk_bytes[offset]
        v = struct.unpack_from("<i", atk_bytes, offset + 8)[0]
        if tp == 3:  # Int
            out[i] = v
    return out


def main():
    starts = load_hand_starts()
    print(f"Found {len(starts)} hand starts:")
    for t, path in starts:
        print(f"  {t}  {os.path.basename(path)}")
    print()

    snapshots = []  # [(timestamp, addon_ints, addon_bytes, atk_ints)]
    for t, _ in starts:
        d = find_first_memdump_at_or_after(t)
        if d is None:
            print(f"  no memdump at or after {t}")
            continue
        addon_bytes = base64.b64decode(d.get("addon_b64", ""))
        atk_bytes = base64.b64decode(d.get("atk_b64", ""))
        addon_ints = extract_int_offsets(addon_bytes)
        addon_byte_offs = extract_byte_offsets(addon_bytes)
        atk_ints = extract_atk_ints(atk_bytes, d.get("atk_count", 0))
        snapshots.append((t, addon_ints, addon_byte_offs, atk_ints))
        print(f"hand at {t}: addon ints sampled, atk_count={d.get('atk_count')}")
    print()

    common_offsets = set(snapshots[0][1].keys())
    for _, addon, _, _ in snapshots[1:]:
        common_offsets &= set(addon.keys())

    int_candidates = []
    for off in sorted(common_offsets):
        values = [s[1][off] for s in snapshots]
        if not all(0 <= v <= 7 for v in values):
            continue
        if len(set(values)) <= 1:
            continue
        int_candidates.append((off, values))

    print(f"Addon int-offset candidates (small ints 0..7 that vary across hands): {len(int_candidates)}")
    for off, values in int_candidates[:80]:
        print(f"  +0x{off:04X}: {values}")
    print()

    common_byte_offs = set(snapshots[0][2].keys())
    for _, _, b, _ in snapshots[1:]:
        common_byte_offs &= set(b.keys())

    byte_candidates = []
    for off in sorted(common_byte_offs):
        values = [s[2][off] for s in snapshots]
        if not all(0 <= v <= 7 for v in values):
            continue
        if len(set(values)) <= 1:
            continue
        byte_candidates.append((off, values))

    print(f"Addon byte-offset candidates (0..7, vary across hands): {len(byte_candidates)}")
    # Skip dense floods (every byte adjacent flagged); only show offsets whose
    # int-aligned neighbor isn't already in the int candidates.
    int_offs = {o for o, _ in int_candidates}
    sparse = [c for c in byte_candidates if (c[0] & ~3) not in int_offs]
    for off, values in sparse[:80]:
        print(f"  +0x{off:04X}: {values}")
    print()

    common_indices = set(snapshots[0][3].keys())
    for _, _, _, atk in snapshots[1:]:
        common_indices &= set(atk.keys())
    atk_candidates = []
    for i in sorted(common_indices):
        values = [s[3][i] for s in snapshots]
        if not all(0 <= v <= 7 for v in values):
            continue
        if len(set(values)) <= 1:
            continue
        atk_candidates.append((i, values))

    print(f"AtkValue-index candidates: {len(atk_candidates)}")
    for i, values in atk_candidates[:50]:
        print(f"  atk[{i:3d}]: {values}")


if __name__ == "__main__":
    main()

#!/bin/sh
# WW3MOD tournament harness — compare two batch summaries side by side.
#
# Use cases:
#   1. A/B testing AI changes: run normal-vs-normal as baseline, run
#      v2-vs-normal as candidate, compare to detect winrate shifts.
#   2. Mirror-paired bias diagnosis: run primary scenario, run mirror
#      scenario (factions swapped), compare to attribute bias to
#      faction vs position.
#   3. Cross-map validation: same matchup on two different maps —
#      bias appearing in both = AI/faction; only one = map.
#
# Usage:
#   ./tools/autotest/compare-batches.sh <batch-dir-A> <batch-dir-B>
#
# Output: side-by-side report with side winrate, score ratio stats,
# match duration stats, and faction attribution hints when one batch is
# a mirror of the other.

set -e

A="$1"
B="$2"

if [ -z "${A}" ] || [ -z "${B}" ] || [ ! -d "${A}" ] || [ ! -d "${B}" ]; then
	cat <<EOF
Usage: $0 <batch-dir-A> <batch-dir-B>

  Both directories must contain summary.json (produced by
  aggregate-tournament.sh). If aggregator hasn't been run, do:

    ./tools/autotest/aggregate-tournament.sh <batch-dir>

  before this comparator.
EOF
	exit 3
fi

if [ ! -f "${A}/summary.json" ] || [ ! -f "${B}/summary.json" ]; then
	echo "Error: summary.json missing in one or both dirs."
	echo "Run aggregate-tournament.sh on each first."
	exit 3
fi

PYTHON=$(command -v python3 || command -v python)
[ -z "${PYTHON}" ] && { echo "Error: python3 not found."; exit 3; }

"${PYTHON}" - "${A}" "${B}" <<'PY'
import json
import os
import sys

def load(p):
    with open(os.path.join(p, "summary.json")) as f:
        return json.load(f)

def meta(p):
    try:
        with open(os.path.join(p, "batch.meta.json")) as f:
            return json.load(f)
    except FileNotFoundError:
        return {}

A_path, B_path = sys.argv[1], sys.argv[2]
A, B = load(A_path), load(B_path)
A_meta, B_meta = meta(A_path), meta(B_path)

def label(meta, fallback):
    s = meta.get("scenario", "")
    return s if s else fallback

def pct(d, key):
    return d.get("side_winrate_pct", {}).get(key, 0.0)

print("=" * 70)
print(f"BATCH A: {label(A_meta, os.path.basename(A_path.rstrip('/')))}")
print(f"BATCH B: {label(B_meta, os.path.basename(B_path.rstrip('/')))}")
print("=" * 70)
print()

print("SIDE WINRATE (by player name)")
print("-" * 70)
print(f"{'Player':<20} {'Batch A %':>12} {'Batch B %':>12} {'Delta':>10}")
all_players = set(A.get("side_winrate_pct", {}).keys()) | set(B.get("side_winrate_pct", {}).keys())
for name in sorted(all_players):
    a, b = pct(A, name), pct(B, name)
    delta = b - a
    arrow = " up" if delta > 5 else " dn" if delta < -5 else " ~~"
    print(f"{name:<20} {a:>11.1f}% {b:>11.1f}% {delta:>+9.1f}{arrow}")
print()

# Faction-keyed winrate (when verdict JSON has the faction field).
def fpct(d, key):
    return d.get("faction_winrate_pct", {}).get(key, 0.0)

a_factions = set(A.get("faction_winrate_pct", {}).keys())
b_factions = set(B.get("faction_winrate_pct", {}).keys())
all_factions = a_factions | b_factions
if all_factions:
    print("FACTION WINRATE")
    print("-" * 70)
    print(f"{'Faction':<20} {'Batch A %':>12} {'Batch B %':>12} {'Delta':>10}")
    for faction in sorted(all_factions):
        a, b = fpct(A, faction), fpct(B, faction)
        delta = b - a
        arrow = " up" if delta > 5 else " dn" if delta < -5 else " ~~"
        print(f"{faction:<20} {a:>11.1f}% {b:>11.1f}% {delta:>+9.1f}{arrow}")
    print()
else:
    print("FACTION WINRATE: (not available — verdicts lack 'faction' field;")
    print("                 add it via Round 15 engine change + rebuild)")
    print()

print("MATCH COUNTS")
print("-" * 70)
print(f"{'':<24} {'A':>10} {'B':>10}")
for k in ("total_matches", "verdict_count", "fail_count",
         "sr_capture_count", "time_limit_count", "decisive_count"):
    print(f"{k:<24} {A.get(k, 0):>10} {B.get(k, 0):>10}")
print()

print("SCORE RATIO (winner / loser)")
print("-" * 70)
print(f"{'':<24} {'A':>10} {'B':>10}")
sa, sb = A.get("score_ratio_stats") or {}, B.get("score_ratio_stats") or {}
for k in ("n", "mean", "median", "min", "max"):
    va, vb = sa.get(k, 0), sb.get(k, 0)
    if isinstance(va, float):
        print(f"{k:<24} {va:>10.3f} {vb:>10.3f}")
    else:
        print(f"{k:<24} {va:>10} {vb:>10}")
print()

print("DURATION (ticks)")
print("-" * 70)
print(f"{'':<24} {'A':>10} {'B':>10}")
da, db = A.get("duration_ticks_stats") or {}, B.get("duration_ticks_stats") or {}
for k in ("n", "mean", "median", "min", "max"):
    va, vb = da.get(k, 0), db.get(k, 0)
    if isinstance(va, float):
        print(f"{k:<24} {va:>10.1f} {vb:>10.1f}")
    else:
        print(f"{k:<24} {va:>10} {vb:>10}")
print()

print("INTERPRETATION HINTS")
print("-" * 70)

def is_mirror_pair(a_meta, b_meta):
    a_s, b_s = a_meta.get("scenario", ""), b_meta.get("scenario", "")
    return ("mirror" in a_s) != ("mirror" in b_s)

if is_mirror_pair(A_meta, B_meta):
    print("Looks like a mirror-paired batch.")
    print("If both batches show the SAME player name winning, the bias is")
    print("POSITIONAL (the side at that position has an advantage regardless")
    print("of faction).")
    print("If the winning player name FLIPS between A and B, the bias is")
    print("FACTIONAL (the same faction wins regardless of side it sits on).")
else:
    print("Not a mirror pair. Delta winrate above represents a real change")
    print("between batches — AI/bot changes between commits, different maps,")
    print("or different configs. Check batch.meta.json for both runs to see")
    print("what differs.")

print()
print("=" * 70)
PY

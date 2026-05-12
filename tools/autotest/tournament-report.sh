#!/bin/sh
# WW3MOD tournament harness — one-line batch summary.
#
# Useful for pasting into commit messages or quick "did this change help?"
# inspections. Reads <batch-dir>/summary.json and prints a single line.
#
# Usage:
#   ./tools/autotest/tournament-report.sh <batch-dir>
#
# Example output:
#   batch=260512_0837 n=19 USA-bot=84.2% Russia-bot=15.8% mean-ratio=1.70 (america=84.2% russia=15.8%)

set -e

BATCH="$1"
if [ -z "${BATCH}" ] || [ ! -d "${BATCH}" ]; then
	echo "Usage: $0 <batch-dir>"
	exit 3
fi

if [ ! -f "${BATCH}/summary.json" ]; then
	echo "Error: summary.json missing in ${BATCH}. Run aggregate-tournament.sh first."
	exit 3
fi

PYTHON=$(command -v python3 || command -v python)
[ -z "${PYTHON}" ] && { echo "Error: python3 not found."; exit 3; }

"${PYTHON}" - "${BATCH}" <<'PY'
import json, os, sys

p = sys.argv[1]
with open(os.path.join(p, "summary.json")) as f:
    s = json.load(f)

batch_name = os.path.basename(p.rstrip("/"))
# Trim away the tournament scenario suffix for brevity (260512_0837_tournament-... → 260512_0837)
short_ts = batch_name.split("_tournament", 1)[0]

n = s.get("verdict_count", 0)
fails = s.get("fail_count", 0)
mean_ratio = (s.get("score_ratio_stats") or {}).get("mean", 0)

side = s.get("side_winrate_pct", {})
side_str = " ".join(f"{k}={v:.1f}%" for k, v in sorted(side.items()))

faction = s.get("faction_winrate_pct", {})
if faction:
    fac_str = " (" + " ".join(f"{k}={v:.1f}%" for k, v in sorted(faction.items())) + ")"
else:
    fac_str = ""

fail_str = f" fails={fails}" if fails else ""
print(f"batch={short_ts} n={n}{fail_str} {side_str} mean-ratio={mean_ratio:.2f}{fac_str}")
PY

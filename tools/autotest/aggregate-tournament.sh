#!/bin/sh
# Tournament batch aggregator.
#
# Reads every match_*.json in a result dir, produces summary.csv (one row per
# match) and summary.json (aggregate stats).
#
# Usage: ./tools/autotest/aggregate-tournament.sh <result-dir>
#
# The per-match JSON is what TestMode.WriteResult emits — its `notes` field
# contains an escaped JSON blob with the actual verdict (see SerializeVerdict in
# BotVsBotMatchWatcher.cs). We unwrap the notes here.

set -e

RESULT_DIR="$1"
if [ -z "${RESULT_DIR}" ] || [ ! -d "${RESULT_DIR}" ]; then
	echo "Usage: $0 <result-dir>"
	exit 3
fi

# CSV header.
CSV="${RESULT_DIR}/summary.csv"
echo "match,status,winner,reason,duration_ticks,p1_name,p1_score,p1_army,p1_bot,p2_name,p2_score,p2_army,p2_bot" > "${CSV}"

OK=0
SR_CAPTURES=0
TIME_LIMITS=0
SKIPPED=0
P1_WINS=0
P2_WINS=0
TOTAL_TICKS=0

# Note: this script uses python3 for JSON parsing — jq isn't guaranteed to be
# installed on every dev macOS but python3 is part of the system. If python3
# becomes a problem, a portable jq drop-in is the migration path.
PYTHON=$(command -v python3 || command -v python)
if [ -z "${PYTHON}" ]; then
	echo "Error: python3 not found; required for aggregation."
	exit 3
fi

for match_file in "${RESULT_DIR}"/match_*.json; do
	[ -f "${match_file}" ] || continue
	match_name=$(basename "${match_file}" .json)

	# Use python to extract + flatten — handles escaped JSON-in-notes properly.
	row=$("${PYTHON}" - "${match_file}" "${match_name}" <<'PY' 2>/dev/null || echo ""
import json
import sys

path, match_name = sys.argv[1], sys.argv[2]
with open(path) as f:
    outer = json.load(f)

status = outer.get("status", "unknown")
notes = outer.get("notes", "")

# `notes` is a JSON-encoded verdict blob (or a plain string on init failure).
verdict = None
try:
    verdict = json.loads(notes)
except Exception:
    pass

if not isinstance(verdict, dict):
    print(f"{match_name},{status},,,,,,,,,,,")
    sys.exit(0)

players = verdict.get("players", [])
p1 = players[0] if len(players) > 0 else {}
p2 = players[1] if len(players) > 1 else {}

def comp(p, key): return p.get("score_components", {}).get(key, 0)

winner_idx = verdict.get("winner_client_index", -1)
winner_name = verdict.get("winner_name", "")
reason = verdict.get("win_reason", "")
duration = verdict.get("duration_ticks", 0)

print(",".join(str(x) for x in [
    match_name,
    status,
    winner_name,
    reason,
    duration,
    p1.get("name", ""),
    p1.get("score_total", 0),
    comp(p1, "army_value"),
    p1.get("bot_type", ""),
    p2.get("name", ""),
    p2.get("score_total", 0),
    comp(p2, "army_value"),
    p2.get("bot_type", ""),
]))
PY
	)

	if [ -n "${row}" ]; then
		echo "${row}" >> "${CSV}"
	fi
done

# Build summary.json — re-walk the matches with python for aggregate.
"${PYTHON}" - "${RESULT_DIR}" <<'PY' > "${RESULT_DIR}/summary.json"
import glob, json, os, sys, statistics

result_dir = sys.argv[1]

matches = []
for f in sorted(glob.glob(os.path.join(result_dir, "match_*.json"))):
    with open(f) as fh:
        outer = json.load(fh)
    notes = outer.get("notes", "")
    try:
        verdict = json.loads(notes)
    except Exception:
        verdict = None
    matches.append({"file": os.path.basename(f), "status": outer.get("status"), "verdict": verdict})

decisive_count = 0
sr_cap_count = 0
time_limit_count = 0
fail_count = 0

winner_names = {}
durations = []
p1_scores = []
p2_scores = []

for m in matches:
    v = m["verdict"]
    if not v:
        fail_count += 1
        continue

    reason = v.get("win_reason", "")
    if reason == "sr_capture":
        sr_cap_count += 1
        decisive_count += 1
    elif reason == "time_limit":
        time_limit_count += 1

    durations.append(v.get("duration_ticks", 0))

    players = v.get("players", [])
    if len(players) >= 1:
        p1_scores.append(players[0].get("score_total", 0))
    if len(players) >= 2:
        p2_scores.append(players[1].get("score_total", 0))

    w = v.get("winner_name", "")
    if w:
        winner_names[w] = winner_names.get(w, 0) + 1

def stats(xs):
    if not xs:
        return None
    return {
        "n": len(xs),
        "mean": statistics.mean(xs),
        "median": statistics.median(xs),
        "min": min(xs),
        "max": max(xs),
    }

# Winrate analysis. With deterministic seeding (Test.RandomSeed) and
# legacy-vs-legacy bots, winrate should land in the 40-60% noise band per
# side. Skews outside that suggest map / faction / scenario bias.
total_with_verdict = len(matches) - fail_count
side_winrate_pct = {}
if winner_names and total_with_verdict > 0:
    for name, count in winner_names.items():
        side_winrate_pct[name] = round(100.0 * count / total_with_verdict, 1)

# Score-gap distribution (winner_score / loser_score) — a sanity check on
# whether matches are decisive (>1.5×) or close. Lots of close matches = the
# benchmark is sensitive; lots of decisive ones = bias or AI-vs-bystander.
score_ratios = []
for m in matches:
    v = m["verdict"]
    if not v:
        continue
    players = v.get("players", [])
    if len(players) < 2:
        continue
    s1 = players[0].get("score_total", 0)
    s2 = players[1].get("score_total", 0)
    hi = max(s1, s2)
    lo = max(min(s1, s2), 1)  # avoid div-by-zero
    score_ratios.append(hi / lo)

summary = {
    "total_matches": len(matches),
    "verdict_count": total_with_verdict,
    "fail_count": fail_count,
    "side_winrate_pct": side_winrate_pct,
    "score_ratio_stats": stats(score_ratios),
    "sr_capture_count": sr_cap_count,
    "time_limit_count": time_limit_count,
    "decisive_count": decisive_count,
    "winner_counts": winner_names,
    "duration_ticks_stats": stats(durations),
    "p1_score_stats": stats(p1_scores),
    "p2_score_stats": stats(p2_scores),
}
print(json.dumps(summary, indent=2))
PY

echo "==> Aggregate written:"
echo "      ${CSV}"
echo "      ${RESULT_DIR}/summary.json"
echo
echo "==> Quick view:"
head -1 "${CSV}"
tail -n +2 "${CSV}" | head -20
echo
echo "    (full CSV: ${CSV})"

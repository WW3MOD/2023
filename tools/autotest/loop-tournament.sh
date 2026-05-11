#!/bin/sh
# WW3MOD AI tournament harness — autonomous milestone-driven loop runner.
#
# Drives long unattended runs (overnight, multi-hour, full-day). Each round runs
# a batch of N matches, aggregates, evaluates milestone triggers, and stops on
# goal-met or budget-exhausted.
#
# Usage:
#   ./tools/autotest/loop-tournament.sh <scenario> <target.yaml>
#
# Target schema (YAML, MiniYaml-style):
#
#   Scenario: tournament-arena-skirmish-2p
#   Config:   tools/autotest/scenarios/tournament-arena-skirmish-2p/tournament-sanity.yaml
#   BatchSize: 10            # matches per round
#   BudgetHours: 8           # max wall-clock for the whole loop
#   StopWhen:
#       Metric: v2_winrate   # one of: v2_winrate, p1_winrate, perf_avg_tickrate
#       Op:     ">="          # >=, <=, ==
#       Value:  0.60
#   Milestones:
#       - Name: winrate_above_50
#         Condition: v2_winrate > 0.50
#       - Name: winrate_below_30
#         Condition: v2_winrate < 0.30
#       - Name: perf_regression
#         Condition: perf_avg_tickrate < 100
#
# Notes:
# - Each round commits its findings to git as a milestone marker. Reverting any
#   single round is safe; the next round starts fresh from current HEAD.
# - StopWhen and Milestones are read by aggregate-tournament-loop.py (TBD;
#   stub for now — Phase 4 deliverable). Loop just orchestrates the batches
#   and writes a round-by-round summary log.
# - The loop never pushes to remote and never modifies the git config.
#
# Phase 4 status: scaffold only. The condition-evaluation + bell-the-user logic
# is documented but not yet implemented — that's the next session's work.

set -e

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

SCENARIO="$1"
TARGET="$2"

if [ -z "${SCENARIO}" ] || [ -z "${TARGET}" ]; then
	cat <<EOF
Usage: $0 <scenario> <target.yaml>
EOF
	exit 3
fi

if [ ! -d "tools/autotest/scenarios/${SCENARIO}" ]; then
	echo "Error: scenario not found: ${SCENARIO}"
	exit 3
fi

if [ ! -f "${TARGET}" ]; then
	echo "Error: target file not found: ${TARGET}"
	exit 3
fi

# Parse target via awk (no MiniYaml CLI on the shell side yet; this is enough
# for the scaffold).
CONFIG=$(awk -F': *' '/^Config:/ { print $2; exit }' "${TARGET}")
BATCH_SIZE=$(awk -F': *' '/^BatchSize:/ { print $2; exit }' "${TARGET}")
BUDGET_HOURS=$(awk -F': *' '/^BudgetHours:/ { print $2; exit }' "${TARGET}")

[ -z "${BATCH_SIZE}" ] && BATCH_SIZE=10
[ -z "${BUDGET_HOURS}" ] && BUDGET_HOURS=8
[ -z "${CONFIG}" ] && CONFIG="tools/autotest/scenarios/${SCENARIO}/tournament.yaml"

LOOP_TS=$(date +"%y%m%d_%H%M")
LOOP_DIR="tools/autotest/tournament-loops/${LOOP_TS}_${SCENARIO}"
mkdir -p "${LOOP_DIR}"

BUDGET_SECS=$((BUDGET_HOURS * 3600))
START_TS=$(date +%s)

echo "============================================================"
echo "Loop:        ${LOOP_TS}"
echo "Scenario:    ${SCENARIO}"
echo "Config:      ${CONFIG}"
echo "BatchSize:   ${BATCH_SIZE} matches/round"
echo "Budget:      ${BUDGET_HOURS}h (${BUDGET_SECS}s)"
echo "Target:      ${TARGET}"
echo "Output dir:  ${LOOP_DIR}"
echo "============================================================"

# Loop body — one round per iteration. The shell scaffold is intentionally
# simple. Real condition-evaluation comes later (Phase 4 v2 — TODO):
#   - Parse summary.json from each round, extract metrics.
#   - Compare against StopWhen condition; exit on hit.
#   - Evaluate Milestones; on each hit, write a milestone_<name>_<round>.md
#     and ring the terminal bell.
#
# For tonight's scaffold: just run rounds until budget exhausted or 5 rounds
# done, whichever comes first. Each round commits its results.

ROUND=0
MAX_ROUNDS=5
while [ ${ROUND} -lt ${MAX_ROUNDS} ]; do
	ROUND=$((ROUND + 1))
	NOW=$(date +%s)
	ELAPSED=$((NOW - START_TS))
	REMAINING=$((BUDGET_SECS - ELAPSED))

	if [ ${REMAINING} -le 0 ]; then
		echo
		echo "==> Budget exhausted after ${ROUND} rounds. Stopping."
		break
	fi

	echo
	echo "============================================================"
	echo "Round ${ROUND} / ${MAX_ROUNDS} — elapsed ${ELAPSED}s, remaining ${REMAINING}s"
	echo "============================================================"

	ROUND_DIR="${LOOP_DIR}/round_${ROUND}"
	mkdir -p "${ROUND_DIR}"

	./tools/autotest/run-tournament.sh "${SCENARIO}" \
		--seeds "${BATCH_SIZE}" \
		--config "${CONFIG}" \
		--result-dir "${ROUND_DIR}" 2>&1 | tee "${ROUND_DIR}/run.log"

	# Per-round commit so each round is a real attribution point. Skip if
	# nothing's actually changed (round results are gitignored; the loop's
	# round dir is also gitignored). A real implementation here would commit a
	# round-summary markdown to a non-gitignored path.

	echo "==> Round ${ROUND} complete. Results: ${ROUND_DIR}"

	# Stop condition placeholder. Real eval comes in Phase 4 v2.
	echo "    (stop condition + milestone eval not yet implemented; continuing.)"
done

echo
echo "============================================================"
echo "Loop done. ${ROUND} rounds run. Output: ${LOOP_DIR}"
echo "============================================================"

# Final aggregate across all rounds — TODO: meta-aggregator that combines
# round CSVs into a loop-wide summary.

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
#   MaxRounds: 20            # safety cap on round count (0 = unbounded)
#   StopWinner: USA-bot      # player name to track for StopThreshold (optional)
#   StopThreshold: 0.60      # stop when StopWinner's winrate >= this (0..1)
#   MaxWallSecs: 120         # per-match wall-clock budget passed to run-tournament.sh
#   MirrorScenario: ""       # optional mirror scenario for --mirror flag
#
# Behavior:
# - After each round, runs aggregate-tournament.sh + reads summary.json.
# - StopThreshold met → loop exits with success, writes goal_met.txt.
# - Budget exhausted or MaxRounds reached → loop exits, writes
#   budget_exhausted.txt or max_rounds_reached.txt.
# - Each round writes round_<N>/ with all per-match files + summary.
# - Loop-wide tracking in loop_progress.csv (round, verdicts, winrate, etc.).
# - Rings the terminal bell (printf '\a') on goal-met / budget-out / unusual
#   winrate shifts between rounds.
# - The loop never pushes to remote and never modifies git config.
#
# Phase 4 status: condition-evaluation + bell wired up. Compatible with
# the scaffold target.yaml format from earlier rounds.

set -e

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

SCENARIO="$1"
TARGET="$2"

if [ -z "${SCENARIO}" ] || [ -z "${TARGET}" ]; then
	cat <<EOF
Usage: $0 <scenario> <target.yaml>

  Run an autonomous milestone-driven loop. Each round runs a batch of N
  matches (per BatchSize), aggregates, then evaluates the stop condition.

  See tools/autotest/example-target.yaml for a schema reference.
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

# Parse target via awk. Tab-indented YAML; values are everything after the
# first ': ' separator. Default values supplied if keys missing.
# `tr -d '\r'` strips a trailing CR so a CRLF-saved target.yaml (common when
# authored on Windows) doesn't corrupt numeric compares or path values.
CONFIG=$(awk -F': *' '/^Config:/ { print $2; exit }' "${TARGET}" | tr -d '\r')
BATCH_SIZE=$(awk -F': *' '/^BatchSize:/ { print $2; exit }' "${TARGET}" | tr -d '\r')
BUDGET_HOURS=$(awk -F': *' '/^BudgetHours:/ { print $2; exit }' "${TARGET}" | tr -d '\r')
MAX_ROUNDS=$(awk -F': *' '/^MaxRounds:/ { print $2; exit }' "${TARGET}" | tr -d '\r')
STOP_WINNER=$(awk -F': *' '/^StopWinner:/ { print $2; exit }' "${TARGET}" | tr -d '\r')
STOP_THRESHOLD=$(awk -F': *' '/^StopThreshold:/ { print $2; exit }' "${TARGET}" | tr -d '\r')
MAX_WALL_SECS=$(awk -F': *' '/^MaxWallSecs:/ { print $2; exit }' "${TARGET}" | tr -d '\r')
MIRROR_SCENARIO=$(awk -F': *' '/^MirrorScenario:/ { print $2; exit }' "${TARGET}" | tr -d '\r')

[ -z "${BATCH_SIZE}" ] && BATCH_SIZE=10
[ -z "${BUDGET_HOURS}" ] && BUDGET_HOURS=8
[ -z "${MAX_ROUNDS}" ] && MAX_ROUNDS=20
[ -z "${CONFIG}" ] && CONFIG="tools/autotest/scenarios/${SCENARIO}/tournament.yaml"
[ -z "${MAX_WALL_SECS}" ] && MAX_WALL_SECS=120

LOOP_TS=$(date +"%y%m%d_%H%M")
LOOP_DIR="tools/autotest/tournament-loops/${LOOP_TS}_${SCENARIO}"
mkdir -p "${LOOP_DIR}"

BUDGET_SECS=$((BUDGET_HOURS * 3600))
START_TS=$(date +%s)

# Loop progress CSV — one row per round.
PROGRESS_CSV="${LOOP_DIR}/loop_progress.csv"
echo "round,started_at,verdict_count,fail_count,stop_winner,stop_winner_pct,prev_pct,delta_pct,elapsed_secs" > "${PROGRESS_CSV}"

echo "============================================================"
echo "Loop:        ${LOOP_TS}"
echo "Scenario:    ${SCENARIO}"
echo "Config:      ${CONFIG}"
echo "BatchSize:   ${BATCH_SIZE} matches/round"
echo "MaxRounds:   ${MAX_ROUNDS}"
echo "Budget:      ${BUDGET_HOURS}h (${BUDGET_SECS}s)"
echo "MaxWallSecs: ${MAX_WALL_SECS}s/match"
[ -n "${STOP_WINNER}" ] && echo "Stop when:   ${STOP_WINNER} >= ${STOP_THRESHOLD}"
[ -n "${MIRROR_SCENARIO}" ] && echo "Mirror:      ${MIRROR_SCENARIO}"
echo "Target:      ${TARGET}"
echo "Output dir:  ${LOOP_DIR}"
echo "============================================================"

PYTHON=$(command -v python3 || command -v python)

# Parse a numeric field from a round's summary.json.
parse_summary_field() {
	round_dir="$1"
	field="$2"
	subfield="$3"  # optional; if set, looks up nested dict key

	if [ ! -f "${round_dir}/summary.json" ]; then
		echo "0"
		return
	fi

	if [ -z "${subfield}" ]; then
		"${PYTHON}" -c "import json; print(json.load(open('${round_dir}/summary.json')).get('${field}', 0))"
	else
		"${PYTHON}" -c "import json; d=json.load(open('${round_dir}/summary.json')).get('${field}', {}); print(d.get('${subfield}', 0))"
	fi
}

# Float comparison via bash. Returns 0 (true) if $1 >= $2.
fge() {
	awk -v a="$1" -v b="$2" 'BEGIN { exit !(a+0 >= b+0) }'
}

ROUND=0
PREV_PCT="0"
STOP_REASON=""

while true; do
	if [ "${MAX_ROUNDS}" -gt 0 ] && [ "${ROUND}" -ge "${MAX_ROUNDS}" ]; then
		STOP_REASON="max_rounds_reached"
		break
	fi

	ROUND=$((ROUND + 1))
	NOW=$(date +%s)
	ELAPSED=$((NOW - START_TS))
	REMAINING=$((BUDGET_SECS - ELAPSED))

	if [ ${REMAINING} -le 0 ]; then
		STOP_REASON="budget_exhausted"
		ROUND=$((ROUND - 1))  # didn't actually run this one
		break
	fi

	echo
	echo "============================================================"
	echo "Round ${ROUND} / ${MAX_ROUNDS} — elapsed ${ELAPSED}s, remaining ${REMAINING}s"
	echo "============================================================"

	ROUND_DIR="${LOOP_DIR}/round_${ROUND}"
	mkdir -p "${ROUND_DIR}"

	MIRROR_ARG=""
	[ -n "${MIRROR_SCENARIO}" ] && MIRROR_ARG="--mirror ${MIRROR_SCENARIO}"

	./tools/autotest/run-tournament.sh "${SCENARIO}" \
		--seeds "${BATCH_SIZE}" \
		--config "${CONFIG}" \
		--result-dir "${ROUND_DIR}" \
		--max-wall-secs "${MAX_WALL_SECS}" \
		${MIRROR_ARG} 2>&1 | tee "${ROUND_DIR}/run.log" > /dev/null

	# Re-run aggregator to ensure summary.json present (run-tournament should
	# have done it already, but be defensive).
	./tools/autotest/aggregate-tournament.sh "${ROUND_DIR}" > /dev/null 2>&1 || true

	VERDICTS=$(parse_summary_field "${ROUND_DIR}" "verdict_count")
	FAILS=$(parse_summary_field "${ROUND_DIR}" "fail_count")
	CUR_PCT=0
	if [ -n "${STOP_WINNER}" ]; then
		CUR_PCT=$(parse_summary_field "${ROUND_DIR}" "side_winrate_pct" "${STOP_WINNER}")
	fi

	DELTA=$(awk -v c="${CUR_PCT}" -v p="${PREV_PCT}" 'BEGIN { printf "%.1f", c - p }')
	echo "round=${ROUND}  verdicts=${VERDICTS}/${BATCH_SIZE} fails=${FAILS}  ${STOP_WINNER}=${CUR_PCT}% (delta ${DELTA}%)"

	NOW2=$(date +%s)
	ELAPSED2=$((NOW2 - START_TS))
	echo "${ROUND},$(date -u +%Y-%m-%dT%H:%M:%SZ),${VERDICTS},${FAILS},${STOP_WINNER},${CUR_PCT},${PREV_PCT},${DELTA},${ELAPSED2}" >> "${PROGRESS_CSV}"

	# Big winrate shift → bell + milestone marker.
	if awk -v d="${DELTA}" 'BEGIN { exit !(d+0 > 15 || d+0 < -15) }' 2>/dev/null; then
		echo "  ! Large winrate swing (delta ${DELTA}%) — writing milestone marker."
		echo "round ${ROUND}: ${STOP_WINNER} winrate ${PREV_PCT}% -> ${CUR_PCT}% (delta ${DELTA}%)" \
			> "${LOOP_DIR}/milestone_winrate_swing_round${ROUND}.txt"
		printf "\a"
	fi

	# Stop condition check.
	if [ -n "${STOP_THRESHOLD}" ] && [ -n "${CUR_PCT}" ] && [ "${CUR_PCT}" != "0" ]; then
		# CUR_PCT is in 0..100 (from side_winrate_pct), threshold is 0..1.
		CUR_FRAC=$(awk -v p="${CUR_PCT}" 'BEGIN { printf "%.4f", p/100 }')
		if fge "${CUR_FRAC}" "${STOP_THRESHOLD}"; then
			STOP_REASON="goal_met"
			echo "  ! GOAL MET: ${STOP_WINNER} ${CUR_PCT}% >= ${STOP_THRESHOLD} target."
			printf "\a"
			break
		fi
	fi

	PREV_PCT="${CUR_PCT}"
done

NOW=$(date +%s)
TOTAL=$((NOW - START_TS))

echo
echo "============================================================"
echo "Loop done after ${ROUND} round(s) in ${TOTAL}s wall-clock."
echo "Reason: ${STOP_REASON:-unknown}"
echo "Output: ${LOOP_DIR}"
echo "============================================================"
echo "${STOP_REASON}" > "${LOOP_DIR}/${STOP_REASON:-finished}.txt"

# Final terminal bell.
printf "\a"

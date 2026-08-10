#!/bin/sh
# WW3MOD developer test harness — multi-test runner
#
# Usage:  ./tools/autotest/run-batch.sh [--hidden|--minimized] [--timeout N] [--seed N] [--speed N] <test1> <test2> ...
#         ./tools/autotest/run-batch.sh [--hidden|--minimized] [--timeout N] [--seed N] [--speed N] --all
#
# --speed N (optional, leading): forwarded to every run-test.sh. DEFAULTS TO 8,
# unlike run-test.sh which defaults to 1x. A batch is never watched by a human,
# so there is no reason to pace it at wall-clock: Test.SpeedMultiplier only
# divides world.Timestep and never enters a synced path, so the simulation stays
# BYTE-IDENTICAL and only finishes sooner (engine/OpenRA.Mods.Common/Traits/
# World/TestModeSpeedMultiplier.cs). The per-test --timeout is pure wall-clock
# and is NOT scaled, so a faster batch also gets more watchdog headroom, not
# less. Pass --speed 1 to restore the old wall-clock pacing (e.g. to watch a
# batch run), or set BATCH_SPEED in the environment to change the default.
# Range 1-16; the effective ceiling is whatever tick rate the machine sustains,
# and falling short only makes a run slower, never wrong.
#
# Runs each named test sequentially via run-test.sh, prints a per-test
# verdict line and a final summary. Exit code: 0 if all pass; otherwise
# the count of non-pass tests (capped at 99 so the shell doesn't truncate).
#
# --hidden / --minimized (optional, leading): forwarded to every run-test.sh as
# its window behavior. --hidden creates the window with SDL_WINDOW_HIDDEN (never
# mapped, never focus-steals) — the unattended profile, and the one to use on
# Windows where a minimized window can surface as a black frame. --minimized is
# the legacy SDL_MinimizeWindow (macOS dock) behavior. Omit to keep the prior
# behavior exactly: no window flag is forwarded and run-test.sh's default
# (visible `background`) applies.
#
# --timeout N (optional, leading): forwarded to every run-test.sh as its
# per-test wall-clock kill-timeout. Omit to use run-test.sh's own default
# (300s), which already prevents a rules-broken map from hanging the batch.
#
# --seed N (optional, leading): use N as a BASE seed and forward a DISTINCT
# derived seed to each test — the k-th test gets --seed (N + k), skipping the
# value 0 (which run-test.sh reserves as the unset sentinel). A single shared
# seed would be wrong: batches run different tests, but reusing one value
# hides per-test reproducibility and couples unrelated runs. Base + index
# gives each test its own byte-reproducible seed while a whole batch stays
# reproducible from one number, mirroring run-tournament.sh's per-index seed.
# N is an integer (may be negative); 0 is rejected (the unset sentinel). Each
# derived value is re-validated by run-test.sh. Omit to keep the prior
# behavior exactly: no --seed is forwarded and the engine picks (and records)
# a wall-clock seed per test.
#
# Per-test exit codes from run-test.sh: 0=pass, 1=fail, 2=skip, 3=error.
# Pass-through unchanged so a future CI step can read each verdict from logs.

set -u

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

# Optional leading flags, forwarded to each run-test.sh. Any may appear, in any
# order, before the first test name / --all. Stop at the first non-flag token so
# --all (handled below) and test folder names pass through untouched.
TIMEOUT_ARGS=""
SEED=""
WINDOW_ARGS=""
SPEED="${BATCH_SPEED:-8}"
while [ $# -gt 0 ]; do
	case "$1" in
		--timeout=*) TIMEOUT_ARGS="--timeout ${1#*=}"; shift ;;
		--timeout)   TIMEOUT_ARGS="--timeout ${2:-}"; shift 2 ;;
		--seed=*)    SEED="${1#*=}"; shift ;;
		--seed)      SEED="${2:-}"; shift 2 ;;
		--speed=*)   SPEED="${1#*=}"; shift ;;
		--speed)     SPEED="${2:-}"; shift 2 ;;
		--hidden)    WINDOW_ARGS="--hidden"; shift ;;
		--minimized) WINDOW_ARGS="--minimized"; shift ;;
		*)           break ;;
	esac
done

# Validate --speed (same 1-16 clamp run-test.sh enforces). Fail fast rather than
# after launching a game.
case "${SPEED}" in
	''|*[!0-9]*)
		echo "Error: --speed must be an integer 1-16 (got '${SPEED}')"
		exit 3 ;;
esac
if [ "${SPEED}" -lt 1 ] || [ "${SPEED}" -gt 16 ]; then
	echo "Error: --speed must be 1-16 (got '${SPEED}')"
	exit 3
fi

SPEED_ARGS=""
[ "${SPEED}" -gt 1 ] && SPEED_ARGS="--speed ${SPEED}"

# Validate the BASE seed up front (same rules as run-test.sh: integer, may be
# negative; 0 is the reserved unset sentinel). Fail fast rather than after
# launching a game. Per-test derived values are re-validated by run-test.sh.
if [ -n "${SEED}" ]; then
	_seed_digits="${SEED#-}"
	case "${_seed_digits}" in
		''|*[!0-9]*)
			echo "Error: --seed must be an integer, e.g. 1017 or -42 (got '${SEED}')"
			exit 3 ;;
	esac
	case "${_seed_digits}" in
		*[!0]*) : ;;
		*)
			echo "Error: --seed 0 is reserved as the unset sentinel; pick any non-zero int"
			exit 3 ;;
	esac
fi

if [ $# -eq 0 ]; then
	echo "Usage: $0 [--hidden|--minimized] [--timeout N] [--seed N] [--speed N] <test-folder> [<test-folder> ...]"
	echo "       $0 [--hidden|--minimized] [--timeout N] [--seed N] [--speed N] --all"
	echo "       --speed defaults to 8 (sim is byte-identical; pass --speed 1 to watch)"
	exit 3
fi

if [ "$1" = "--all" ]; then
	ALL=$(ls -d tools/autotest/scenarios/test-*/ 2>/dev/null | xargs -n1 basename)
	if [ -z "${ALL}" ]; then
		echo "No test-* folders found under tools/autotest/scenarios/"
		exit 3
	fi

	# Skip scenarios that can never produce a verdict. A scenario with no
	# assertion call writes no result.json, so run-test.sh burns its FULL
	# wall-clock timeout (300s by default, and the timeout is deliberately NOT
	# scaled by --speed) and then synthesizes a FAIL. Nine such scenarios exist
	# today -- test-artillery-turret is a "watch the turret rotate" demo filed
	# under test-*, and the eight test-balance-* scenarios report numbers for a
	# human to read rather than passing or failing. Left in, they cost ~45
	# minutes per --all run and put nine permanent false FAILs in every
	# regression tally, which is how a red batch stops meaning anything.
	#
	# Detected rather than hardcoded, so a future verdict-less scenario is
	# excluded automatically -- and ANNOUNCED rather than silently dropped, so a
	# real test that loses its assertion shows up here instead of vanishing.
	# Named tests are never filtered: asking for one by name always runs it.
	TESTS=""
	SKIPPED_NOVERDICT=""
	for _t in ${ALL}; do
		# Strip Lua line comments before looking for an assertion. Without this, a
		# comment reading "No Test.Pass -- the window stays open until you close it
		# manually" counts AS a Test.Pass and keeps a verdict-less demo in the run,
		# which then burns the full 300s timeout. That is not hypothetical: it is
		# exactly how test-burn-arena and test-burn-compare survived the first cut.
		if cat "tools/autotest/scenarios/${_t}"/*.lua 2>/dev/null | sed 's/--.*$//' \
			| grep -qE "Test\.(Pass|Fail|Skip)|Assert(Within|After)"; then
			TESTS="${TESTS} ${_t}"
		else
			SKIPPED_NOVERDICT="${SKIPPED_NOVERDICT} ${_t}"
		fi
	done

	if [ -n "${SKIPPED_NOVERDICT}" ]; then
		echo "==> Excluded from --all (no assertion, so no verdict is possible):"
		for _t in ${SKIPPED_NOVERDICT}; do echo "      ${_t}"; done
		echo "    Run any of these by name to execute it anyway."
		echo
	fi
else
	TESTS="$*"
fi

if [ -n "${SEED}" ]; then
	echo "==> Seed: base ${SEED}, per-test derived (base + test index, skipping 0) — reproducible"
fi

PASS=0; FAIL=0; SKIP=0; ERR=0
LINES=""

# Running offset into the base-seed sequence. Only advances when --seed is set,
# so with no seed the run-test.sh invocation below is byte-for-byte unchanged.
# Walking a monotonic offset (rather than offset = fixed test index) lets us
# simply skip the value 0 without ever colliding two tests onto the same seed.
_seed_offset=0

for t in ${TESTS}; do
	echo
	echo "============================================================"
	echo "  Running: ${t}"
	echo "============================================================"

	SEED_ARGS=""
	if [ -n "${SEED}" ]; then
		_derived=$((SEED + _seed_offset))
		if [ "${_derived}" -eq 0 ]; then
			_seed_offset=$((_seed_offset + 1))
			_derived=$((SEED + _seed_offset))
		fi
		SEED_ARGS="--seed ${_derived}"
		_seed_offset=$((_seed_offset + 1))
	fi

	./tools/autotest/run-test.sh ${WINDOW_ARGS} ${TIMEOUT_ARGS} ${SPEED_ARGS} ${SEED_ARGS} "${t}"
	rc=$?

	case ${rc} in
		0) verdict="PASS"; PASS=$((PASS + 1)) ;;
		1) verdict="FAIL"; FAIL=$((FAIL + 1)) ;;
		2) verdict="SKIP"; SKIP=$((SKIP + 1)) ;;
		*) verdict="ERR ($rc)"; ERR=$((ERR + 1)) ;;
	esac

	LINES="${LINES}${verdict}|${t}
"
done

TOTAL=$((PASS + FAIL + SKIP + ERR))
NON_PASS=$((FAIL + SKIP + ERR))

echo
echo "============================================================"
echo "  Summary (${TOTAL} tests)"
echo "============================================================"
printf '%s' "${LINES}" | awk -F'|' '{ printf "  %-8s %s\n", $1, $2 }'
echo "  ────────────────────────────────────────────"
printf "  Pass: %d  Fail: %d  Skip: %d  Error: %d\n" "${PASS}" "${FAIL}" "${SKIP}" "${ERR}"

if [ ${NON_PASS} -gt 99 ]; then
	exit 99
fi
exit ${NON_PASS}

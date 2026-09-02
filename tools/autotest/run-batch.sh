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
# the count of non-GREEN tests (capped at 99 so the shell doesn't truncate).
#
# "non-GREEN" rather than "non-pass" because a scenario may declare the outcome it is
# SUPPOSED to produce, in a file `tools/autotest/scenarios/test-<name>/expected-status`.
# A declared `fail` that fails is green and shows as `OK(fail)`; a declared `fail` that
# PASSES is red and shows as `STALE`, because the declaration has outlived its reason.
# Anything else -- a crash under a `fail` declaration, a malformed file -- is still red.
# Without this, a scenario that legitimately fails reds every batch forever, which is
# the "how a red batch stops meaning anything" failure the --all filter below guards
# against from the other side. Rationale and decision table: expected-status.sh.
#
# A declaration is graded against run-test.sh's OUTCOME NAME, never against its exit code.
# The two are not interchangeable: exit 1 covers both a real assertion FAIL and a watchdog
# TIMEOUT-FAIL, so grading on the code let a `fail` declaration report OK(fail) for a run
# that hung and never happened. The name arrives via AUTOTEST_OUTCOME_FILE, written by
# run-test.sh's EXIT trap. A hang, crash or lost outcome is red under ANY declaration and
# is listed under "NEVER REACHED A VERDICT" in the summary.
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
	# scaled by --speed) and then synthesizes a FAIL. TWENTY-ONE such scenarios
	# exist today -- nine test-balance-* reporting numbers for a human to read
	# rather than passing or failing, three test-savegame-resume-*, three
	# test-javelin-*, two test-burn-*, and test-artillery-turret (a "watch the
	# turret rotate" demo filed under test-*), test-atgm-humvee-motion,
	# test-desync-dialog, test-minelayer-mode-survives-modifiers. Left in, they
	# cost ~105 minutes per --all run (21 x the 300s default) and put twenty-one
	# permanent false FAILs in every regression tally, which is how a red batch
	# stops meaning anything.
	#
	# Do not trust these counts over the loop below: it is the enumeration, the
	# prose is a summary of it, and the prose drifted once already (it read
	# "nine ... and the eight test-balance-*" while the loop was excluding 21
	# with nine balance scenarios among them). Re-derive by running the same
	# predicate rather than by editing this paragraph from memory.
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

# A scenario may DECLARE the outcome it is supposed to produce, so a by-merit negative
# stops reddening every batch forever without being hidden from it. Declaring `fail` makes
# a FAIL green AND makes a PASS red -- the same asymmetry as mods/ww3mod/lint-baseline.txt
# and for the same reason: a floor that can only be lowered deliberately. Nothing changes
# for a scenario with no declaration, which is the overwhelming majority of them --
# `ls tools/autotest/scenarios/*/expected-status` enumerates the exceptions, and is the
# only count worth quoting, because a hardcoded one here goes stale the next time somebody
# declares. See expected-status.sh.
. "$(dirname "$0")/expected-status.sh"

PASS=0; FAIL=0; SKIP=0; ERR=0
BAD=0; STALE=""; MISCONFIGURED=""; NOTRUN=""
LINES=""

# Where run-test.sh hands its precise OUTCOME name back. One path, rewritten per test,
# and deleted before each run so a run that somehow writes nothing cannot be graded on
# its predecessor's outcome. Carries the pid so two concurrent batches cannot share it.
BATCH_OUTCOME_FILE="${TMPDIR:-/tmp}/ww3mod-batch-outcome.$$"
trap 'rm -f "${BATCH_OUTCOME_FILE}"' EXIT

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

	rm -f "${BATCH_OUTCOME_FILE}"
	AUTOTEST_OUTCOME_FILE="${BATCH_OUTCOME_FILE}" \
		./tools/autotest/run-test.sh ${WINDOW_ARGS} ${TIMEOUT_ARGS} ${SPEED_ARGS} ${SEED_ARGS} "${t}"
	rc=$?

	# The tally stays exit-code shaped: Pass/Fail/Skip/Error are what CI and every
	# existing reader of this summary expect, and run-test.sh's codes are unchanged.
	case ${rc} in
		0) verdict="PASS";      PASS=$((PASS + 1)) ;;
		1) verdict="FAIL";      FAIL=$((FAIL + 1)) ;;
		2) verdict="SKIP";      SKIP=$((SKIP + 1)) ;;
		*) verdict="ERR ($rc)"; ERR=$((ERR + 1)) ;;
	esac

	# GRADING, HOWEVER, USES THE PRECISE OUTCOME NAME, never the exit code. exit 1 is
	# shared by a real assertion FAIL and a watchdog TIMEOUT-FAIL; grading on ${rc}
	# is what let a `fail` declaration absorb a hang -- a run that never happened,
	# recorded as a pass. run-test.sh writes the name it actually decided on to
	# AUTOTEST_OUTCOME_FILE from its EXIT trap, so there is no exit path that leaves
	# this file unwritten. Read from the file rather than the stdout banner because
	# capturing stdout is what costs a caller its exit code (this harness has lost one
	# to a pipe twice) -- and the two must agree, so we check that they do.
	outcome=$(sed -n 's/^outcome=\([^ ]*\).*/\1/p' "${BATCH_OUTCOME_FILE}" 2>/dev/null | head -1)
	_rt_exit=$(sed -n 's/^.* exit=\([^ ]*\).*/\1/p' "${BATCH_OUTCOME_FILE}" 2>/dev/null | head -1)
	if [ -z "${outcome}" ]; then
		# NO FALLBACK TO ${rc} ON PURPOSE. Deriving the outcome from the exit code is
		# precisely the bug being fixed, so doing it "just as a fallback" reinstates it
		# in the one situation where the harness is already known to be misbehaving.
		outcome="NO-OUTCOME"
	elif [ "${_rt_exit}" != "${rc}" ]; then
		# The runner's own record of how it exited disagrees with the code we received:
		# something between the two is rewriting the verdict. Never gradeable.
		outcome="OUTCOME-MISMATCH"
	fi

	# Show the precise name whenever it is not the exit-code bucket, so TIMEOUT-FAIL
	# and CRASH stop reading as an indistinguishable "FAIL" / "ERR (3)" in the summary.
	case "${outcome}" in
		PASS|FAIL|SKIP) : ;;
		*) verdict="${outcome}" ;;
	esac

	_declared=$(expected_status_read "tools/autotest/scenarios/${t}")
	case $(expected_status_grade "${_declared}" "${outcome}") in
		GREEN)
			# A declared outcome that occurred is green, and says so in the tally rather
			# than reading as an ordinary pass -- "OK(fail)" is not the same result as
			# "PASS" and a summary that conflated them would hide the declaration.
			[ -n "${_declared}" ] && verdict="OK(${_declared})"
			;;
		STOPPED)
			verdict="STALE"
			BAD=$((BAD + 1))
			STALE="${STALE} ${t}"
			;;
		NOTRUN)
			BAD=$((BAD + 1))
			NOTRUN="${NOTRUN} ${t}(${outcome})"
			;;
		CONFIG)
			verdict="CONFIG"
			BAD=$((BAD + 1))
			# Newline-delimited: the message contains spaces, and a space-delimited
			# accumulator word-splits it into one bogus line per word.
			MISCONFIGURED="${MISCONFIGURED}${t}: ${_declared#ERROR:}
"
			;;
		*)
			BAD=$((BAD + 1))
			;;
	esac

	LINES="${LINES}${verdict}|${t}
"
done

TOTAL=$((PASS + FAIL + SKIP + ERR))

echo
echo "============================================================"
echo "  Summary (${TOTAL} tests)"
echo "============================================================"
printf '%s' "${LINES}" | awk -F'|' '{ printf "  %-10s %s\n", $1, $2 }'
echo "  ────────────────────────────────────────────"
printf "  Pass: %d  Fail: %d  Skip: %d  Error: %d\n" "${PASS}" "${FAIL}" "${SKIP}" "${ERR}"

# A stale declaration is reported louder than an ordinary failure, because it is the one
# result nobody is looking for: the scenario started doing better than its note says, so
# the note is now lying to every future reader of this batch.
if [ -n "${STALE}" ]; then
	echo
	echo "  !! DECLARATION NOW STALE — these scenarios no longer produce the outcome"
	echo "     their expected-status file declares, so the file must be deleted:"
	for _s in ${STALE}; do
		echo "       tools/autotest/scenarios/${_s}/expected-status"
	done
	echo "     Delete it in the same commit as whatever fixed the scenario."
fi

# The run did not happen. Reported separately from an ordinary failure because it is a
# different KIND of result: a FAIL is the scenario answering no, and these are the harness
# giving up -- nothing was measured, so nothing was learned, and no declaration can make
# one of them green. This block is also the thing that makes an `expected-status` file
# safe to write: without it, "declared fail" quietly covered "never loaded its rules".
if [ -n "${NOTRUN}" ]; then
	echo
	echo "  !! NEVER REACHED A VERDICT — these scenarios hung, crashed or were killed,"
	echo "     so the run did not happen and no expected-status declaration grades them"
	echo "     green. Read the run banner and the debug.log before re-running:"
	for _n in ${NOTRUN}; do
		echo "       ${_n}"
	done
fi

if [ -n "${MISCONFIGURED}" ]; then
	echo
	echo "  !! MALFORMED expected-status declaration:"
	printf '%s' "${MISCONFIGURED}" | while IFS= read -r _m; do
		[ -n "${_m}" ] && echo "       ${_m}"
	done
fi

if [ ${BAD} -gt 99 ]; then
	exit 99
fi
exit ${BAD}

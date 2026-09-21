#!/bin/sh
# WW3MOD world-construction smoke gate.
#
# Usage:  ./tools/autotest/run-smoke.sh [--quick] [--timeout N] [--ticks N] [--map NAME]...
#
#   --quick        Only the canary scenario (test-world-smoke). One launch, ~30s.
#   --timeout N    Per-map watchdog, seconds. Default 150.
#   --ticks N      Sim ticks a shipped map must survive before passing. Default 30.
#   --map NAME     Run ONLY these maps (repeatable), and do NOT run the canary -- every run
#                  of this script starts a game, so "only what I named" means exactly that.
#                  Pass --map test-world-smoke to include the canary.
#                  Default (no --map): the canary, then every directory under
#                  mods/ww3mod/maps/.
#
# WHAT THIS EXISTS TO CATCH
# -------------------------
# Nothing else we run constructs a World. On 2026-09-10 DefconWall threw a
# NullReferenceException in INotifyCreated.Created, every match failed to start, and six
# gates were green through it: build, check, NUnit, YAML lint, lua-gate, nav-guard. All six
# read files. This one starts the game.
#
# A PASS here means: the world actor's traits were all constructed, every
# INotifyCreated.Created ran, IWorldLoaded ran, and the sim ticked N times without throwing.
# It means nothing whatsoever about gameplay, balance, rendering or AI. Do not grow it.
#
# THE THREE OUTCOMES, AND WHY THE THIRD IS NOT THE SECOND
# ------------------------------------------------------
#   exit 0  PASS           every map reached a verdict of `pass`.
#   exit 2  SMOKE FAILURE  at least one map started and did not reach a verdict. THIS IS THE
#                          BUG CLASS. A crash, a hang, or a fail verdict all land here.
#   exit 3  LAUNCH FAILURE nothing was proven either way -- the game never got to run. A
#                          missing engine/bin/OpenRA.dll, a missing run-test.sh (exit 127),
#                          an unreadable scenario, or a run that "failed" faster than a game
#                          can physically load.
#
# The third is the one that gets misread, and misreading it is worse than either other
# outcome: it converts "we learned nothing" into "the world is broken" (false alarm) or, if
# somebody greps for the wrong thing, into "clean" (false green). CLAUDE.md records the same
# trap twice already -- a bare ./run-test.sh exits 127 because the launchers live in
# tools/autotest/, and a zero-byte log plus a fast non-zero exit is a launch failure rather
# than a slow tool. So this script discriminates three ways, before and after each run:
#
#   1. PRE-FLIGHT. engine/bin/OpenRA.dll and engine/VERSION are checked here, mirroring
#      launch-game.sh's own guard, so an unbuilt tree refuses to start instead of producing
#      eleven identical NO-RESULTs that look exactly like eleven broken maps.
#   2. THE OUTCOME FILE. run-test.sh writes outcome= to AUTOTEST_OUTCOME_FILE from its EXIT
#      trap, so every exit path writes it -- crash, Ctrl-C, internal abort. A MISSING or
#      EMPTY outcome file therefore means run-test.sh never reached its own trap, which is
#      a launch failure and never a test result.
#   3. ELAPSED TIME. A real world-construction failure still costs a process start, a mod
#      load and a map load. Under MIN_PLAUSIBLE_SECONDS the game cannot have got far enough
#      to fail the way this gate is asking about, so a fast non-PASS is reported as a launch
#      failure with its duration, not banked as evidence about the World.
#
# Per-map output lands in the usual per-run directories under ~/.ww3mod-tests/screenshots/;
# each line below names the one for that map.

set -e

QUICK=0
TIMEOUT_SECS=150
SMOKE_TICKS=30
ONLY_MAPS=""
CANARY="test-world-smoke"

# Below this, a non-PASS is a launch failure rather than a verdict. Process start, mod load
# and map load do not fit in this budget on any machine this runs on; the observed floor for
# a real scenario run is well over a minute.
MIN_PLAUSIBLE_SECONDS=8

while [ $# -gt 0 ]; do
	case "$1" in
		--quick)        QUICK=1; shift ;;
		--timeout=*)    TIMEOUT_SECS="${1#*=}"; shift ;;
		--timeout)      TIMEOUT_SECS="$2"; shift 2 ;;
		--ticks=*)      SMOKE_TICKS="${1#*=}"; shift ;;
		--ticks)        SMOKE_TICKS="$2"; shift 2 ;;
		--map=*)        ONLY_MAPS="${ONLY_MAPS} ${1#*=}"; shift ;;
		--map)          ONLY_MAPS="${ONLY_MAPS} $2"; shift 2 ;;
		--help|-h)      sed -n '2,60p' "$0" | sed 's/^# \?//'; exit 0 ;;
		*)              echo "Unknown flag: $1"; exit 3 ;;
	esac
done

# ── Pre-flight (discriminator 1) ────────────────────────────────────────────
# Every failure in this block is exit 3. None of them is evidence about a World.
fail_launch() {
	echo
	echo "============================================================"
	echo "==> SMOKE: LAUNCH FAILURE — nothing was run, nothing was proven."
	echo "==>   $1"
	echo "============================================================"
	echo "SMOKE_VERDICT outcome=LAUNCH-FAILURE exit=3 passed=0 failed=0"
	exit 3
}

[ -f "tools/autotest/run-smoke.sh" ] || fail_launch \
	"run this from the repository root: ./tools/autotest/run-smoke.sh"
[ -f "tools/autotest/run-test.sh" ] || fail_launch \
	"tools/autotest/run-test.sh not found (a bare ./run-test.sh from the root exits 127)."
[ -d "tools/autotest/scenarios/${CANARY}" ] || fail_launch \
	"canary scenario tools/autotest/scenarios/${CANARY} is missing."

# Mirrors launch-game.sh's own guard, and reads ENGINE_DIRECTORY from the same place it does
# rather than hardcoding "engine" -- a user.config may move it. Without this check the unbuilt
# case surfaces as one NO-RESULT per map, which is indistinguishable by outcome alone from
# every map being broken.
# `if`, not `[ -f x ] && . x`: with `set -e` on, a bare && list whose test fails makes the
# whole list non-zero and kills the script. user.config normally does NOT exist, so the
# terse form would abort here on most machines, before running anything.
if [ -f "mod.config" ]; then
	. ./mod.config
fi
if [ -f "user.config" ]; then
	. ./user.config
fi
ENGINE_DIR="${ENGINE_DIRECTORY:-./engine}"

[ -f "${ENGINE_DIR}/bin/OpenRA.dll" ] || fail_launch \
	"${ENGINE_DIR}/bin/OpenRA.dll not found — build first (.\\make.ps1 all, or make all)."
if [ -n "${ENGINE_VERSION:-}" ] && [ -f "${ENGINE_DIR}/VERSION" ]; then
	_have=$(cat "${ENGINE_DIR}/VERSION")
	if [ "${_have}" != "${ENGINE_VERSION}" ]; then
		fail_launch "${ENGINE_DIR}/VERSION is ${_have}, mod.config wants ${ENGINE_VERSION} — rebuild."
	fi
fi

# ── Map list ────────────────────────────────────────────────────────────────
# The canary runs first on a DEFAULT run. It is a tiny scenario with its own Lua verdict, so
# it separates "the harness and the verdict path work" from "a particular shipped map is
# broken" -- if the canary fails too, suspect the world rules, not the map.
#
# --map SUPPRESSES it, and that is a correctness property rather than a convenience: every
# run of this script starts a game, so "run only what I named" has to mean exactly that. The
# earlier version ran the canary unconditionally, and invoking it with a deliberately invalid
# --map to exercise the validation path launched a full game before ever reaching the name
# check. Name the canary explicitly to include it.
RUN_CANARY=1
if [ -n "${ONLY_MAPS}" ]; then
	MAPS="${ONLY_MAPS}"
	RUN_CANARY=0
elif [ "${QUICK}" = "1" ]; then
	MAPS=""
else
	MAPS=""
	for d in mods/ww3mod/maps/*/; do
		[ -d "${d}" ] || continue
		MAPS="${MAPS} $(basename "${d}")"
	done
fi

echo "============================================================"
echo "==> WW3MOD world-construction smoke gate"
echo "==>   canary:  ${CANARY} (its own Lua verdict)"
echo "==>   maps:    $(echo ${MAPS} | wc -w | tr -d ' ') shipped map(s) via Test.SmokeTicks=${SMOKE_TICKS}"
echo "==>   timeout: ${TIMEOUT_SECS}s per map"
echo "============================================================"

PASSED=0
FAILED=0
FAILED_NAMES=""
OUTCOME_FILE="${TMPDIR:-/tmp}/ww3mod-smoke-outcome.$$"
trap 'rm -f "${OUTCOME_FILE}"' EXIT

run_one() {
	_label="$1"
	_map_arg="$2"
	_extra="$3"

	rm -f "${OUTCOME_FILE}"
	printf '\n>>> smoke: %s\n' "${_label}"
	_start=$(date +%s)

	# `set -e` is on and run-test.sh exits non-zero on any non-PASS, which is a normal
	# result here rather than an abort -- so the call is guarded and the status captured.
	set +e
	AUTOTEST_OUTCOME_FILE="${OUTCOME_FILE}" \
	AUTOTEST_EXTRA_ARGS="${_extra}" \
		./tools/autotest/run-test.sh --hidden --timeout "${TIMEOUT_SECS}" ${_map_arg} "${CANARY}"
	_rc=$?
	set -e

	_elapsed=$(( $(date +%s) - _start ))

	# Discriminator 2: the outcome file is written from run-test.sh's EXIT trap, so it exists
	# on every exit path that trap ever reached. Missing or empty means it did not.
	if [ ! -s "${OUTCOME_FILE}" ]; then
		fail_launch "${_label}: run-test.sh wrote no outcome (rc=${_rc}, ${_elapsed}s). It never reached its own EXIT trap — this is a launch failure, not a result."
	fi

	_outcome=$(sed -n 's/^outcome=\([A-Z-]*\).*/\1/p' "${OUTCOME_FILE}")
	[ -n "${_outcome}" ] || fail_launch \
		"${_label}: unparseable outcome file: $(cat "${OUTCOME_FILE}")"

	case "${_outcome}" in
		PASS)
			PASSED=$((PASSED + 1))
			printf '    PASS  %-28s %ss\n' "${_label}" "${_elapsed}"
			;;
		HARNESS-ERROR|INTERRUPTED)
			# run-test.sh's own name for "I could not run this", which is exit 3 for a bad
			# flag, a missing scenario or lock contention. Never a statement about a World.
			fail_launch "${_label}: run-test.sh reported ${_outcome} (rc=${_rc}, ${_elapsed}s)."
			;;
		*)
			# Discriminator 3: too fast to have been a world-construction failure.
			if [ "${_elapsed}" -lt "${MIN_PLAUSIBLE_SECONDS}" ]; then
				fail_launch "${_label}: ${_outcome} after only ${_elapsed}s (rc=${_rc}). A game cannot start, load a mod and load a map that fast, so this is a launch failure and NOT evidence that the World is broken."
			fi
			FAILED=$((FAILED + 1))
			FAILED_NAMES="${FAILED_NAMES} ${_label}(${_outcome})"
			printf '    FAIL  %-28s %ss  outcome=%s\n' "${_label}" "${_elapsed}" "${_outcome}"
			;;
	esac
}

# The canary loads the scenario's own map and reaches its verdict through Lua, so it needs
# no Test.SmokeTicks. Keeping it on the Lua path is deliberate: it means a break in the
# engine-side gate and a break in the world rules cannot mask each other.
if [ "${RUN_CANARY}" = "1" ]; then
	run_one "${CANARY}" "" ""
fi

for m in ${MAPS}; do
	if [ "${m}" = "${CANARY}" ]; then
		run_one "${CANARY}" "" ""
	else
		run_one "${m}" "--map ${m}" "Test.SmokeTicks=${SMOKE_TICKS}"
	fi
done

echo
echo "============================================================"
if [ "${FAILED}" -eq 0 ]; then
	echo "==> SMOKE: PASS — ${PASSED} map(s) constructed a World and ticked."
	echo "============================================================"
	echo "SMOKE_VERDICT outcome=PASS exit=0 passed=${PASSED} failed=0"
	exit 0
fi

echo "==> SMOKE: FAILURE — ${FAILED} of $((PASSED + FAILED)) map(s) did not reach a verdict."
echo "==>   ${FAILED_NAMES}"
echo "==>   These started and did not finish. Read the per-run debug.log and any"
echo "==>   exception-*.log named in the per-map banner above; a throw in a world trait's"
echo "==>   constructor or INotifyCreated.Created is the case this gate was built for."
echo "==>   If EVERY map failed but the canary passed, suspect the shipped-map path"
echo "==>   (Test.SmokeTicks / SmokeTestExit) before suspecting ten separate maps."
echo "============================================================"
echo "SMOKE_VERDICT outcome=FAIL exit=2 passed=${PASSED} failed=${FAILED}"
exit 2

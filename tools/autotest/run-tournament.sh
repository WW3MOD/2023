#!/bin/sh
# WW3MOD AI tournament harness — batch match runner.
#
# Wraps run-test.sh, iterating N matches against a tournament scenario, collecting
# per-match verdict JSON into a timestamped result dir, and writing a summary CSV.
#
# Usage:
#   ./tools/autotest/run-tournament.sh <scenario> [options]
#
# Options:
#   --seeds N          Run N matches (each with a fresh in-engine seed via
#                      Server.cs's DateTime.Now). Default: 5.
#   --config <path>    Override the tournament.yaml inside the scenario.
#                      Default: <scenario>/tournament.yaml.
#   --result-dir <dir> Where to dump per-match results. Default:
#                      tools/autotest/tournament-results/<YYMMDD_HHMM>_<scenario>/
#   --max-wall-secs N  Per-match wall-clock cap (kills the game if it exceeds N
#                      seconds). Defaults to 4× the config's TimeLimitSeconds.
#   -v|--visible       Pass --visible to run-test.sh (default: --background).
#
# Per-match output:
#   <result-dir>/match_<seed-index>.json  Engine-written verdict + meta
#   <result-dir>/match_<seed-index>.log   stdout/stderr capture
#
# Batch output:
#   <result-dir>/summary.csv              One row per match
#   <result-dir>/summary.json             Aggregate stats
#   <result-dir>/batch.meta.json          Git SHA, scenario, config used
#
# Exit code: 0 if any matches ran; 3 on usage error.
#
# Phase 1 limitations (tracked in WORKSPACE/plans/260511_ai_tournament_harness.md):
#   - In-engine seed is DateTime.Now-based (not deterministic across seed indices).
#     For statistical validity over N matches it's fine; for reproduction of a
#     specific match it isn't. Phase 2 candidate: Tournament.RandomSeed launch arg.
#   - Sequential only — parallel runner is Phase 3.

set -e

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

SCENARIO=""
SEEDS=5
CONFIG=""
RESULT_DIR=""
MAX_WALL_SECS=""
RUN_TEST_FLAGS="--background --mute"

while [ $# -gt 0 ]; do
	case "$1" in
		--seeds)         SEEDS="$2"; shift 2 ;;
		--seeds=*)       SEEDS="${1#*=}"; shift ;;
		--config)        CONFIG="$2"; shift 2 ;;
		--config=*)      CONFIG="${1#*=}"; shift ;;
		--result-dir)    RESULT_DIR="$2"; shift 2 ;;
		--result-dir=*)  RESULT_DIR="${1#*=}"; shift ;;
		--max-wall-secs) MAX_WALL_SECS="$2"; shift 2 ;;
		--max-wall-secs=*) MAX_WALL_SECS="${1#*=}"; shift ;;
		-v|--visible)    RUN_TEST_FLAGS="--visible --mute"; shift ;;
		--help|-h)
			sed -n '2,33p' "$0" | sed 's/^# \?//'
			exit 0 ;;
		--*)
			echo "Unknown flag: $1"
			exit 3 ;;
		*)
			if [ -z "${SCENARIO}" ]; then
				SCENARIO="$1"
				shift
			else
				echo "Unexpected positional arg: $1"
				exit 3
			fi
			;;
	esac
done

if [ -z "${SCENARIO}" ]; then
	echo "Usage: $0 <scenario> [--seeds N] [--config path] [--result-dir dir]"
	echo "  e.g.  $0 tournament-arena-skirmish-2p --seeds 20"
	exit 3
fi

SCENARIO_DIR="tools/autotest/scenarios/${SCENARIO}"
if [ ! -d "${SCENARIO_DIR}" ]; then
	echo "Error: scenario not found at ${SCENARIO_DIR}"
	exit 3
fi

if [ -z "${CONFIG}" ]; then
	CONFIG="${SCENARIO_DIR}/tournament.yaml"
fi

if [ ! -f "${CONFIG}" ]; then
	echo "Error: tournament config not found at ${CONFIG}"
	exit 3
fi

# Compute default max-wall-secs from TimeLimitSeconds × 4 (windowed at 1× takes
# ~real-time; at 6× ~1/6; we leave headroom for boot/teardown).
if [ -z "${MAX_WALL_SECS}" ]; then
	TIME_LIMIT_SECS=$(awk '/^TimeLimitSeconds:/ { gsub(",",""); print $2; exit }' "${CONFIG}")
	if [ -z "${TIME_LIMIT_SECS}" ]; then
		MAX_WALL_SECS=600
	else
		MAX_WALL_SECS=$((TIME_LIMIT_SECS * 4))
	fi
fi

# Result dir — auto-generate if not provided.
if [ -z "${RESULT_DIR}" ]; then
	TS=$(date +"%y%m%d_%H%M")
	RESULT_DIR="tools/autotest/tournament-results/${TS}_${SCENARIO}"
fi
mkdir -p "${RESULT_DIR}"

GIT_SHA=$(git rev-parse HEAD 2>/dev/null || echo "unknown")
GIT_DIRTY=$(git diff --quiet HEAD 2>/dev/null && echo "false" || echo "true")

# Batch meta — what produced this run.
cat > "${RESULT_DIR}/batch.meta.json" <<EOF
{
  "scenario": "${SCENARIO}",
  "config": "${CONFIG}",
  "seeds_requested": ${SEEDS},
  "git_sha": "${GIT_SHA}",
  "git_dirty": ${GIT_DIRTY},
  "started_at": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "max_wall_secs": ${MAX_WALL_SECS}
}
EOF

echo "==> Tournament:  ${SCENARIO}"
echo "==> Config:      ${CONFIG}"
echo "==> Seeds:       ${SEEDS}"
echo "==> Result dir:  ${RESULT_DIR}"
echo "==> Git SHA:     ${GIT_SHA} (dirty=${GIT_DIRTY})"
echo "==> Max-wall:    ${MAX_WALL_SECS}s/match"
echo

# Hand the engine the absolute config path so it can find it regardless of cwd.
CONFIG_ABS=$(cd "$(dirname "${CONFIG}")" && pwd)/$(basename "${CONFIG}")

OK=0
FAIL=0

for i in $(seq 1 ${SEEDS}); do
	echo "------- match ${i}/${SEEDS} -------"

	# Engine runs with cwd=engine/, so relative paths land in the wrong place.
	# Resolve to absolute before handing off.
	MATCH_RESULT_FILE="${REPO_ROOT}/${RESULT_DIR}/match_${i}.json"
	MATCH_LOG="${REPO_ROOT}/${RESULT_DIR}/match_${i}.log"

	# Inject the tournament config into the regular launch via Test.TournamentConfig.
	# We re-use run-test.sh's launcher but inject extra args via env (run-test.sh
	# doesn't accept arbitrary engine args today — for Phase 1 we call launch-game.sh
	# directly with the same arg shape, mirroring run-test.sh).
	#
	# Mirror run-test.sh's settings.yaml backup so audio mute doesn't leak.
	SETTINGS_FILE=""
	case "$(uname)" in
		Darwin) SETTINGS_FILE="${HOME}/Library/Application Support/OpenRA/settings.yaml" ;;
		Linux)  SETTINGS_FILE="${HOME}/.config/openra/settings.yaml" ;;
	esac
	SETTINGS_BACKUP=""
	if [ -n "${SETTINGS_FILE}" ] && [ -f "${SETTINGS_FILE}" ]; then
		SETTINGS_BACKUP="${RESULT_DIR}/.settings.yaml.bak"
		cp "${SETTINGS_FILE}" "${SETTINGS_BACKUP}"
	fi

	# Background launch — terminal keeps focus.
	(
		./launch-game.sh \
			"Launch.Map=${SCENARIO}" \
			"Test.Mode=true" \
			"Test.Name=${SCENARIO}-match${i}" \
			"Test.ResultPath=${MATCH_RESULT_FILE}" \
			"Test.TournamentConfig=${CONFIG_ABS}" \
			"Graphics.Mode=Windowed" \
			"Sound.Mute=true" \
			> "${MATCH_LOG}" 2>&1 || true
	) &
	GAME_PID=$!

	# Wall-clock watchdog. Polls the result file and the game PID.
	WALL_START=$(date +%s)
	while true; do
		if [ -f "${MATCH_RESULT_FILE}" ]; then
			# Engine wrote verdict; give it a moment to clean up.
			sleep 1
			break
		fi

		if ! kill -0 "${GAME_PID}" 2>/dev/null; then
			# Game exited without writing verdict.
			break
		fi

		NOW=$(date +%s)
		ELAPSED=$((NOW - WALL_START))
		if [ ${ELAPSED} -ge ${MAX_WALL_SECS} ]; then
			echo "  ! Wall-clock limit (${MAX_WALL_SECS}s) exceeded, killing match."
			# GAME_PID is the subshell — the actual dotnet process is a child.
			# Kill by command pattern to be sure. PITFALL: SIGTERM is sometimes
			# ignored by dotnet (process hangs after Game.Exit); SIGKILL is the
			# only reliable terminator on macOS.
			kill -KILL "${GAME_PID}" 2>/dev/null || true
			pkill -KILL -f "Test\\.ResultPath=${MATCH_RESULT_FILE}" 2>/dev/null || true
			break
		fi

		sleep 2
	done

	wait "${GAME_PID}" 2>/dev/null || true
	# Belt-and-braces: ensure no dotnet process is still holding our result path.
	pkill -KILL -f "Test\\.ResultPath=${MATCH_RESULT_FILE}" 2>/dev/null || true

	if [ -n "${SETTINGS_BACKUP}" ] && [ -f "${SETTINGS_BACKUP}" ]; then
		mv "${SETTINGS_BACKUP}" "${SETTINGS_FILE}"
	fi

	if [ -f "${MATCH_RESULT_FILE}" ]; then
		echo "  ok — verdict written"
		OK=$((OK + 1))
	else
		echo "  fail — no verdict file. See ${MATCH_LOG}"
		FAIL=$((FAIL + 1))
	fi
done

echo
echo "==> Batch complete: ${OK} verdict / ${FAIL} no-verdict (of ${SEEDS})"
echo "==> Results:  ${RESULT_DIR}"
echo

# Aggregate if we have at least one match result.
if [ ${OK} -gt 0 ]; then
	"${REPO_ROOT}/tools/autotest/aggregate-tournament.sh" "${RESULT_DIR}"
fi

exit 0

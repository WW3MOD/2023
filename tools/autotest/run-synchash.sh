#!/bin/sh
# WW3MOD cross-runtime determinism probe.
#
# Runs one bot-vs-bot tournament match on an explicitly chosen .NET runtime and
# writes a per-net-frame sync-hash trace (Test.SyncHashLog). Two traces taken on
# two runtimes with the same seed answer: does the simulation hash identically
# across .NET majors?
#
# Why this exists rather than reusing the sync-report machinery: sync reports are
# hard-disabled under a ReplayConnection (OrderManager.cs, `Connection is not
# ReplayConnection`), so "replay a match with Test.ForceSyncReports" cannot work.
# The ring also holds only 32 frames and dumps only on a game-save ack.
#
# Runtime selection is NOT inferred from the command line. `dotnet exec
# --fx-version` pins it exactly, and the engine stamps Platform.RuntimeVersion
# into the trace header, so the artifact proves its own provenance.
#
# Usage: run-synchash.sh <fx-version> <seed> <out-tsv> [scenario] [config] [timeout-secs]

set -e

FX_VERSION="$1"
SEED="$2"
OUT_TSV="$3"
SCENARIO="${4:-tournament-arena-skirmish-2p}"
CONFIG_NAME="${5:-tournament.yaml}"
TIMEOUT_SECS="${6:-900}"

[ -n "${FX_VERSION}" ] && [ -n "${SEED}" ] && [ -n "${OUT_TSV}" ] || {
	echo >&2 "usage: $0 <fx-version> <seed> <out-tsv> [scenario] [config] [timeout-secs]"
	exit 2
}

REPO_ROOT=$(cd "$(dirname "$0")/../.." && pwd)
cd "${REPO_ROOT}"

# A tournament config is optional: bot-vs-bot scenarios need one to get a match
# clock and a win rule, scripted Lua scenarios end themselves with Test.Pass.
# "none" selects the latter. TestMode.SpeedMultiplier is applied by the tournament
# watcher only, so the speed args travel with the config and not without it.
TOURNAMENT_ARGS=""
if [ "${CONFIG_NAME}" != "none" ]; then
	case "${CONFIG_NAME}" in
		/*) CONFIG_PATH="${CONFIG_NAME}" ;;
		*)  CONFIG_PATH="${REPO_ROOT}/tools/autotest/scenarios/${SCENARIO}/${CONFIG_NAME}" ;;
	esac
	[ -f "${CONFIG_PATH}" ] || { echo >&2 "no such config: ${CONFIG_PATH}"; exit 2; }
	TOURNAMENT_ARGS="Test.TournamentConfig=${CONFIG_PATH} Test.GameSpeed=fastest Test.SpeedMultiplier=8"
fi

REAL_DOTNET=$(command -v dotnet) || { echo >&2 "dotnet not on PATH"; exit 2; }
"${REAL_DOTNET}" --list-runtimes | grep -q "Microsoft.NETCore.App ${FX_VERSION} " || {
	echo >&2 "runtime ${FX_VERSION} is not installed"; exit 2; }

OUT_DIR=$(dirname "${OUT_TSV}")
mkdir -p "${OUT_DIR}"
RUN_LOG="${OUT_TSV%.tsv}.log"
RESULT_JSON="${OUT_TSV%.tsv}.result.json"
rm -f "${OUT_TSV}" "${RUN_LOG}" "${RESULT_JSON}"

# launch-game.sh hardcodes `dotnet bin/OpenRA.dll`, which honours the
# runtimeconfig's `rollForward: Major` and therefore silently picks whatever the
# muxer likes — on a machine with 6/8/10 installed that is 6.0.x, not the version
# under test. A PATH shim rewrites that single call into an exact `exec
# --fx-version`, without editing the shipped launcher.
SHIM_DIR=$(mktemp -d)
cat > "${SHIM_DIR}/dotnet" <<SHIM
#!/bin/sh
exec "${REAL_DOTNET}" exec --fx-version "${FX_VERSION}" "\$@"
SHIM
chmod +x "${SHIM_DIR}/dotnet"
trap 'rm -rf "${SHIM_DIR}"' EXIT

echo "==> scenario=${SCENARIO} config=${CONFIG_NAME} fx=${FX_VERSION} seed=${SEED}"

(
	PATH="${SHIM_DIR}:${PATH}" OPENRA_WINDOW_HIDDEN=1 ./launch-game.sh \
		"Launch.Map=${SCENARIO}" \
		"Test.Mode=true" \
		"Test.Name=synchash-${SCENARIO}-seed${SEED}" \
		"Test.ResultPath=${RESULT_JSON}" \
		"Test.SyncHashLog=${OUT_TSV}" \
		${TOURNAMENT_ARGS} \
		"Test.RandomSeed=${SEED}" \
		"Graphics.Mode=Windowed" \
		"Graphics.CapFramerate=false" \
		"Sound.Mute=true" \
		> "${RUN_LOG}" 2>&1 || true
) &
GAME_PID=$!

START=$(date +%s)
while :; do
	if ! kill -0 "${GAME_PID}" 2>/dev/null; then
		echo "==> game exited on its own"
		break
	fi
	if [ -f "${RESULT_JSON}" ]; then
		sleep 2
		break
	fi
	if [ $(( $(date +%s) - START )) -ge "${TIMEOUT_SECS}" ]; then
		echo "==> wall-clock limit ${TIMEOUT_SECS}s exceeded, killing"
		# The game is a GRANDCHILD (subshell -> launch-game.sh -> dotnet), so
		# `pkill -P ${GAME_PID}` reaps only the launcher and orphans the game,
		# which then collides with the next run. Match on this run's own output
		# path instead — unique per invocation, so it can never reach another
		# agent's game or an unrelated dotnet process. Deliberately NOT Test.Name:
		# that arg is held identical across the runs being compared, so nothing
		# that varies between them can reach the simulation at all.
		pkill -f "Test.SyncHashLog=${OUT_TSV}" 2>/dev/null || true
		pkill -P "${GAME_PID}" 2>/dev/null || true
		kill "${GAME_PID}" 2>/dev/null || true
		sleep 3
		break
	fi
	sleep 2
done
wait "${GAME_PID}" 2>/dev/null || true

ELAPSED=$(( $(date +%s) - START ))

# Provenance first: the header the engine wrote is the only acceptable answer to
# "which runtime ran this", because it came from the process itself.
echo "==> elapsed ${ELAPSED}s"
if [ -f "${OUT_TSV}" ]; then
	sed -n '1,5p' "${OUT_TSV}"
	echo "==> frames: $(grep -vc '^#' "${OUT_TSV}")"
else
	echo "==> NO TRACE WRITTEN"
	exit 3
fi

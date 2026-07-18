#!/bin/sh
# WW3MOD — capture a screenshot of the skirmish lobby, no human in the loop.
#
# Usage:
#   ./tools/autotest/screenshot-lobby.sh <label> [--map=<map-id>] [--tab=<tab>] \
#       [--no-quit] [--timeout=<sec>]
#
# Pipeline:
#   1. Launches the game with Test.Mode=true Test.OpenSkirmishLobby=true so
#      MainMenuLogic clicks through to the skirmish lobby on its own.
#   2. Polls a Test.LobbyReadyFile marker that LobbyLogic touches once the
#      lobby's MapIsPlayable. Beats blind-sleeping — survives slow machines
#      without overshooting on fast ones.
#   3. Sends "screenshot <label>" via the same cmd-file watcher screenshot.sh
#      uses. Engine captures synchronously, appends to manifest.json.
#   4. Waits for the new manifest entry, prints the PNG path on stdout.
#   5. Sends "quit" so the game tears itself down cleanly (no pkill).
#
# Options:
#   --map=<id>       Override Test.LaunchLobbyMap (default: river-zeta-ww3).
#                    Matched against MapPreview Title, package folder, or Uid.
#   --tab=<name>     Test.OpenLobbyTab — "match" (default), "advanced", "music".
#   --no-quit        Leave the game running after the screenshot. Useful when
#                    iterating on lobby YAML in one terminal while shooting
#                    follow-up screenshots via tools/autotest/screenshot.sh.
#   --timeout=<sec>  Per-phase timeout (default: 30). Bumped from the user's
#                    ~15s target to give cold-cache launches headroom.

set -e

LABEL=""
LOBBY_MAP="river-zeta-ww3"
LOBBY_TAB=""
DO_QUIT=1
PHASE_TIMEOUT=30

while [ $# -gt 0 ]; do
	case "$1" in
		--map=*)     LOBBY_MAP="${1#*=}"; shift ;;
		--tab=*)     LOBBY_TAB="${1#*=}"; shift ;;
		--no-quit)   DO_QUIT=0; shift ;;
		--timeout=*) PHASE_TIMEOUT="${1#*=}"; shift ;;
		--help|-h)
			sed -n '2,30p' "$0" | sed 's/^# \?//'
			exit 0 ;;
		--*)
			echo "Unknown flag: $1" >&2
			exit 1 ;;
		*)
			if [ -z "${LABEL}" ]; then
				LABEL="$1"; shift
			else
				echo "Extra positional arg: $1" >&2
				exit 1
			fi ;;
	esac
done

if [ -z "${LABEL}" ]; then
	echo "Usage: $0 <label> [--map=<id>] [--tab=<name>] [--no-quit] [--timeout=<sec>]" >&2
	exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

# Per-run dir; matches the manual_<id> naming so screenshot.sh's lookup picks
# this one up if the user wants to fire follow-up captures via --no-quit.
RUN_ID="manual_lobby_$(date +%y%m%d_%H%M%S)"
RESULT_DIR="${HOME}/.ww3mod-tests"
SCREENSHOT_DIR="${RESULT_DIR}/screenshots/${RUN_ID}"
CMD_FILE="${SCREENSHOT_DIR}/cmd.txt"
MANIFEST_FILE="${SCREENSHOT_DIR}/manifest.json"
READY_FILE="${SCREENSHOT_DIR}/lobby-ready"

mkdir -p "${SCREENSHOT_DIR}"
rm -f "${CMD_FILE}" "${READY_FILE}"

echo "==> Lobby screenshot: ${LABEL}" >&2
echo "==> Map: ${LOBBY_MAP}" >&2
echo "==> Run dir: ${SCREENSHOT_DIR}" >&2

# Launch in the background so we can drive the cmd file from this script.
# Audio muted (Sound.Mute=true) — captures don't need it, and the test pipeline
# elsewhere keeps audio off by default. Output suppressed so the user sees
# only the PNG path on stdout when --wait completes.
LOBBY_TAB_ARG=""
if [ -n "${LOBBY_TAB}" ]; then
	LOBBY_TAB_ARG="Test.OpenLobbyTab=${LOBBY_TAB}"
fi

# Backup settings.yaml around the run — same pattern as run-test.sh, since the
# engine may auto-save Sound.Mute=true otherwise.
SUPPORT_DIR=""
SETTINGS_FILE=""
SETTINGS_BACKUP=""
case "$(uname)" in
	Darwin) SUPPORT_DIR="${HOME}/Library/Application Support/OpenRA" ;;
	Linux)  SUPPORT_DIR="${HOME}/.config/openra" ;;
	MINGW*|MSYS*|CYGWIN*)
		# Git Bash / MSYS on Windows: the engine's support dir is %APPDATA%/OpenRA.
		# APPDATA is exported into the MSYS environment; guard anyway.
		if [ -n "${APPDATA:-}" ]; then
			SUPPORT_DIR="${APPDATA}/OpenRA"
		fi ;;
esac
if [ -n "${SUPPORT_DIR}" ]; then
	SETTINGS_FILE="${SUPPORT_DIR}/settings.yaml"
fi
if [ -n "${SETTINGS_FILE}" ] && [ -f "${SETTINGS_FILE}" ]; then
	SETTINGS_BACKUP="${RESULT_DIR}/settings.yaml.bak.lobby"
	cp "${SETTINGS_FILE}" "${SETTINGS_BACKUP}"
fi

# Move the skirmish restore file aside for the run. SkirmishLogic.ClientJoined
# replays skirmish.ww3mod.yaml when the local client joins and issues a server
# "map <uid>" command from it — overriding Test.LaunchLobbyMap AFTER the lobby
# was seeded (that's the "changed the map to <last-played>" chat line). With
# the file absent the seed sticks and SkirmishLogic just adds its default bot.
# cleanup() restores the user's original, clobbering whatever the test game
# re-saved on LobbyInfoSynced.
SKIRMISH_FILE=""
SKIRMISH_BACKUP=""
if [ -n "${SUPPORT_DIR}" ] && [ -f "${SUPPORT_DIR}/skirmish.ww3mod.yaml" ]; then
	SKIRMISH_FILE="${SUPPORT_DIR}/skirmish.ww3mod.yaml"
	SKIRMISH_BACKUP="${RESULT_DIR}/skirmish.ww3mod.yaml.bak.lobby"
	mv "${SKIRMISH_FILE}" "${SKIRMISH_BACKUP}"
fi

./launch-game.sh \
	"Test.Mode=true" \
	"Test.Name=lobby-screenshot" \
	"Test.ScreenshotDir=${SCREENSHOT_DIR}" \
	"Test.ScreenshotCmdFile=${CMD_FILE}" \
	"Test.OpenSkirmishLobby=true" \
	"Test.LaunchLobbyMap=${LOBBY_MAP}" \
	"Test.LobbyReadyFile=${READY_FILE}" \
	${LOBBY_TAB_ARG} \
	"Sound.Mute=true" \
	>/dev/null 2>&1 &
GAME_PID=$!

# Restore settings.yaml + reap the game if we exit early.
cleanup() {
	# Best-effort tear down. The "quit" command path is preferred, but if we
	# crash on a timeout below, hard-kill the PID so we don't leave the game
	# stranded.
	if [ ${DO_QUIT} -eq 1 ] && kill -0 ${GAME_PID} 2>/dev/null; then
		kill ${GAME_PID} 2>/dev/null || true
	fi
	if [ -n "${SETTINGS_BACKUP}" ] && [ -f "${SETTINGS_BACKUP}" ]; then
		mv "${SETTINGS_BACKUP}" "${SETTINGS_FILE}"
	fi
	if [ -n "${SKIRMISH_BACKUP}" ] && [ -f "${SKIRMISH_BACKUP}" ]; then
		mv "${SKIRMISH_BACKUP}" "${SKIRMISH_FILE}"
	fi
}
trap cleanup EXIT INT TERM

# Phase 1: wait for lobby ready marker.
echo "==> Waiting for lobby ready signal (timeout: ${PHASE_TIMEOUT}s)..." >&2
DEADLINE=$(( $(date +%s) + PHASE_TIMEOUT ))
while [ "$(date +%s)" -lt ${DEADLINE} ]; do
	if [ -f "${READY_FILE}" ]; then
		break
	fi
	if ! kill -0 ${GAME_PID} 2>/dev/null; then
		echo "Error: game process exited before lobby was ready" >&2
		exit 3
	fi
	sleep 0.2
done
if [ ! -f "${READY_FILE}" ]; then
	echo "Error: timed out waiting for lobby ready marker at ${READY_FILE}" >&2
	exit 2
fi

# Give the lobby one extra beat to finish painting (player rows, map preview
# scale-in). The ready signal fires the moment MapIsPlayable goes true, which
# is before the first paint of the lobby's slot rebuild.
sleep 0.5

# Phase 2: request the screenshot.
PRE_COUNT=0
if [ -f "${MANIFEST_FILE}" ]; then
	PRE_COUNT=$(grep -o '"path":"' "${MANIFEST_FILE}" 2>/dev/null | wc -l | tr -d ' ')
fi

printf "screenshot %s\n" "${LABEL}" > "${CMD_FILE}"
echo "==> Sent: screenshot ${LABEL}" >&2

# Phase 3: wait for the manifest to grow.
DEADLINE=$(( $(date +%s) + PHASE_TIMEOUT ))
NEW_PATH=""
while [ "$(date +%s)" -lt ${DEADLINE} ]; do
	if [ -f "${MANIFEST_FILE}" ]; then
		CUR_COUNT=$(grep -o '"path":"' "${MANIFEST_FILE}" 2>/dev/null | wc -l | tr -d ' ')
		if [ "${CUR_COUNT}" -gt "${PRE_COUNT}" ]; then
			NEW_PATH=$(grep -o '"path":"[^"]*"' "${MANIFEST_FILE}" | tail -1 | sed 's/"path":"\(.*\)"/\1/')
			break
		fi
	fi
	sleep 0.2
done
if [ -z "${NEW_PATH}" ]; then
	echo "Error: timed out waiting for screenshot to appear in ${MANIFEST_FILE}" >&2
	exit 4
fi

# The manifest entry is added the moment TakeScreenshot enqueues the request;
# the actual PNG write happens one render frame later. Spin until the file
# materialises (Renderer.SaveScreenshot is sync inside TestMode, so this is
# bounded by one tick at most).
PNG_DEADLINE=$(( $(date +%s) + 5 ))
while [ "$(date +%s)" -lt ${PNG_DEADLINE} ]; do
	if [ -f "${NEW_PATH}" ]; then
		break
	fi
	sleep 0.1
done
if [ ! -f "${NEW_PATH}" ]; then
	echo "Error: manifest says ${NEW_PATH} but the PNG never appeared on disk" >&2
	exit 5
fi

echo "${NEW_PATH}"

# Phase 4: quit the game cleanly, unless --no-quit.
if [ ${DO_QUIT} -eq 1 ]; then
	printf "quit\n" > "${CMD_FILE}"
	# Wait up to PHASE_TIMEOUT seconds for the process to exit on its own.
	QUIT_DEADLINE=$(( $(date +%s) + PHASE_TIMEOUT ))
	while [ "$(date +%s)" -lt ${QUIT_DEADLINE} ]; do
		if ! kill -0 ${GAME_PID} 2>/dev/null; then
			break
		fi
		sleep 0.2
	done
	# If still alive, hard-kill via cleanup trap.
else
	echo "==> Game left running (PID ${GAME_PID}). Send more shots with screenshot.sh, or kill it manually." >&2
	# Detach so the script can exit while the game keeps going.
	trap - EXIT
fi

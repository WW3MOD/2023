#!/bin/sh
# WW3MOD — open a recorded replay through the real compatibility prompt, photograph
# whatever dialog it raises, take the "Watch Anyway" button, and photograph what
# happens next.
#
# Usage:  ./tools/autotest/watch-replay.sh <path-to.orarep> [--no-confirm]
#
# Launch.Replay routes through ReplayUtils.PromptReplayCompatibility exactly as the
# replay browser does (BlankLoadScreen.cs), so this exercises the shipped decision and
# the shipped dialog rather than a reconstruction of them.
#
# WHY A `click` COMMAND AND NOT A MOUSE. Driving the host's cursor would be scripting
# the user's desktop; the command file already reaches into the game, so the button is
# pressed through its own OnClick — the same handler a real click runs.
#
# WHAT THE SECOND CAPTURE IS FOR. A press that merely DISMISSES the dialog is consumed
# exactly as happily as one that continues into playback, so "the button worked" is not
# something the click's return value can tell you. The discriminator is on screen:
# cancel runs Game.LoadShellMap and lands on the MAIN MENU, confirm runs Game.JoinReplay
# and lands in the WORLD with the replay/observer chrome. Hence a shot after the click.

set -e

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

REPLAY=""
CONFIRM=1
while [ $# -gt 0 ]; do
	case "$1" in
		--no-confirm) CONFIRM=0; shift ;;
		*) REPLAY="$1"; shift ;;
	esac
done

if [ -z "${REPLAY}" ] || [ ! -f "${REPLAY}" ]; then
	echo "Usage: $0 <path-to.orarep> [--no-confirm]" >&2
	exit 1
fi

RUN_ID="manual_replay_$(date +%y%m%d_%H%M%S)"
RUN_DIR="${HOME}/.ww3mod-tests/screenshots/${RUN_ID}"
CMD_FILE="${RUN_DIR}/cmd.txt"
mkdir -p "${RUN_DIR}"
rm -f "${CMD_FILE}"

ENGINE_LOG="${HOME}/Library/Application Support/OpenRA/Logs/debug.log"

echo "==> Replay:  ${REPLAY}"
echo "==> Run dir: ${RUN_DIR}"

# The engine log is a fixed path with no run identity, so a stale one reads exactly like
# a current one. Empty it first, then poll-copy it WHILE the run is in flight rather than
# once at the end -- another worker's clear is free to fire the moment this run stops.
: > "${ENGINE_LOG}" 2>/dev/null || true
( while :; do cp "${ENGINE_LOG}" "${RUN_DIR}/debug.log" 2>/dev/null || true; sleep 2; done ) &
LOGCOPY_PID=$!

# Windowed, overriding launch-game.sh's PseudoFullscreen default: a fullscreen launch
# switches the display mode and takes the whole screen off whoever is using it. Last-wins
# arg semantics mean these override the defaults baked into the launcher.
./launch-game.sh \
	"Graphics.Mode=Windowed" \
	"Graphics.WindowedSize=1600,900" \
	"Test.Mode=true" \
	"Test.Name=replay-dialog" \
	"Test.ScreenshotDir=${RUN_DIR}" \
	"Test.ScreenshotCmdFile=${CMD_FILE}" \
	"Launch.Replay=${REPLAY}" > "${RUN_DIR}/game-stdout.log" 2>&1 &
GAME_PID=$!

cleanup() {
	kill "${LOGCOPY_PID}" 2>/dev/null || true
	cp "${ENGINE_LOG}" "${RUN_DIR}/debug.log" 2>/dev/null || true
}
trap cleanup EXIT

# Consumption, not elapsed time, is the readiness signal: PollCommands deletes the file
# after reading it, so its disappearance proves the game reached LogicTick and executed
# the command. Sleeping a guessed number of seconds instead is how a shot lands on a
# black loading frame.
send() {
	printf '%s\n' "$1" > "${CMD_FILE}"
	i=0
	while [ -f "${CMD_FILE}" ]; do
		i=$((i + 1))
		if [ "${i}" -gt 150 ]; then
			echo "!! command never consumed after 150s: $1" >&2
			return 1
		fi
		if ! kill -0 "${GAME_PID}" 2>/dev/null; then
			echo "!! game exited before consuming: $1" >&2
			return 1
		fi
		sleep 1
	done
	echo "==> consumed: $1"
}

send "screenshot 01-prompt" || exit 1
sleep 2

if [ "${CONFIRM}" -eq 1 ]; then
	send "click CONFIRM_BUTTON" || exit 1
	# Joining a replay tears down the menu world and loads the recorded map, which is not
	# instant. Two shots rather than one so a slow load cannot be mistaken for a dismissal.
	sleep 8
	send "screenshot 02-after-confirm" || exit 1
	sleep 6
	send "screenshot 03-playback" || exit 1
fi

send "quit" || true
wait "${GAME_PID}" 2>/dev/null || true

echo
echo "==> Captures:"
ls -1 "${RUN_DIR}"/*.png 2>/dev/null || echo "   (none)"
echo "==> Manifest: ${RUN_DIR}/manifest.json"

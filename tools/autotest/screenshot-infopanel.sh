#!/bin/sh
# WW3MOD — photograph the Esc-menu info panel (GAME_INFO_PANEL) as a SPECTATOR.
#
# Usage:  ./tools/autotest/screenshot-infopanel.sh [<path-to.orarep>]
#
# WHY A REPLAY AND NOT A SKIRMISH. The panel has two layouts and the interesting
# one is the spectator's: GameInfoStatsLogic branches on
# `player != null && !player.NonCombatant` (:97), and only the else branch hides
# the objective block and shifts the table up — the clamp under test. A skirmish
# launched with Launch.Map seats the local client in a slot, so LocalPlayer is
# non-null and that branch never runs. Joining a replay lands in the world as an
# observer with LocalPlayer null, which is the same state the reported screenshot
# was taken in.
#
# WHY click AND NOT A SECOND LAUNCH. Test.OpenIngameInfoPanel fires once, so it
# can open one tab per process. The tab strip's buttons are reachable through the
# cmd file's `click` verb, which invokes the button's own OnClick — the same
# handler a real click runs — so both tabs come out of one launch slot.
#
# FAILURE IS LOUD AND IS NOT READ THROUGH A PIPE. Every capture is verified as a
# file on disk with a non-trivial byte size, and the summary is written to
# result.txt in the run dir. `cmd | tail` returns tail's exit code and has
# inverted a verdict in this project twice; nothing here is piped.

set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

# NEWEST, not largest. Picking the largest file as a proxy for "longest match"
# selected a 13MB replay from three months ago, which the engine rejected with
# "Incompatible Replay — Replay metadata could not be read." before ever loading
# a world; the run then photographed that dialog twice and reported two captures.
# Replay metadata is read backwards from the end of the file and is build-
# sensitive, so recency is the property that matters, not size.
REPLAY="${1:-}"
if [ -z "${REPLAY}" ]; then
	REPLAY="$(ls -1t "${HOME}/Library/Application Support/OpenRA/Replays/ww3mod/release-20230225/"*.orarep 2>/dev/null | head -1 || true)"
fi

if [ -z "${REPLAY}" ] || [ ! -f "${REPLAY}" ]; then
	echo "!! no replay found; pass one explicitly" >&2
	exit 2
fi

RUN_ID="manual_infopanel_$(date +%y%m%d_%H%M%S)"
RUN_DIR="${HOME}/.ww3mod-tests/screenshots/${RUN_ID}"
CMD_FILE="${RUN_DIR}/cmd.txt"
RESULT="${RUN_DIR}/result.txt"
mkdir -p "${RUN_DIR}"
rm -f "${CMD_FILE}"

ENGINE_LOG="${HOME}/Library/Application Support/OpenRA/Logs/debug.log"

echo "==> Replay:  ${REPLAY}"
echo "==> Run dir: ${RUN_DIR}"

# debug.log is a fixed global path with no run identity, so a stale one reads
# exactly like a current one. Empty it, then poll-copy while the run is in flight.
: > "${ENGINE_LOG}" 2>/dev/null || true
( while :; do cp "${ENGINE_LOG}" "${RUN_DIR}/debug.log" 2>/dev/null || true; sleep 2; done ) &
LOGCOPY_PID=$!

# Windowed on purpose: launch-game.sh defaults to PseudoFullscreen, which switches
# the display mode and takes the whole screen off whoever is at the machine.
# Last-wins arg semantics let these override the launcher's defaults.
./launch-game.sh \
	"Graphics.Mode=Windowed" \
	"Graphics.WindowedSize=1600,900" \
	"Test.Mode=true" \
	"Test.Name=infopanel-a" \
	"Test.ScreenshotDir=${RUN_DIR}" \
	"Test.ScreenshotCmdFile=${CMD_FILE}" \
	"Test.OpenIngameInfoPanel=Objectives" \
	"Launch.Replay=${REPLAY}" > "${RUN_DIR}/game-stdout.log" 2>&1 &
GAME_PID=$!

cleanup() {
	kill "${LOGCOPY_PID}" 2>/dev/null || true
	cp "${ENGINE_LOG}" "${RUN_DIR}/debug.log" 2>/dev/null || true
	kill "${GAME_PID}" 2>/dev/null || true
}
trap cleanup EXIT

# Consumption, not elapsed time, is the readiness signal: PollCommands deletes the
# file after reading it, so its disappearance proves the game reached LogicTick and
# ran the command. Blind-sleeping a guessed number of seconds is how a shot lands
# on a black loading frame.
send() {
	printf '%s\n' "$1" > "${CMD_FILE}"
	i=0
	while [ -f "${CMD_FILE}" ]; do
		i=$((i + 1))
		if [ "${i}" -gt 120 ]; then
			echo "!! command never consumed after 120s: $1" >&2
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

# Launch.Replay routes through ReplayUtils.PromptReplayCompatibility. A version
# mismatch raises a "Watch Anyway" prompt whose button is CONFIRM_BUTTON; an
# UNREADABLE replay raises a different, terminal dialog whose only button is OK
# and which never continues into playback. Neither click is fatal here — the
# BUTTON2 check after the captures is what distinguishes "reached the world" from
# "sat on a dialog".
sleep 12
send "click CONFIRM_BUTTON" || true
sleep 10

# The panel opens itself via Test.OpenIngameInfoPanel once the observer HUD loads.
send "screenshot 01-spectator-objectives" || true
sleep 3

# Objectives / Options / How to Play => TAB_CONTAINER_3, so Options is BUTTON2.
# Debug needs a LocalPlayer and Chat needs >1 non-bot client, so neither is present
# for a replay observer.
send "click BUTTON2" || true
sleep 3
send "screenshot 02-spectator-options" || true
sleep 3

send "quit" || true
i=0
while kill -0 "${GAME_PID}" 2>/dev/null && [ "${i}" -lt 30 ]; do i=$((i + 1)); sleep 1; done
kill "${GAME_PID}" 2>/dev/null || true

# ---- verdict, from files on disk ----
STATUS="PASS"
{
	echo "run_dir=${RUN_DIR}"
	echo "replay=${REPLAY}"
} > "${RESULT}"

SHOTS=0
for png in "${RUN_DIR}"/*.png; do
	[ -f "${png}" ] || continue
	SHOTS=$((SHOTS + 1))
	BYTES=$(wc -c < "${png}" | tr -d ' ')
	echo "shot=${png} bytes=${BYTES}" >> "${RESULT}"
	# An almost-flat frame compresses to nothing: a black loading frame came back
	# at 59KB against 1.6MB for the real one. Small is a blank-frame smell, not proof.
	if [ "${BYTES}" -lt 120000 ]; then
		echo "warn=SUSPICIOUSLY_SMALL ${png}" >> "${RESULT}"
		STATUS="SUSPECT"
	fi
done

echo "shots=${SHOTS}" >> "${RESULT}"
if [ "${SHOTS}" -lt 2 ]; then
	STATUS="NO-RESULT"
	echo "error=expected 2 captures, got ${SHOTS}" >> "${RESULT}"
fi

# Two byte-identical captures mean nothing changed on screen between them, so the
# tab was never switched and neither frame is of what it claims to be. This is the
# check that would have caught the incompatible-replay run immediately: it produced
# two files with the same md5 and reported them as two captures.
DISTINCT=$(md5 -q "${RUN_DIR}"/*.png 2>/dev/null | sort -u | wc -l | tr -d ' ')
echo "distinct_frames=${DISTINCT}" >> "${RESULT}"
if [ "${SHOTS}" -ge 2 ] && [ "${DISTINCT}" -lt 2 ]; then
	STATUS="NO-RESULT"
	echo "error=all captures are byte-identical; the tab never changed" >> "${RESULT}"
fi

# The tab click is the proof the panel was on screen at all. The engine logs a miss
# verbatim, so this cannot pass by silence: if BUTTON2 was not found, the world
# never loaded or the panel never opened, and both frames are of something else.
if grep -aq "external click: BUTTON2 → NO SUCH VISIBLE WIDGET" "${RUN_DIR}/debug.log" 2>/dev/null; then
	STATUS="NO-RESULT"
	echo "error=BUTTON2 not found; the info panel was never open" >> "${RESULT}"
fi

echo "status=${STATUS}" >> "${RESULT}"

echo
echo "===================== ${STATUS} ====================="
cat "${RESULT}"
echo "====================================================="

[ "${STATUS}" = "PASS" ] || exit 1

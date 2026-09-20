#!/bin/sh
# WW3MOD — photograph the map editor's Zones tool, with no human at the mouse.
#
# Usage:  ./tools/autotest/screenshot-editor-zones.sh [<map-directory-name>]
#         (default: twin-rivers-ww3)
#
# WHAT IT CAPTURES. Two frames from ONE launch:
#   01-zones-intact   the Zones panel with the map's authored DMZ drawn, readout
#                     reading "DMZ splits the map into 2 areas."
#   02-zones-cut      the same panel after a scripted erase stroke cuts the band,
#                     readout red and reading "does NOT split the map (1 area)".
# The second frame is the whole point: the red state is the one that cannot be
# reasoned about from the code, and it is unreachable without either a cursor or
# a scripted stroke.
#
# WHY NOT `click` FOR THE TOOL. The Zones tool lives in a DROPDOWN, and a
# dropdown's items are built inside ShowDropDown — until a human opens it there
# is no "Zones" widget in the tree at all, so the cmd file's `click` verb (which
# matches a visible widget by id) reports NO SUCH VISIBLE WIDGET and the driver
# photographs the marker panel while claiming to have photographed this one.
# MapToolsLogic therefore reads Test.EditorTool directly. Same reasoning as
# Test.OpenIngameInfoPanel existing alongside `click`.
#
# SELECTING A TOOL IS NOT SHOWING ITS TAB, and this driver's first run is the
# proof. It waited for `editor zone selected:`, got it, took two frames and
# reported PASS — and both frames showed the TILES tab, because the Tools panel
# lives inside TOOLS_WIDGETS and tab visibility belongs to MapEditorTabsLogic,
# whose menuType defaults to Tiles. Every marker the driver checked was about the
# TOOL; none was about the CONTAINER it is drawn in, so the run was green on a
# capture of the wrong panel. Test.EditorTool now flips the tab too, and — the
# part that actually makes this safe — the PASS below rests on `zone panel
# shown:`, which is emitted from the readout label's own GetText and therefore
# cannot be written unless that label was really rendered.
#
# GENERALISE: a capture driver's evidence must come from the thing that would be
# IN THE PHOTOGRAPH, not from the mechanism you were exercising.
#
# THE STROKE GOES THROUGH THE BRUSH'S OWN ACTION. `zone-erase x,y,size` arms
# TestMode.ZoneStroke; EditorZoneBrush.Tick replays it through the same
# PaintZoneEditorAction a dragged stroke uses, so it is undoable, appears in the
# editor's history, and moves the revision counter the readout watches. A verb
# that wrote cells directly would paint the same pixels and prove less.
#
# FAILURE IS LOUD AND IS NOT READ THROUGH A PIPE. Every capture is verified as a
# file on disk with a non-trivial byte size; the two frames must differ; and the
# engine's own miss markers are grepped out of debug.log. `cmd | tail` returns
# tail's exit code and has inverted a verdict in this project twice, so nothing
# here is piped into a verdict.
#
# IT DOES NOT BUILD. launch-game.sh runs an already-built tree; run `.\make.ps1
# all` (or `make all`) first or the run photographs a stale engine.

set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

MAP_NAME="${1:-twin-rivers-ww3}"

# Cut the band here. twin-rivers' DMZ is a three-cell vertical band at x59-61 for
# most of its length (see its map.yaml `Zones: DMZ`).
#
# VERIFIED STATICALLY, NOT GUESSED, against the same flood the readout runs
# (defcon_wall_audit.engine_load_gate, which mirrors DefconWallRegion): the intact
# band is 399 cells in Bounds and 2 components; a size-9 disc at (60,60) removes
# 27 of them, leaving 372 cells and 1 component. So this cut really does turn the
# readout red, and the run is not hoping it does.
#
# Pass a different map and this cell almost certainly misses that map's band —
# which the run REPORTS rather than photographs, because the engine logs
# "CHANGED NOTHING" and the verdict below greps for it.
CUT_X=60
CUT_Y=60
CUT_SIZE=9

# ---- platform (mirrors tools/autotest/run-test.sh) ------------------------------
IS_WINDOWS=0
case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*|Windows_NT) IS_WINDOWS=1 ;;
esac

# Paths handed to the .NET game process must be Windows-form on Git-Bash.
to_game_path() {
	if [ "${IS_WINDOWS}" = "1" ] && command -v cygpath >/dev/null 2>&1; then
		cygpath -w "$1"
	else
		printf '%s' "$1"
	fi
}

# The PID we hold is the launch-game.sh shell; on Git-Bash the real game is a
# dotnet.exe child, so a plain kill of the shell orphans it.
kill_game() {
	_pid="$1"
	if [ "${IS_WINDOWS}" = "1" ]; then
		_winpid=""
		[ -r "/proc/${_pid}/winpid" ] && _winpid=$(cat "/proc/${_pid}/winpid" 2>/dev/null || true)
		if [ -n "${_winpid}" ] && command -v taskkill >/dev/null 2>&1; then
			taskkill //PID "${_winpid}" //T //F >/dev/null 2>&1 || true
		fi
	else
		command -v pkill >/dev/null 2>&1 && pkill -P "${_pid}" 2>/dev/null || true
	fi
	kill "${_pid}" 2>/dev/null || true
}

find_debug_log() {
	case "$(uname -s)" in
		Darwin) echo "${HOME}/Library/Application Support/OpenRA/Logs/debug.log" ;;
		Linux)  echo "${HOME}/.config/openra/Logs/debug.log" ;;
		MINGW*|MSYS*|CYGWIN*|Windows_NT)
			for _c in \
				"${REPO_ROOT}/engine/Support/Logs/debug.log" \
				"$(cygpath -u "${APPDATA:-}" 2>/dev/null)/OpenRA/Logs/debug.log" \
				"$(cygpath -u "${USERPROFILE:-}" 2>/dev/null)/Documents/OpenRA/Logs/debug.log"; do
				if [ -f "${_c}" ]; then echo "${_c}"; return; fi
			done
			;;
	esac
}

# md5 is macOS, md5sum is everything else. Only used to tell two frames apart.
hash_file() {
	if command -v md5sum >/dev/null 2>&1; then
		md5sum "$1" | cut -d' ' -f1
	elif command -v md5 >/dev/null 2>&1; then
		md5 -q "$1"
	else
		wc -c < "$1" | tr -d ' '
	fi
}

if [ ! -d "mods/ww3mod/maps/${MAP_NAME}" ]; then
	echo "!! no such map directory: mods/ww3mod/maps/${MAP_NAME}" >&2
	exit 2
fi

if [ ! -f "engine/bin/OpenRA.dll" ]; then
	echo "!! engine/bin/OpenRA.dll missing — build first (.\\make.ps1 all)" >&2
	exit 2
fi

RUN_ID="manual_editor-zones_$(date +%y%m%d_%H%M%S)"
RUN_DIR="${HOME}/.ww3mod-tests/screenshots/${RUN_ID}"
CMD_FILE="${RUN_DIR}/cmd.txt"
RESULT="${RUN_DIR}/result.txt"
RUN_LOG="${RUN_DIR}/debug.log"
mkdir -p "${RUN_DIR}"
rm -f "${CMD_FILE}"

ENGINE_LOG="$(find_debug_log || true)"
if [ -z "${ENGINE_LOG}" ]; then
	# Not yet created on a fresh checkout. Pick the platform's canonical path so
	# the truncate-and-poll below still has somewhere to look once it appears.
	case "$(uname -s)" in
		MINGW*|MSYS*|CYGWIN*|Windows_NT) ENGINE_LOG="$(cygpath -u "${APPDATA:-}" 2>/dev/null)/OpenRA/Logs/debug.log" ;;
		Darwin) ENGINE_LOG="${HOME}/Library/Application Support/OpenRA/Logs/debug.log" ;;
		*) ENGINE_LOG="${HOME}/.config/openra/Logs/debug.log" ;;
	esac
fi

echo "==> Map:     ${MAP_NAME}"
echo "==> Run dir: ${RUN_DIR}"
echo "==> Log:     ${ENGINE_LOG}"

# debug.log is a fixed global path with no run identity, so a stale one reads
# exactly like a current one. Empty it, then poll-copy while the run is in flight.
: > "${ENGINE_LOG}" 2>/dev/null || true
( while :; do cp "${ENGINE_LOG}" "${RUN_LOG}" 2>/dev/null || true; sleep 2; done ) &
LOGCOPY_PID=$!

# Windowed on purpose. launch-game defaults to PseudoFullscreen, which switches
# the display mode and takes the whole screen off whoever is at the machine.
# Last-wins arg semantics let these override the launcher's defaults.
./launch-game.sh \
	"Graphics.Mode=Windowed" \
	"Graphics.WindowedSize=1600,900" \
	"Test.Mode=true" \
	"Test.Name=editor-zones" \
	"Test.ScreenshotDir=$(to_game_path "${RUN_DIR}")" \
	"Test.ScreenshotCmdFile=$(to_game_path "${CMD_FILE}")" \
	"Test.OpenEditorMap=${MAP_NAME}" \
	"Test.EditorTool=Zones" > "${RUN_DIR}/game-stdout.log" 2>&1 &
GAME_PID=$!

cleanup() {
	kill "${LOGCOPY_PID}" 2>/dev/null || true
	cp "${ENGINE_LOG}" "${RUN_LOG}" 2>/dev/null || true
	kill_game "${GAME_PID}" 2>/dev/null || true
}
trap cleanup EXIT
trap 'echo; echo "==> interrupted"; exit 130' INT TERM

# Consumption, not elapsed time, is the readiness signal for a COMMAND:
# PollCommands deletes the file after reading it, so its disappearance proves the
# game reached LogicTick and ran the line.
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

# Readiness for the EDITOR is a log marker, not a sleep. MapZonesLogic logs this
# once the zone is selected, which is after the editor world exists and its chrome
# is built.
wait_for_log() {
	_needle="$1"; _limit="$2"; i=0
	while [ "${i}" -lt "${_limit}" ]; do
		cp "${ENGINE_LOG}" "${RUN_LOG}" 2>/dev/null || true
		if grep -aq "${_needle}" "${RUN_LOG}" 2>/dev/null; then
			echo "==> saw: ${_needle}"
			return 0
		fi
		if ! kill -0 "${GAME_PID}" 2>/dev/null; then
			echo "!! game exited while waiting for: ${_needle}" >&2
			return 1
		fi
		i=$((i + 1)); sleep 1
	done
	echo "!! timed out waiting for: ${_needle}" >&2
	return 1
}

# READINESS IS "THE READOUT WAS DRAWN", NOT "THE TOOL WAS SELECTED". Those are
# different facts and the difference cost a whole capture run: the first version
# of this driver waited on `editor zone selected:`, passed, and returned two
# frames of the TILES tab. Selecting a tool inside TOOLS_WIDGETS says nothing
# about whether TOOLS_WIDGETS is the visible tab — that lives in
# MapEditorTabsLogic, which defaults to Tiles.
#
# `zone panel shown:` is logged from inside the readout label's own GetText, and
# LabelWidget.Draw is its only caller while Widget.DrawOuter early-returns on an
# invisible widget. So the line cannot exist unless that label was RENDERED, with
# that text, in a real frame. components= is the machine-readable half; the
# sentence is a Fluent string and would move under a reword.
READY=1
wait_for_log "zone panel shown: components=2" 120 || READY=0

# NEVER FIRE A CAPTURE OFF THE LOAD MARKER ITSELF. World setup is logged well
# before that world's first render pass, and a shot taken the instant the marker
# appears comes back as menu widgets over a completely black frame — which is
# indistinguishable from a map that failed to load. Wait a beat; the byte-size
# check below is the backstop.
sleep 8

send "screenshot 01-zones-intact" || true
sleep 3

# Cut the band. The readout is throttled to 250ms and keyed on the layer's
# revision, so three seconds is ample for it to re-flood and turn red.
send "zone-erase ${CUT_X},${CUT_Y},${CUT_SIZE}" || true

# Same evidence again for the red state, and it is what ties FRAME 02 to it: the
# line only appears once the label has been drawn with the new text.
CUT_SHOWN=1
wait_for_log "zone panel shown: components=1" 60 || CUT_SHOWN=0
sleep 2

send "screenshot 02-zones-cut" || true
sleep 3

send "quit" || true
i=0
while kill -0 "${GAME_PID}" 2>/dev/null && [ "${i}" -lt 30 ]; do i=$((i + 1)); sleep 1; done
kill_game "${GAME_PID}" 2>/dev/null || true

kill "${LOGCOPY_PID}" 2>/dev/null || true
cp "${ENGINE_LOG}" "${RUN_LOG}" 2>/dev/null || true

# ---- verdict, from files on disk ------------------------------------------------
STATUS="PASS"
{
	echo "run_dir=${RUN_DIR}"
	echo "map=${MAP_NAME}"
	echo "cut=${CUT_X},${CUT_Y} size ${CUT_SIZE}"
	grep -a "zone panel shown:" "${RUN_LOG}" 2>/dev/null | sed 's/^/readout=/' || true
} > "${RESULT}"

SHOTS=0
for png in "${RUN_DIR}"/*.png; do
	[ -f "${png}" ] || continue
	SHOTS=$((SHOTS + 1))
	BYTES=$(wc -c < "${png}" | tr -d ' ')
	echo "shot=${png} bytes=${BYTES}" >> "${RESULT}"
	# An almost-flat frame compresses to nothing: a black loading frame came back
	# at 59KB against 1.6MB for a real one. Small is a blank-frame smell, not proof.
	if [ "${BYTES}" -lt 120000 ]; then
		echo "warn=SUSPICIOUSLY_SMALL ${png}" >> "${RESULT}"
		STATUS="SUSPECT"
	fi
done
echo "shots=${SHOTS}" >> "${RESULT}"

if [ "${READY}" != "1" ]; then
	STATUS="NO-RESULT"
	echo "error=the Zones readout was never DRAWN with a split band; frame 01 is not of this panel" >> "${RESULT}"
fi

if [ "${CUT_SHOWN}" != "1" ]; then
	STATUS="NO-RESULT"
	echo "error=the Zones readout was never DRAWN with a cut band; frame 02 is not of the red state" >> "${RESULT}"
fi

# Belt and braces on top of the two waits: assert the evidence is in the FINAL
# log copy as well, so a race in the poll-copy cannot leave a PASS resting on a
# line that was read once and never landed in the artefact the reader inspects.
for needle in 	"editor tab: Tools" 	"editor tool: Zones" 	"zone panel shown: components=2" 	"zone panel shown: components=1"; do
	if ! grep -aq "${needle}" "${RUN_LOG}" 2>/dev/null; then
		STATUS="NO-RESULT"
		echo "error=debug.log never showed '${needle}'" >> "${RESULT}"
	fi
done

if [ "${SHOTS}" -lt 2 ]; then
	STATUS="NO-RESULT"
	echo "error=expected 2 captures, got ${SHOTS}" >> "${RESULT}"
fi

# Byte-identical frames mean nothing changed on screen between them, so the erase
# never landed and the second frame is not of what it claims to be. This is the
# check that caught an incompatible-replay run photographing one dialog twice.
DISTINCT=0
if [ "${SHOTS}" -ge 1 ]; then
	DISTINCT=$(for png in "${RUN_DIR}"/*.png; do [ -f "${png}" ] && hash_file "${png}"; done | sort -u | wc -l | tr -d ' ')
fi
echo "distinct_frames=${DISTINCT}" >> "${RESULT}"
if [ "${SHOTS}" -ge 2 ] && [ "${DISTINCT}" -lt 2 ]; then
	STATUS="NO-RESULT"
	echo "error=all captures are byte-identical; the erase never changed the screen" >> "${RESULT}"
fi

# The engine's own miss markers. Each is logged verbatim and none throws, so
# without these greps a miss passes by silence and the run reports a capture of
# whatever happened to be on screen.
for marker in \
	"NO SUCH VISIBLE WIDGET" \
	"OpenEditorMap NO SUCH MAP" \
	"editor tool NO SUCH TOOL" \
	"zone stroke ignored"; do
	if grep -aq "${marker}" "${RUN_LOG}" 2>/dev/null; then
		STATUS="NO-RESULT"
		echo "error=debug.log contains '${marker}'" >> "${RESULT}"
	fi
done

# A stroke that parsed but hit no band cell leaves the readout green, so frame 02
# would look like frame 01 with the cursor moved. Distinct enough to pass the hash
# check, and still not the capture that was asked for.
if grep -aq "zone stroke applied:.*CHANGED NOTHING" "${RUN_LOG}" 2>/dev/null; then
	STATUS="NO-RESULT"
	echo "error=the erase stroke changed no cells; ${CUT_X},${CUT_Y} is not on this map's band" >> "${RESULT}"
fi

echo "status=${STATUS}" >> "${RESULT}"

echo
echo "===================== ${STATUS} ====================="
cat "${RESULT}"
echo "====================================================="

[ "${STATUS}" = "PASS" ] || exit 1

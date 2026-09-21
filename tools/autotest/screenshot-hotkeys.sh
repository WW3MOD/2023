#!/bin/sh
# WW3MOD — photograph the HOTKEY REFERENCE the player can actually reach:
# Esc > Settings > Hotkeys (SETTINGS_PANEL's HOTKEYS_PANEL tab).
#
# Usage:  ./tools/autotest/screenshot-hotkeys.sh [<map-id>]
#         default map: river-zeta-ww3
#
# WHY A SKIRMISH AND NOT A REPLAY, unlike screenshot-infopanel.sh next door. The tab strip under
# test is SettingsLogic's, not GameInfoLogic's, and it is reached through INGAME_MENU's SETTINGS
# button, which IngameMenuLogic.CreateSettingsButton (:480-493) creates unconditionally in any
# world. A replay would do; a skirmish is simpler because it needs no artifact that can age out.
#
# WHY Test.OpenIngameInfoPanel IS THE FIRST STEP AND NOT A click. Opening the Esc menu means
# clicking OPTIONS_BUTTON, a MenuButtonWidget whose OnClick MenuButtonsChromeLogic assigns
# (:42-55) -- reachable by the cmd file's `click` verb in principle, but the launch arg already
# does exactly this and is the path the other drivers use. It loads INGAME_MENU (MenuButtonWidget
# .MenuContainer defaults to "INGAME_MENU", MenuButtonWidget.cs:16), which is the screen carrying
# the SETTINGS button. The panel name passed here is irrelevant to this capture -- it only selects
# which GAME_INFO_PANEL tab shows on the right before we leave for Settings, so AutoSelect -- a
# real IngameInfoPanel value, unlike the misspelled LobbbyOptions -- is the honest thing to pass.
#
# THE TWO IDs ARE NOT GUESSES. IngameMenuLogic.AddButton assigns `button.Id = id` from the
# Buttons: list in ingame-menu.yaml:5, so the Esc menu's button is literally `SETTINGS`.
# SettingsLogic.AddSettingsTab assigns `tab.Id = id` where id is the PANEL key from
# settings.yaml:5-10, so the Hotkeys tab button is literally `HOTKEYS_PANEL`.
# TestModeScreenshots.ClickWidget finds by Id under IsVisible() and invokes the widget's own
# OnClick, which is the same handler a real click runs.
#
# FAILURE IS LOUD AND IS NOT READ THROUGH A PIPE. Every capture is verified as a file on disk
# with a non-trivial byte size and the summary is written to result.txt in the run dir.
# `cmd | tail` returns tail's exit code and has inverted a verdict in this project twice.

set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

MAP="${1:-river-zeta-ww3}"

RUN_ID="manual_hotkeys_$(date +%y%m%d_%H%M%S)"
RUN_DIR="${HOME}/.ww3mod-tests/screenshots/${RUN_ID}"
CMD_FILE="${RUN_DIR}/cmd.txt"
RESULT="${RUN_DIR}/result.txt"
mkdir -p "${RUN_DIR}"
rm -f "${CMD_FILE}"

ENGINE_LOG="${HOME}/Library/Application Support/OpenRA/Logs/debug.log"

echo "==> Map:     ${MAP}"
echo "==> Run dir: ${RUN_DIR}"

# debug.log is a fixed global path with no run identity, so a stale one reads exactly like a
# current one. Empty it, then poll-copy while the run is in flight.
: > "${ENGINE_LOG}" 2>/dev/null || true
( while :; do cp "${ENGINE_LOG}" "${RUN_DIR}/debug.log" 2>/dev/null || true; sleep 2; done ) &
LOGCOPY_PID=$!

# Windowed on purpose: launch-game.sh defaults to PseudoFullscreen, which switches the display
# mode and takes the whole screen off whoever is at the machine. 1600x900 is the size the other
# UI drivers here use, and the settings window is authored 900x600 (settings.yaml:11-12), so it
# sits centred with room around it at this resolution and is legible in one Read.
./launch-game.sh \
	"Graphics.Mode=Windowed" \
	"Graphics.WindowedSize=1600,900" \
	"Test.Mode=true" \
	"Test.Name=hotkeys-panel" \
	"Test.ScreenshotDir=${RUN_DIR}" \
	"Test.ScreenshotCmdFile=${CMD_FILE}" \
	"Test.OpenIngameInfoPanel=AutoSelect" \
	"Launch.Map=${MAP}" > "${RUN_DIR}/game-stdout.log" 2>&1 &
GAME_PID=$!

cleanup() {
	kill "${LOGCOPY_PID}" 2>/dev/null || true
	cp "${ENGINE_LOG}" "${RUN_DIR}/debug.log" 2>/dev/null || true
	kill "${GAME_PID}" 2>/dev/null || true
}
trap cleanup EXIT

# Consumption, not elapsed time, is the readiness signal: PollCommands deletes the file after
# reading it, so its disappearance proves the game reached LogicTick and ran the command.
# Blind-sleeping a guessed number of seconds is how a shot lands on a black loading frame.
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

sleep 20

# The Esc menu is already up via Test.OpenIngameInfoPanel. Leave it for Settings.
send "click SETTINGS" || true
sleep 3

# SettingsLogic opens on its first registered panel (DISPLAY_PANEL). Switch to Hotkeys.
send "click HOTKEYS_PANEL" || true
sleep 3

send "screenshot 01-hotkeys-panel-top" || true
sleep 3

# Filter down to the four groups this branch added, so one frame can show that all four exist and
# carry their fluent headings rather than raw slugs. FILTER_INPUT is a TextFieldWidget and has no
# OnClick, so `click` cannot reach it -- this is a SECOND shot of the same unfiltered list from a
# scrolled position, not a filtered one. Scrolling is likewise not scriptable today; if the four
# new groups fall below the fold in shot 01, that is the limitation to report, NOT a missing group.
send "screenshot 02-hotkeys-panel-second" || true
sleep 3

send "quit" || true
i=0
while kill -0 "${GAME_PID}" 2>/dev/null && [ "${i}" -lt 30 ]; do i=$((i + 1)); sleep 1; done
kill "${GAME_PID}" 2>/dev/null || true

# ---- verdict, from files on disk ----
# Take a final copy first: the poll-copy loop runs every 2s and the verdict reads debug.log.
cp "${ENGINE_LOG}" "${RUN_DIR}/debug.log" 2>/dev/null || true

STATUS="PASS"
COUNT=0
{
	echo "run: ${RUN_ID}"
	echo "map: ${MAP}"
	for f in "${RUN_DIR}"/*.png; do
		[ -f "${f}" ] || continue
		SIZE=$(wc -c < "${f}" | tr -d ' ')
		COUNT=$((COUNT + 1))
		# An almost-flat PNG compresses to nothing: a black frame is ~59 KB where a real one is
		# megabytes (SCREENSHOT.md "The tell for a blank frame is file size, not the image").
		if [ "${SIZE}" -lt 120000 ]; then
			echo "BLANK?  ${f}  ${SIZE} bytes"
			STATUS="NO-RESULT"
		else
			echo "ok      ${f}  ${SIZE} bytes"
		fi
	done
	if [ "${COUNT}" -eq 0 ]; then
		echo "no captures written at all"
		STATUS="NO-RESULT"
	fi
	# A MISSED CLICK IS THE FAILURE MODE THAT LOOKS LIKE A RESULT. `send` only waits for the cmd
	# file to be consumed, which happens whether or not the widget was found, so a click that fired
	# before the menu existed photographs the map or the Esc menu and reports two healthy PNGs.
	# TestModeScreenshots.cs:227 logs each dispatch; require both to have landed.
	for W in SETTINGS HOTKEYS_PANEL; do
		if grep -q "external click: ${W} .* dispatched" "${RUN_DIR}/debug.log" 2>/dev/null; then
			echo "click   ${W}: dispatched"
		else
			echo "click   ${W}: NOT DISPATCHED -- the frames do not show the hotkey panel"
			STATUS="NO-RESULT"
		fi
	done
	echo "status: ${STATUS}"
} > "${RESULT}"

cat "${RESULT}"
[ "${STATUS}" = "PASS" ] || exit 2

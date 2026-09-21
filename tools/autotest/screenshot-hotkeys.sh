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
# WHY THERE IS NO BLIND SLEEP BEFORE THE FIRST CLICK. The first cut of this script slept 20s and
# then clicked, and on run 260922_005942 both clicks missed: debug.log put
# `external click: SETTINGS -> NO SUCH VISIBLE WIDGET` ABOVE the sprite loads, `Scenario
# selection`, `[danger] reference`, `DEFCON wall region` and `Sync reports disabled` -- every one
# of them a world-construction line. The world was still loading, so there was no player HUD, no
# MenuButtonsChromeLogic and no INGAME_MENU to find. The second click landed one line after
# `Sync reports disabled`, i.e. missed by a hair. Both screenshots then came out as healthy
# 1,024,258-byte pictures of the Esc menu -- BYTE-IDENTICAL to each other, which is its own tell.
#
# THE FRAME DOES NOT LICENCE THE SLEEP. A capture at t~26s showing the menu says nothing about
# t=20s, and reading it as "20s was enough" is how this gets mis-fixed as an id problem. The ids
# were never wrong: IngameMenuLogic.AddButton:331 assigns `button.Id = id` from the bare string in
# ingame-menu.yaml:5's Buttons: list, and PollCommands Trim()s the verb argument.
#
# So click_until below RETRIES until debug.log says the click dispatched, which makes the
# precondition itself the wait. Both retries are safe to repeat: a second `click SETTINGS` after a
# successful one finds nothing, because CreateSettingsButton sets hideMenu = true and
# IngameMenuLogic:193 gates buttonContainer on !hideMenu; and re-clicking a settings tab just
# re-selects the tab it is already on.
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
# READ "NO SUCH VISIBLE WIDGET" AS TWO DIFFERENT FAILURES. ClickWidget returns false both when
# FindVisible found nothing AND when it found the widget but could not read a non-null `OnClick`
# field off it (TestModeScreenshots.cs:282-294), and the log line is the same either way. That
# matters here: HOTKEYS_PANEL is the id of BOTH the tab button (SettingsLogic.AddSettingsTab sets
# `tab.Id = id`) and the panel container (`container.Id = panel.Key`, same string). FindVisible
# walks Children FORWARD and returns the first match; SETTINGS_TAB_CONTAINER precedes
# PANEL_CONTAINER in settings.yaml and the container is IsVisible-gated on being the active panel,
# so the button wins -- but if that order ever changes, this script would retry forever against a
# container that has no OnClick, reporting a missing widget that is on screen.
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

# Has this id's click been reported as dispatched yet? Copies the log first: the poll-copy loop
# above only runs every 2s, and ClickWidget is deferred through Game.RunAfterTick, so the line
# lands a tick after the command is consumed.
dispatched() {
	cp "${ENGINE_LOG}" "${RUN_DIR}/debug.log" 2>/dev/null || true
	grep -q "external click: $1 .* dispatched" "${RUN_DIR}/debug.log" 2>/dev/null
}

# Send `click <id>` until debug.log says it dispatched, or give up. Checks BEFORE each send so a
# click that landed while we were not looking costs at most one extra no-op send.
click_until() {
	_id="$1"
	_deadline="${2:-150}"
	_t=0
	while :; do
		if dispatched "${_id}"; then
			echo "==> dispatched: ${_id} (after ${_t}s)"
			return 0
		fi

		if [ "${_t}" -ge "${_deadline}" ]; then
			echo "!! never dispatched within ${_deadline}s: ${_id}" >&2
			return 1
		fi

		send "click ${_id}" || return 1
		sleep 3
		_t=$((_t + 3))
	done
}

# One capture of wherever we actually got to, so a failure is diagnosable instead of silent.
# Named so it can never be mistaken for the frame this script exists to take.
bail() {
	echo "!! stuck at: $1" >&2
	send "screenshot 99-stuck-at-$1" || true
	sleep 3
	send "quit" || true
	FAILED_AT="$1"
}

FAILED_AT=""

# A floor, not a readiness wait -- click_until does the waiting. This only keeps the retry loop
# from hammering the cmd file through the first seconds of a load it cannot possibly beat.
sleep 12

# The Esc menu comes up on its own via Test.OpenIngameInfoPanel; SETTINGS does not exist until it
# does, so this retry IS the wait for the world to finish loading.
if click_until SETTINGS; then
	sleep 2
	# SettingsLogic opens on its first registered panel (DISPLAY_PANEL). Switch to Hotkeys.
	if click_until HOTKEYS_PANEL 60; then
		sleep 3
		send "screenshot 01-hotkeys-panel-top" || true
		sleep 3
	else
		bail HOTKEYS_PANEL
	fi
else
	bail SETTINGS
fi

# A duplicate of shot 01 three seconds later, against SCREENSHOT.md's one-frame-late sampling: it
# is NOT a different state and NOT a filtered view. FILTER_INPUT is a TextFieldWidget with no
# OnClick, so the `click` verb cannot reach it, and scrolling is not scriptable either; if some of
# the four new groups fall below the fold in shot 01, that is the framing limit to report, NOT a
# missing group. Skipped entirely when we never reached the panel -- bail() already took its own
# frame and quit, and a second picture of the wrong screen is what made run 260922_005942 read as
# two healthy captures.
if [ -z "${FAILED_AT}" ]; then
	send "screenshot 02-hotkeys-panel-second" || true
	sleep 3
	send "quit" || true
fi
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
	# before the menu existed photographs the Esc menu and writes a healthy multi-megabyte PNG --
	# which is exactly what run 260922_005942 did, twice, byte-identically.
	# TestModeScreenshots.cs:227 logs each dispatch; require both to have landed.
	for W in SETTINGS HOTKEYS_PANEL; do
		if grep -q "external click: ${W} .* dispatched" "${RUN_DIR}/debug.log" 2>/dev/null; then
			echo "click   ${W}: dispatched"
		else
			echo "click   ${W}: NOT DISPATCHED -- no frame here shows the hotkey panel"
			STATUS="NO-RESULT"
		fi
	done
	if [ -n "${FAILED_AT}" ]; then
		echo "stuck:  ${FAILED_AT} (see 99-stuck-at-${FAILED_AT}.png for where it got to)"
		STATUS="NO-RESULT"
	fi
	# Two identical frames mean nothing changed between them, which for this script means the
	# second shot photographed the same wrong screen as the first.
	if [ "${COUNT}" -eq 2 ] && cmp -s "${RUN_DIR}"/001_*.png "${RUN_DIR}"/002_*.png; then
		echo "warn:   the two frames are byte-identical"
	fi
	echo "status: ${STATUS}"
} > "${RESULT}"

cat "${RESULT}"
[ "${STATUS}" = "PASS" ] || exit 2

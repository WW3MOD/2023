#!/bin/sh
# WW3MOD developer test harness — single-test runner
#
# Usage:  ./tools/autotest/run-test.sh [position] [flags] <test-folder-name>
#
# Position shorthand (positional, before the test name; case-insensitive):
#   L | -L | --left      Left half of the screen
#   R | -R | --right     Right half of the screen
#   F | -F | --full      PseudoFullscreen
#                        (no shorthand → centered, ~90% × ~85%, default)
#
# Window-behavior flags (windowed mode only):
#   --background           (default) Visible, but pushed behind your other
#                          windows immediately after launch via osascript so
#                          your terminal keeps focus. Cmd+Tab to OpenRA brings
#                          it forward.
#   --minimized            Old behavior: SDL_MinimizeWindow into the dock.
#                          Restore by clicking the small icon next to Trash.
#   --visible              Stay foreground. (Alias: --no-minimize.)
#
# Audio flags:
#   --audio                Keep sound on. (run-demo.sh injects this.)
#   --mute                 Force mute. (Default for tests.)
#
# Speed:
#   --speed N              Run the sim at N× wall-clock (1-16). Divides
#                          world.Timestep via Test.SpeedMultiplier — pure
#                          pacing, simulation stays byte-identical. Default:
#                          unset (1×, current behavior).
#
# Timeout:
#   --timeout N            Hard wall-clock watchdog (seconds). If the game is
#                          still alive and no verdict has been written after N
#                          seconds, kill it and synthesize a FAIL result. Guards
#                          against maps whose rules fail to load and idle on the
#                          main menu forever (no Test.Pass/Fail ever runs).
#                          Default: 300. Pure wall-clock — NOT scaled by --speed.
#
# Misc:
#   --position=<centered|left|right|full>  Long form of L/R/F.
#   --fullscreen           Same as F + Mode=PseudoFullscreen.
#   --windowed             Force windowed (default; only useful when overriding
#                          a user settings.yaml that forces fullscreen).
#   --help                 Show this message.
#
# Defaults: windowed, centered (large but not full), background, muted, edge-pan
# disabled (engine-side, gated on Test.Mode + Mode=Windowed).
#
# macOS focus handling: PREV_APP is captured before launch and re-activated
# after the game exits, so the close-time focus shuffle doesn't yank you out
# of the terminal/editor you were typing in.
#
# Examples:
#   ./tools/autotest/run-test.sh test-paladin-fires           # background, muted
#   ./tools/autotest/run-test.sh L test-paladin-fires         # left half, background
#   ./tools/autotest/run-test.sh --visible --audio test-foo   # foreground, sound on
#   ./tools/autotest/run-test.sh --minimized test-foo         # old miniaturize behavior
#   ./tools/autotest/run-test.sh F test-foo                   # fullscreen
#   ./tools/autotest/run-test.sh --speed 8 test-foo           # run 8× wall-clock
#
# Exit code: 0=pass, 1=fail, 2=skip, 3=error.

set -e

GRAPHICS_MODE="Windowed"
POSITION="centered"
WINDOW_BEHAVIOR="background"
AUDIO_MUTE=1
SPEED_MULT=""
TIMEOUT_SECS=300

while [ $# -gt 0 ]; do
	case "$1" in
		L|l|-L|-l|--left)       POSITION="left"; shift ;;
		R|r|-R|-r|--right)      POSITION="right"; shift ;;
		F|f|-F|-f|--full)       POSITION="full"; shift ;;
		C|c|-C|-c|--centered)   POSITION="centered"; shift ;;
		--fullscreen)
			GRAPHICS_MODE="PseudoFullscreen"
			POSITION="full"
			shift ;;
		--windowed)             GRAPHICS_MODE="Windowed"; shift ;;
		--position=*)           POSITION="${1#*=}"; shift ;;
		--background)           WINDOW_BEHAVIOR="background"; shift ;;
		--minimized)            WINDOW_BEHAVIOR="minimized"; shift ;;
		--visible|--no-minimize|--foreground)
		                        WINDOW_BEHAVIOR="visible"; shift ;;
		--audio)                AUDIO_MUTE=0; shift ;;
		--mute)                 AUDIO_MUTE=1; shift ;;
		--speed=*)              SPEED_MULT="${1#*=}"; shift ;;
		--speed)                SPEED_MULT="$2"; shift 2 ;;
		--timeout=*)            TIMEOUT_SECS="${1#*=}"; shift ;;
		--timeout)              TIMEOUT_SECS="$2"; shift 2 ;;
		--help|-h)
			sed -n '2,61p' "$0" | sed 's/^# \?//'
			exit 0 ;;
		--*)
			echo "Unknown flag: $1"
			exit 3 ;;
		*)
			break ;;
	esac
done

TEST_NAME="$1"
if [ -z "${TEST_NAME}" ]; then
	echo "Usage: $0 [L|R|F] [--background|--minimized|--visible] [--audio] [--speed N] [--timeout N] <test-folder-name>"
	echo "  e.g.  $0 test-artillery-turret"
	exit 3
fi

# Validate --speed if supplied: integer 1-16 (matches TestMode arg clamp).
if [ -n "${SPEED_MULT}" ]; then
	case "${SPEED_MULT}" in
		''|*[!0-9]*)
			echo "Error: --speed must be an integer 1-16 (got '${SPEED_MULT}')"
			exit 3 ;;
	esac
	if [ "${SPEED_MULT}" -lt 1 ] || [ "${SPEED_MULT}" -gt 16 ]; then
		echo "Error: --speed must be 1-16 (got '${SPEED_MULT}')"
		exit 3
	fi
fi

# Validate --timeout: positive integer (seconds). Pure wall-clock; deliberately
# NOT scaled by --speed (a hung game never advances the sim, so speed is moot).
case "${TIMEOUT_SECS}" in
	''|*[!0-9]*)
		echo "Error: --timeout must be a positive integer (seconds), got '${TIMEOUT_SECS}'"
		exit 3 ;;
esac
if [ "${TIMEOUT_SECS}" -lt 1 ]; then
	echo "Error: --timeout must be >= 1 (got '${TIMEOUT_SECS}')"
	exit 3
fi

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

# Detect Git-Bash / MSYS / Cygwin. On those, paths handed to the .NET game
# process must be Windows-form (C:\...). Identity passthrough elsewhere so
# macOS/Linux behavior is byte-for-byte unchanged.
IS_WINDOWS=0
case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*|Windows_NT) IS_WINDOWS=1 ;;
esac
to_game_path() {
	if [ "${IS_WINDOWS}" = "1" ] && command -v cygpath >/dev/null 2>&1; then
		cygpath -w "$1"
	else
		printf '%s' "$1"
	fi
}

# Kill the backgrounded launcher and the game it spawned. The PID we hold is the
# launch-game.sh shell; on Git-Bash the actual game is a dotnet.exe child, so a
# plain `kill` of the shell can orphan it. Translate to the Windows PID and
# taskkill the whole tree (//T //F); fall back to POSIX kill everywhere.
kill_game() {
	_pid="$1"
	if [ "${IS_WINDOWS}" = "1" ]; then
		_winpid=""
		[ -r "/proc/${_pid}/winpid" ] && _winpid=$(cat "/proc/${_pid}/winpid" 2>/dev/null || true)
		if [ -n "${_winpid}" ] && command -v taskkill >/dev/null 2>&1; then
			taskkill //PID "${_winpid}" //T //F >/dev/null 2>&1 || true
		fi
	else
		# macOS/Linux: kill the game child first (pkill if present), then the shell.
		command -v pkill >/dev/null 2>&1 && pkill -P "${_pid}" 2>/dev/null || true
	fi
	kill "${_pid}" 2>/dev/null || true
}

# Best-effort locate of the engine's debug.log, mirroring the settings.yaml
# candidate search below. Echoes a path (may not exist) or nothing.
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

MAP_DIR="tools/autotest/scenarios/${TEST_NAME}"
if [ ! -d "${MAP_DIR}" ]; then
	echo "Error: test map not found at ${MAP_DIR}"
	exit 3
fi

# Detect screen size on macOS for window positioning. Falls back to 1920x1080.
SCREEN_W=1920
SCREEN_H=1080
if command -v osascript >/dev/null 2>&1; then
	BOUNDS=$(osascript -e 'tell application "Finder" to get bounds of window of desktop' 2>/dev/null || true)
	if [ -n "${BOUNDS}" ]; then
		DETECTED_W=$(echo "${BOUNDS}" | awk -F', *' '{print $3}')
		DETECTED_H=$(echo "${BOUNDS}" | awk -F', *' '{print $4}')
		[ -n "${DETECTED_W}" ] && SCREEN_W=${DETECTED_W}
		[ -n "${DETECTED_H}" ] && SCREEN_H=${DETECTED_H}
	fi
fi

# Build size + position based on POSITION choice.
WINDOW_ARGS=""
WINDOW_POS_ENV=""
case "${POSITION}" in
	centered)
		# Wide but with a visible margin all around — leaves the menu bar/dock free.
		W=$((SCREEN_W * 90 / 100))
		H=$((SCREEN_H * 85 / 100))
		X=$(((SCREEN_W - W) / 2))
		Y=$(((SCREEN_H - H) / 2))
		# Bias slightly downward so the macOS menu bar stays clear.
		[ ${Y} -lt 32 ] && Y=32
		WINDOW_ARGS="Graphics.WindowedSize=${W},${H}"
		WINDOW_POS_ENV="${X},${Y}"
		;;
	right)
		HALF_W=$((SCREEN_W / 2))
		USABLE_H=$((SCREEN_H - 40))
		WINDOW_ARGS="Graphics.WindowedSize=${HALF_W},${USABLE_H}"
		WINDOW_POS_ENV="${HALF_W},32"
		;;
	left)
		HALF_W=$((SCREEN_W / 2))
		USABLE_H=$((SCREEN_H - 40))
		WINDOW_ARGS="Graphics.WindowedSize=${HALF_W},${USABLE_H}"
		WINDOW_POS_ENV="0,32"
		;;
	full)
		# Don't set size or position; let the user's settings.yaml decide.
		;;
	*)
		echo "Unknown position: ${POSITION} (expected: centered, left, right, full)"
		exit 3 ;;
esac

# Pick a result path under the user's HOME so the engine can write to it
# regardless of where Platform.SupportDir lands.
RESULT_DIR="${HOME}/.ww3mod-tests"
mkdir -p "${RESULT_DIR}"
RESULT_FILE="${RESULT_DIR}/result.json"
rm -f "${RESULT_FILE}"

# Per-run screenshot output dir. Tests can capture via Test.Screenshot(label)
# in Lua; the PNGs land here with predictable filenames (NNN_<label>.png) and
# paths are echoed into the verdict JSON's screenshots[] array. Each run gets
# its own folder so successive runs of the same test don't clobber each other.
RUN_ID="$(date +%y%m%d_%H%M%S)_${TEST_NAME}"
SCREENSHOT_DIR="${RESULT_DIR}/screenshots/${RUN_ID}"
mkdir -p "${SCREENSHOT_DIR}"

# Cleanup: drop screenshot runs older than 7 days so /.ww3mod-tests/screenshots
# doesn't grow unboundedly. Best-effort — failures (e.g. permissions) ignored.
find "${RESULT_DIR}/screenshots" -mindepth 1 -maxdepth 1 -type d -mtime +7 \
	-exec rm -rf {} \; 2>/dev/null || true

# Optional one-line description shown in the TEST MODE panel.
# Read from <map-folder>/description.txt; first non-empty line wins.
TEST_DESCRIPTION=""
if [ -f "${MAP_DIR}/description.txt" ]; then
	TEST_DESCRIPTION=$(awk 'NF { print; exit }' "${MAP_DIR}/description.txt" | tr -d '\r')
fi

AUDIO_LABEL="muted"
[ "${AUDIO_MUTE}" = "0" ] && AUDIO_LABEL="audio"

SPEED_LABEL="1x"
[ -n "${SPEED_MULT}" ] && SPEED_LABEL="${SPEED_MULT}x"

echo "==> Test: ${TEST_NAME}"
echo "==> Mode: ${GRAPHICS_MODE} (${POSITION}, ${WINDOW_BEHAVIOR}, ${AUDIO_LABEL}, ${SPEED_LABEL})"
[ -n "${WINDOW_POS_ENV}" ] && echo "==> Position: ${WINDOW_POS_ENV} on ${SCREEN_W}x${SCREEN_H}"
[ -n "${TEST_DESCRIPTION}" ] && echo "==> Description: ${TEST_DESCRIPTION}"
echo "==> Result file: ${RESULT_FILE}"
echo "==> Screenshots: ${SCREENSHOT_DIR}"
echo

# OpenRA's SDL platform reads OPENRA_WINDOW_X/Y at window creation (engine
# patch). Falls back to SDL_WINDOWPOS_CENTERED_DISPLAY when unset.
if [ -n "${WINDOW_POS_ENV}" ]; then
	export OPENRA_WINDOW_X="${WINDOW_POS_ENV%,*}"
	export OPENRA_WINDOW_Y="${WINDOW_POS_ENV#*,}"
fi

# Engine reads OPENRA_WINDOW_MINIMIZED=1 and calls SDL_MinimizeWindow after
# window creation. Only set when the user explicitly opts back in via
# --minimized, since miniaturized SDL windows are awkward to restore on macOS
# (Cmd+Tab can't unminiaturize — only the small dock icon next to Trash does).
MINIMIZE_ARGS=""
if [ "${WINDOW_BEHAVIOR}" = "minimized" ] && [ "${POSITION}" != "full" ] && [ "${GRAPHICS_MODE}" = "Windowed" ]; then
	export OPENRA_WINDOW_MINIMIZED=1
	# A minimized window suspends rendering; a low framerate cap would then
	# throttle the *suspended* sim to a few ticks/s (the logic gate clears only
	# at the render cadence). Force the cap off so a minimized run free-runs.
	# See WORKSPACE/plans/260721_sim_throughput.md, Option C.
	MINIMIZE_ARGS="Graphics.CapFramerate=false"
fi

# Audio mute via the Sound.Mute toggle (not by zeroing volumes — that would
# risk polluting the saved volume levels if the engine auto-saves settings).
# Sound.Mute is the same flag the in-game mute hotkey toggles.
AUDIO_ARGS=""
if [ "${AUDIO_MUTE}" = "1" ]; then
	AUDIO_ARGS="Sound.Mute=true"
fi

# Speed multiplier (Test.SpeedMultiplier) — applied universally at world load by
# the TestModeSpeedMultiplier trait. Unset → 1× (byte-identical to old behavior).
SPEED_ARGS=""
if [ -n "${SPEED_MULT}" ]; then
	SPEED_ARGS="Test.SpeedMultiplier=${SPEED_MULT}"
fi

# macOS focus handling. Capture the currently-frontmost app so we can:
#   1. Bounce focus back to it after the game window appears (background mode).
#   2. Restore focus after the game exits (defends against the close-time
#      focus shuffle that picks a random next-frontmost app).
PREV_APP=""
RESTORE_PID=""
if [ "$(uname)" = "Darwin" ] && command -v osascript >/dev/null 2>&1; then
	PREV_APP=$(osascript -e 'tell application "System Events" to name of first application process whose frontmost is true' 2>/dev/null || true)
fi

# Background-mode watchdog: poll for ~5s; once frontmost flips away from
# PREV_APP (i.e. OpenRA grabbed focus), bounce back to PREV_APP.
if [ "${WINDOW_BEHAVIOR}" = "background" ] \
	&& [ "${GRAPHICS_MODE}" = "Windowed" ] \
	&& [ -n "${PREV_APP}" ]; then
	(
		i=0
		while [ ${i} -lt 20 ]; do
			CURRENT=$(osascript -e 'tell application "System Events" to name of first application process whose frontmost is true' 2>/dev/null || echo "")
			if [ -n "${CURRENT}" ] && [ "${CURRENT}" != "${PREV_APP}" ]; then
				# Give the game a brief moment to settle, then defocus it.
				sleep 0.4
				osascript -e "tell application \"${PREV_APP}\" to activate" 2>/dev/null || true
				exit 0
			fi
			sleep 0.25
			i=$((i + 1))
		done
	) &
	RESTORE_PID=$!
fi

# Back up settings.yaml around the launch. The engine sometimes auto-saves
# settings during normal flow (the launch-game.sh comment about Graphics.Mode
# pollution alludes to this), and a saved Sound.Mute=true would carry over to
# normal launches. Restoring the file post-run sidesteps the risk entirely.
SETTINGS_FILE=""
SETTINGS_BACKUP=""
case "$(uname -s)" in
	Darwin) SETTINGS_FILE="${HOME}/Library/Application Support/OpenRA/settings.yaml" ;;
	Linux)  SETTINGS_FILE="${HOME}/.config/openra/settings.yaml" ;;
	MINGW*|MSYS*|CYGWIN*|Windows_NT)
		# engine/Support override, then %APPDATA%\OpenRA (modern), then
		# Documents\OpenRA (legacy). First existing wins; empty → skip backup.
		for _cand in \
			"${REPO_ROOT}/engine/Support/settings.yaml" \
			"$(cygpath -u "${APPDATA:-}" 2>/dev/null)/OpenRA/settings.yaml" \
			"$(cygpath -u "${USERPROFILE:-}" 2>/dev/null)/Documents/OpenRA/settings.yaml"; do
			if [ -f "${_cand}" ]; then SETTINGS_FILE="${_cand}"; break; fi
		done
		;;
esac
if [ -n "${SETTINGS_FILE}" ] && [ -f "${SETTINGS_FILE}" ]; then
	SETTINGS_BACKUP="${RESULT_DIR}/settings.yaml.bak"
	cp "${SETTINGS_FILE}" "${SETTINGS_BACKUP}"
fi

# Game-side args need Windows-form paths under Git-Bash; identity elsewhere.
RESULT_FILE_GAME=$(to_game_path "${RESULT_FILE}")
SCREENSHOT_DIR_GAME=$(to_game_path "${SCREENSHOT_DIR}")

./launch-game.sh \
	"Launch.Map=${TEST_NAME}" \
	"Test.Mode=true" \
	"Test.Name=${TEST_NAME}" \
	"Test.Description=${TEST_DESCRIPTION}" \
	"Test.ResultPath=${RESULT_FILE_GAME}" \
	"Test.ScreenshotDir=${SCREENSHOT_DIR_GAME}" \
	"Graphics.Mode=${GRAPHICS_MODE}" \
	${WINDOW_ARGS} \
	${AUDIO_ARGS} \
	${SPEED_ARGS} \
	${MINIMIZE_ARGS} \
	&
LAUNCH_PID=$!

# ── Hard wall-clock watchdog ────────────────────────────────────────────────
# The engine writes result.json only when Test.Pass/Fail/Skip runs. If a map's
# rules fail to load (e.g. a duplicate MiniYaml key), the game logs "Failed to
# load rules" to debug.log, falls back to the main menu, and idles FOREVER — no
# verdict is written and the window sits on screen until a human kills it. Poll
# once a second for either a written verdict or the game exiting on its own; if
# neither happens within TIMEOUT_SECS, kill the game and synthesize a FAIL so
# the runner (and run-batch) always get a definite result.
TIMED_OUT=0
_elapsed=0
while :; do
	if ! kill -0 "${LAUNCH_PID}" 2>/dev/null; then
		break            # launcher/game exited on its own
	fi
	if [ -f "${RESULT_FILE}" ]; then
		break            # verdict written; let the game exit itself
	fi
	if [ "${_elapsed}" -ge "${TIMEOUT_SECS}" ]; then
		TIMED_OUT=1
		break
	fi
	sleep 1
	_elapsed=$((_elapsed + 1))
done

if [ "${TIMED_OUT}" = "1" ]; then
	echo
	echo "==> TIMEOUT: no verdict after ${TIMEOUT_SECS}s — killing the game."
	kill_game "${LAUNCH_PID}"

	# Surface the most likely cause: rules that failed to load.
	DEBUG_LOG=$(find_debug_log)
	if [ -n "${DEBUG_LOG}" ] && [ -f "${DEBUG_LOG}" ]; then
		MATCHES=$(tail -n 200 "${DEBUG_LOG}" 2>/dev/null | grep -i "Failed to load rules" || true)
		if [ -n "${MATCHES}" ]; then
			echo "==> debug.log reports a rules-load failure:"
			printf '%s\n' "${MATCHES}" | sed 's/^/    /'
		else
			echo "==> No 'Failed to load rules' in ${DEBUG_LOG} tail — hang is elsewhere."
		fi
	else
		echo "==> Could not locate debug.log to diagnose the hang."
	fi

	# Synthetic verdict in the engine's schema (name/status/notes/timestamp) so
	# the STATUS grep below and run-batch's exit-code read both see a FAIL.
	if [ ! -f "${RESULT_FILE}" ]; then
		NOW_ISO=$(date -u +%Y-%m-%dT%H:%M:%SZ)
		NOTES="timeout: no verdict after ${TIMEOUT_SECS}s - game hung or rules failed to load; check "'%APPDATA%\\OpenRA\\Logs\\debug.log'
		printf '{"name":"%s","status":"fail","notes":"%s","timestamp":"%s"}\n' \
			"${TEST_NAME}" "${NOTES}" "${NOW_ISO}" > "${RESULT_FILE}"
	fi
fi

# Reap the launcher (returns immediately if already gone or just killed).
wait "${LAUNCH_PID}" 2>/dev/null || true

if [ -n "${SETTINGS_BACKUP}" ] && [ -f "${SETTINGS_BACKUP}" ]; then
	mv "${SETTINGS_BACKUP}" "${SETTINGS_FILE}"
fi

# Reap the watchdog if it's still alive (game exited before window appeared).
if [ -n "${RESTORE_PID}" ]; then
	kill "${RESTORE_PID}" 2>/dev/null || true
	wait "${RESTORE_PID}" 2>/dev/null || true
fi

# Restore focus after the game exits — this is the fix for the close-time
# focus theft. macOS otherwise picks an arbitrary next-frontmost app.
if [ -n "${PREV_APP}" ]; then
	osascript -e "tell application \"${PREV_APP}\" to activate" 2>/dev/null || true
fi

echo

if [ ! -f "${RESULT_FILE}" ]; then
	echo "==> No result file written. Test was likely closed without verdict."
	exit 3
fi

echo "==> Result:"
cat "${RESULT_FILE}"
echo

# PITFALL: Game.TakeScreenshot is async (ThreadPool via Renderer.SaveScreenshot).
# When the verdict is written from Test.Pass/Fail, the PNG files referenced in
# the JSON may still be flushing. A brief settle wait keeps the post-run
# listing accurate. 250ms is empirically enough for one or two captures.
sleep 0.25

# Surface any captured screenshots. The verdict JSON paths are authoritative,
# but a directory listing is the simplest "what's there" view for the runner.
if [ -d "${SCREENSHOT_DIR}" ]; then
	SHOT_COUNT=$(find "${SCREENSHOT_DIR}" -maxdepth 1 -name "*.png" -type f 2>/dev/null | wc -l | tr -d ' ')
	if [ "${SHOT_COUNT}" -gt 0 ]; then
		echo "==> Screenshots (${SHOT_COUNT}):"
		find "${SCREENSHOT_DIR}" -maxdepth 1 -name "*.png" -type f 2>/dev/null \
			| sort | sed 's|^|    |'
		echo
	else
		# Empty per-run dir is just clutter; drop it so the screenshots/ folder
		# only carries dirs that actually contain captures.
		rmdir "${SCREENSHOT_DIR}" 2>/dev/null || true
	fi
fi

STATUS=$(grep -o '"status":"[^"]*"' "${RESULT_FILE}" | head -1 | sed 's/"status":"\(.*\)"/\1/')

case "${STATUS}" in
	pass) exit 0 ;;
	fail) exit 1 ;;
	skip) exit 2 ;;
	*)    exit 3 ;;
esac

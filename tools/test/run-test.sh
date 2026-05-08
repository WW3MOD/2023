#!/bin/sh
# WW3MOD developer test harness — single-test runner
#
# Usage:  ./tools/test/run-test.sh [flags] <test-folder-name>
#
# Flags (must come before the test name):
#   --position=<auto|right|left|full>  Where to put the windowed game.
#                                 First-run default: auto (osascript-based
#                                 detection of the AXMain Ghostty/Terminal
#                                 window — works for single-window setups,
#                                 unreliable when several windows exist).
#                                 Run --position=<left|right> once from a
#                                 terminal to save it per-TTY; subsequent
#                                 runs from that same terminal reuse the
#                                 saved choice without needing the flag.
#                                 Saved at ~/.ww3mod-tests/position-prefs/.
#                                 full skips sizing/positioning entirely.
#   --fullscreen                  Same as --position=full + Mode=PseudoFullscreen
#   --windowed                    Force windowed (default; redundant unless
#                                 overriding a user-config that forces fullscreen)
#   --help                        Show this message
#
# Defaults: windowed, right half, edge-pan disabled (engine-side, gated on
# Test.Mode + Mode=Windowed).
#
# Example: ./tools/test/run-test.sh test-artillery-turret
#          ./tools/test/run-test.sh --position=left test-artillery-turret
#          ./tools/test/run-test.sh --fullscreen test-artillery-turret
#
# Exit code: 0=pass, 1=fail, 2=skip, 3=error.

set -e

GRAPHICS_MODE="Windowed"
POSITION=""        # explicit --position value (empty = no override)
EXPLICIT_POS=0

while [ $# -gt 0 ]; do
	case "$1" in
		--fullscreen)
			GRAPHICS_MODE="PseudoFullscreen"
			POSITION="full"
			EXPLICIT_POS=1
			shift ;;
		--windowed)
			GRAPHICS_MODE="Windowed"
			shift ;;
		--position=*)
			POSITION="${1#*=}"
			EXPLICIT_POS=1
			shift ;;
		--help|-h)
			sed -n '2,22p' "$0" | sed 's/^# \?//'
			exit 0 ;;
		--*)
			echo "Unknown flag: $1"
			exit 3 ;;
		*)
			break ;;
	esac
done

# Per-terminal saved preference: keyed by TTY device. First explicit
# --position from a given terminal is saved; subsequent runs from the
# same terminal reuse it without needing the flag.
TTY_KEY=$(tty 2>/dev/null | tr / _ | tr -d ' ' || echo unknown)
PREF_DIR="${HOME}/.ww3mod-tests/position-prefs"
PREF_FILE="${PREF_DIR}/${TTY_KEY}"

if [ "${EXPLICIT_POS}" = "1" ] && [ -n "${POSITION}" ]; then
	mkdir -p "${PREF_DIR}"
	echo "${POSITION}" > "${PREF_FILE}"
	echo "==> saved '${POSITION}' as preference for this terminal (${TTY_KEY})"
elif [ -z "${POSITION}" ] && [ -f "${PREF_FILE}" ]; then
	POSITION=$(cat "${PREF_FILE}")
	echo "==> using saved position '${POSITION}' for this terminal"
elif [ -z "${POSITION}" ]; then
	POSITION="auto"
fi

TEST_NAME="$1"
if [ -z "${TEST_NAME}" ]; then
	echo "Usage: $0 [--position=right|left|full] [--fullscreen|--windowed] <test-folder-name>"
	echo "  e.g.  $0 test-artillery-turret"
	exit 3
fi

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

MAP_DIR="mods/ww3mod/maps/${TEST_NAME}"
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

# Auto-detect: place game on the half opposite the calling terminal's
# frontmost window. Requires accessibility permission for the calling
# terminal in System Settings → Privacy & Security → Accessibility.
# Falls back to right if detection fails.
if [ "${POSITION}" = "auto" ]; then
	DETECTED_CENTER=$(osascript -e '
		tell application "System Events"
			try
				set frontProc to first application process whose frontmost is true
				set winPos to position of window 1 of frontProc
				set winSize to size of window 1 of frontProc
				return ((item 1 of winPos) + (item 1 of winSize) / 2) as integer
			on error
				return -1
			end try
		end tell' 2>/dev/null || echo -1)
	if [ "${DETECTED_CENTER}" = "-1" ] || [ -z "${DETECTED_CENTER}" ]; then
		echo "==> auto-detect failed (accessibility permission needed?) — defaulting to right"
		echo "==> grant: System Settings → Privacy & Security → Accessibility → ${TERM_PROGRAM:-your terminal}"
		POSITION="right"
	elif [ "${DETECTED_CENTER}" -lt "$((SCREEN_W / 2))" ]; then
		POSITION="right"
		echo "==> auto: terminal on left (center=${DETECTED_CENTER}), placing game on right"
	else
		POSITION="left"
		echo "==> auto: terminal on right (center=${DETECTED_CENTER}), placing game on left"
	fi
fi

# Build size + position based on POSITION choice.
WINDOW_ARGS=""
WINDOW_POS_ENV=""
case "${POSITION}" in
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
		echo "Unknown --position value: ${POSITION} (expected: right, left, full)"
		exit 3 ;;
esac

# Pick a result path under the user's HOME so the engine can write to it
# regardless of where Platform.SupportDir lands.
RESULT_DIR="${HOME}/.ww3mod-tests"
mkdir -p "${RESULT_DIR}"
RESULT_FILE="${RESULT_DIR}/result.json"
rm -f "${RESULT_FILE}"

echo "==> Test: ${TEST_NAME}"
echo "==> Mode: ${GRAPHICS_MODE} (${POSITION})"
[ -n "${WINDOW_POS_ENV}" ] && echo "==> Position: ${WINDOW_POS_ENV} on ${SCREEN_W}x${SCREEN_H}"
echo "==> Result file: ${RESULT_FILE}"
echo

# OpenRA's SDL platform reads OPENRA_WINDOW_X/Y at window creation (engine
# patch). Falls back to SDL_WINDOWPOS_CENTERED_DISPLAY when unset.
if [ -n "${WINDOW_POS_ENV}" ]; then
	export OPENRA_WINDOW_X="${WINDOW_POS_ENV%,*}"
	export OPENRA_WINDOW_Y="${WINDOW_POS_ENV#*,}"
fi

./launch-game.sh \
	"Launch.Map=${TEST_NAME}" \
	"Test.Mode=true" \
	"Test.Name=${TEST_NAME}" \
	"Test.ResultPath=${RESULT_FILE}" \
	"Graphics.Mode=${GRAPHICS_MODE}" \
	${WINDOW_ARGS} \
	|| true

echo

if [ ! -f "${RESULT_FILE}" ]; then
	echo "==> No result file written. Test was likely closed without verdict."
	exit 3
fi

echo "==> Result:"
cat "${RESULT_FILE}"
echo

STATUS=$(grep -o '"status":"[^"]*"' "${RESULT_FILE}" | head -1 | sed 's/"status":"\(.*\)"/\1/')

case "${STATUS}" in
	pass) exit 0 ;;
	fail) exit 1 ;;
	skip) exit 2 ;;
	*)    exit 3 ;;
esac

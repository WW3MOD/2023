#!/bin/sh
# WW3MOD developer test harness — single-test runner
#
# Usage:  ./tools/test/run-test.sh [position] [flags] <test-folder-name>
#
# Position shorthand (positional, before the test name; case-insensitive):
#   L | -L | --left      Left half of the screen
#   R | -R | --right     Right half of the screen
#   F | -F | --full      PseudoFullscreen
#                        (no shorthand → centered, ~90% × ~85%, default)
#
# Flags:
#   --position=<centered|left|right|full>  Long form of the above.
#   --fullscreen           Same as F + Mode=PseudoFullscreen.
#   --windowed             Force windowed (default; only useful when overriding
#                          a user settings.yaml that forces fullscreen).
#   --minimized            Open the window minimized (default for AUTOTEST).
#   --no-minimize          Open the window normally (default for DEMO).
#   --help                 Show this message.
#
# Defaults: windowed, centered (large but not full), minimized, edge-pan
# disabled (engine-side, gated on Test.Mode + Mode=Windowed).
#
# Examples:
#   ./tools/test/run-test.sh test-paladin-fires           # centered, minimized
#   ./tools/test/run-test.sh L test-paladin-fires         # left half
#   ./tools/test/run-test.sh R --no-minimize test-foo     # right, visible
#   ./tools/test/run-test.sh F test-foo                   # fullscreen
#
# Exit code: 0=pass, 1=fail, 2=skip, 3=error.

set -e

GRAPHICS_MODE="Windowed"
POSITION="centered"
MINIMIZE=1

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
		--minimized)            MINIMIZE=1; shift ;;
		--no-minimize)          MINIMIZE=0; shift ;;
		--help|-h)
			sed -n '2,30p' "$0" | sed 's/^# \?//'
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
	echo "Usage: $0 [L|R|F] [--no-minimize] <test-folder-name>"
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

# Optional one-line description shown in the TEST MODE panel.
# Read from <map-folder>/description.txt; first non-empty line wins.
TEST_DESCRIPTION=""
if [ -f "${MAP_DIR}/description.txt" ]; then
	TEST_DESCRIPTION=$(awk 'NF { print; exit }' "${MAP_DIR}/description.txt" | tr -d '\r')
fi

MIN_LABEL="visible"
[ "${MINIMIZE}" = "1" ] && MIN_LABEL="minimized"

echo "==> Test: ${TEST_NAME}"
echo "==> Mode: ${GRAPHICS_MODE} (${POSITION}, ${MIN_LABEL})"
[ -n "${WINDOW_POS_ENV}" ] && echo "==> Position: ${WINDOW_POS_ENV} on ${SCREEN_W}x${SCREEN_H}"
[ -n "${TEST_DESCRIPTION}" ] && echo "==> Description: ${TEST_DESCRIPTION}"
echo "==> Result file: ${RESULT_FILE}"
echo

# OpenRA's SDL platform reads OPENRA_WINDOW_X/Y at window creation (engine
# patch). Falls back to SDL_WINDOWPOS_CENTERED_DISPLAY when unset.
if [ -n "${WINDOW_POS_ENV}" ]; then
	export OPENRA_WINDOW_X="${WINDOW_POS_ENV%,*}"
	export OPENRA_WINDOW_Y="${WINDOW_POS_ENV#*,}"
fi

# Engine reads OPENRA_WINDOW_MINIMIZED=1 and calls SDL_MinimizeWindow after
# window creation (windowed mode only).
if [ "${MINIMIZE}" = "1" ] && [ "${POSITION}" != "full" ] && [ "${GRAPHICS_MODE}" = "Windowed" ]; then
	export OPENRA_WINDOW_MINIMIZED=1
fi

./launch-game.sh \
	"Launch.Map=${TEST_NAME}" \
	"Test.Mode=true" \
	"Test.Name=${TEST_NAME}" \
	"Test.Description=${TEST_DESCRIPTION}" \
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

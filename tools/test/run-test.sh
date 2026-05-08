#!/bin/sh
# WW3MOD developer test harness — single-test runner
#
# Usage:  ./tools/test/run-test.sh [flags] <test-folder-name>
#
# Flags (must come before the test name):
#   --fullscreen   Launch fullscreen (default: windowed for dev visibility)
#   --windowed     Launch windowed (the default; redundant unless overriding a
#                  user-config that forces fullscreen)
#   --help         Show this message
#
# Example: ./tools/test/run-test.sh test-artillery-turret
#          ./tools/test/run-test.sh --fullscreen test-artillery-turret
#
# Launches the game with Test.Mode=true + Launch.Map=<folder>, waits for the
# player to press F1/F2/F3/F4, reads the result JSON, and prints a summary.
# Exit code: 0=pass, 1=fail, 2=skip, 3=error.

set -e

GRAPHICS_MODE="Windowed"

while [ $# -gt 0 ]; do
	case "$1" in
		--fullscreen)
			GRAPHICS_MODE="PseudoFullscreen"
			shift ;;
		--windowed)
			GRAPHICS_MODE="Windowed"
			shift ;;
		--help|-h)
			sed -n '2,17p' "$0" | sed 's/^# \?//'
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
	echo "Usage: $0 [--fullscreen|--windowed] <test-folder-name>"
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

# Pick a result path under the user's HOME so the engine can write to it
# regardless of where Platform.SupportDir lands.
RESULT_DIR="${HOME}/.ww3mod-tests"
mkdir -p "${RESULT_DIR}"
RESULT_FILE="${RESULT_DIR}/result.json"
rm -f "${RESULT_FILE}"

echo "==> Test: ${TEST_NAME}"
echo "==> Mode: ${GRAPHICS_MODE}"
echo "==> Result file: ${RESULT_FILE}"
echo "==> Launching game (close it manually if it doesn't auto-exit on PASS/FAIL/SKIP)"
echo

# Reuse the existing launcher, append our test args.
./launch-game.sh \
	"Launch.Map=${TEST_NAME}" \
	"Test.Mode=true" \
	"Test.Name=${TEST_NAME}" \
	"Test.ResultPath=${RESULT_FILE}" \
	"Graphics.Mode=${GRAPHICS_MODE}" \
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

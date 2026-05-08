#!/bin/sh
# WW3MOD developer test harness — single-test runner
#
# Usage:  ./tools/test/run-test.sh <test-folder-name>
# Example: ./tools/test/run-test.sh test-artillery-turret
#
# Launches the game with Test.Mode=true + Launch.Map=<folder>, waits for the
# player to press F1/F2/F3 (or click PASS/FAIL/SKIP), reads the result JSON,
# and prints a summary. Exit code: 0=pass, 1=fail, 2=skip, 3=error.

set -e

TEST_NAME="$1"
if [ -z "${TEST_NAME}" ]; then
	echo "Usage: $0 <test-folder-name>"
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
echo "==> Result file: ${RESULT_FILE}"
echo "==> Launching game (close it manually if it doesn't auto-exit on PASS/FAIL/SKIP)"
echo

# Reuse the existing launcher, append our test args.
./launch-game.sh \
	"Launch.Map=${TEST_NAME}" \
	"Test.Mode=true" \
	"Test.Name=${TEST_NAME}" \
	"Test.ResultPath=${RESULT_FILE}" \
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

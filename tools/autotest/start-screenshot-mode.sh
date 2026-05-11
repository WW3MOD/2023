#!/bin/sh
# WW3MOD — launch the game in external screenshot mode.
#
# Usage:  ./tools/autotest/start-screenshot-mode.sh
#
# Opens the game at the main menu (no Launch.Map) with Test.Mode=true and a
# command-file watcher running. From any state — main menu, server lobby,
# in-match — you can take a screenshot via:
#
#   ./tools/autotest/screenshot.sh <label>            # fire-and-forget
#   ./tools/autotest/screenshot.sh <label> --wait     # blocks, prints path
#
# Screenshots land in ${HOME}/.ww3mod-tests/screenshots/manual_<run-id>/ with
# a manifest.json listing every capture (label, path, note, timestamp).
#
# The game window comes up visible and foreground — unlike AUTOTEST runs, which
# default to background — because manual UI navigation needs focus.

set -e

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

# Per-run dir. RUN_ID matches run-test.sh's format for consistency.
RUN_ID="manual_$(date +%y%m%d_%H%M%S)"
RESULT_DIR="${HOME}/.ww3mod-tests"
SCREENSHOT_DIR="${RESULT_DIR}/screenshots/${RUN_ID}"
CMD_FILE="${SCREENSHOT_DIR}/cmd.txt"

mkdir -p "${SCREENSHOT_DIR}"
rm -f "${CMD_FILE}"

echo "==> Screenshot mode"
echo "==> Screenshots: ${SCREENSHOT_DIR}"
echo "==> Command file: ${CMD_FILE}"
echo "==> Manifest: ${SCREENSHOT_DIR}/manifest.json"
echo
echo "From another terminal:"
echo "    ./tools/autotest/screenshot.sh <label>          # fire-and-forget"
echo "    ./tools/autotest/screenshot.sh <label> --wait   # waits, prints PNG path"
echo
echo "Press Ctrl+C in the game window (or just close it) to end the session."
echo

# Test.Name=manual marks the run so the verdict (if any) is distinguishable.
# No Test.ResultPath / Launch.Map — the game opens at the main menu and waits
# for the user (or screenshot.sh) to drive it.
./launch-game.sh \
	"Test.Mode=true" \
	"Test.Name=manual" \
	"Test.ScreenshotDir=${SCREENSHOT_DIR}" \
	"Test.ScreenshotCmdFile=${CMD_FILE}" \
	|| true

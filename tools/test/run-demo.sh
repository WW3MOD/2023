#!/bin/sh
# WW3MOD demo harness — load a staged scenario for human viewing.
#
# Usage:  ./tools/test/run-demo.sh [flags] <demo-folder-name>
#         e.g.  ./tools/test/run-demo.sh demo-changed-vehicles
#
# Same flags as run-test.sh (--position, --fullscreen, --windowed, --help).
# Demos live in mods/ww3mod/maps/demo-*/ and do NOT write a result file —
# the user closes the window when done; this script returns 0 either way.
#
# If you find yourself wanting an exit code from a demo, you want a test
# (AUTOTEST / run-test.sh), not a demo.

set -e

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "${REPO_ROOT}"

# Last positional arg is the demo name; everything before is flags.
NAME=""
for a in "$@"; do NAME="$a"; done

case "${NAME}" in
	"")
		echo "Usage: $0 [flags] <demo-folder-name>"
		echo "       $0 demo-changed-vehicles"
		exit 3 ;;
	--help|-h)
		sed -n '2,12p' "$0" | sed 's/^# \?//'
		exit 0 ;;
	demo-*) ;;
	*)
		echo "Demo folders must be named demo-* (got: ${NAME})."
		echo "If this is a test scenario, use ./tools/test/run-test.sh instead."
		exit 3 ;;
esac

if [ ! -d "mods/ww3mod/maps/${NAME}" ]; then
	echo "Error: demo folder not found at mods/ww3mod/maps/${NAME}"
	exit 3
fi

# Delegate to run-test.sh — same launch plumbing, same window-positioning logic.
# Demos won't write result.json, so run-test.sh exits 3 ("no result"); we treat
# that as success here because verdict-less is the demo's whole point.
./tools/test/run-test.sh "$@"
rc=$?

if [ ${rc} -eq 3 ]; then
	exit 0
fi
exit ${rc}

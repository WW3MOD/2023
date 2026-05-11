#!/bin/sh
# WW3MOD developer test harness — discovery
#
# Lists all `test-*` folders under tools/autotest/scenarios/ with the first
# non-empty line of their description.txt (if present). Read-only; no game launch.

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
MAPS_DIR="${REPO_ROOT}/tools/autotest/scenarios"

if ! ls -d "${MAPS_DIR}"/test-*/ >/dev/null 2>&1; then
	echo "No test-* folders found under ${MAPS_DIR}"
	exit 1
fi

printf '  %-32s %s\n' "TEST" "DESCRIPTION"
printf '  %-32s %s\n' "────────────────────────────────" "────────────────────────────────────────"

for d in "${MAPS_DIR}"/test-*/; do
	name=$(basename "$d")
	desc=""
	if [ -f "${d}/description.txt" ]; then
		desc=$(awk 'NF { print; exit }' "${d}/description.txt" | tr -d '\r')
	fi
	printf '  %-32s %s\n' "${name}" "${desc}"
done

echo
echo "Run one:    ./tools/autotest/run-test.sh <name>"
echo "Run batch:  ./tools/autotest/run-batch.sh <name1> <name2> ..."
echo "Run all:    ./tools/autotest/run-batch.sh --all"

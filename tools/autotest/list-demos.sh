#!/bin/sh
# WW3MOD demo harness — discovery
#
# Lists all `demo-*` folders under tools/autotest/scenarios/ with the first
# non-empty line of their description.txt (if present). Read-only; no game launch.

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
MAPS_DIR="${REPO_ROOT}/tools/autotest/scenarios"

if ! ls -d "${MAPS_DIR}"/demo-*/ >/dev/null 2>&1; then
	echo "No demo-* folders found under ${MAPS_DIR}"
	exit 1
fi

printf '  %-32s %s\n' "DEMO" "DESCRIPTION"
printf '  %-32s %s\n' "────────────────────────────────" "────────────────────────────────────────"

for d in "${MAPS_DIR}"/demo-*/; do
	name=$(basename "$d")
	desc=""
	if [ -f "${d}/description.txt" ]; then
		desc=$(awk 'NF { print; exit }' "${d}/description.txt" | tr -d '\r')
	fi
	printf '  %-32s %s\n' "${name}" "${desc}"
done

echo
echo "Run one:    ./tools/autotest/run-demo.sh <name>"

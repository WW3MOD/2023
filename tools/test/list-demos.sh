#!/bin/sh
# WW3MOD demo harness — discovery
#
# Lists all `demo-*` folders under mods/ww3mod/maps/ with the first non-empty
# line of their description.txt (if present). Read-only; no game launch.

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
MAPS_DIR="${REPO_ROOT}/mods/ww3mod/maps"

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
echo "Run one:    ./tools/test/run-demo.sh <name>"

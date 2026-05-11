#!/bin/sh
# Regenerate tools/combat-sim/data/stats.json from the live mod YAML.
#
# Calls OpenRA.Utility's --dump-balance-json command, which iterates the
# fully-resolved Ruleset (post-inheritance) and emits every combat-relevant
# actor + every weapon as JSON. The combat-sim loads this JSON at startup
# instead of carrying its own hardcoded copies, so stat drift is impossible.
#
# Re-run after any change to mods/ww3mod/rules/*.yaml. A pre-commit hook
# can wire this in (see tools/git-hooks/pre-commit).

set -e

REPO_ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
ENGINE_DIR="${REPO_ROOT}/engine"
MOD_SEARCH_PATHS="${REPO_ROOT}/mods,${ENGINE_DIR}/mods"
OUT_FILE="${REPO_ROOT}/tools/combat-sim/data/stats.json"

mkdir -p "$(dirname "${OUT_FILE}")"

if [ ! -f "${ENGINE_DIR}/bin/OpenRA.Utility.dll" ]; then
	echo "OpenRA.Utility.dll not found — run 'make' first." >&2
	exit 1
fi

cd "${ENGINE_DIR}"

# Strip the "Loading mod: ww3mod" line that the engine writes to stdout
# before the JSON. Find the first '{' line and emit from there.
ENGINE_DIR="${ENGINE_DIR}" \
MOD_SEARCH_PATHS="${MOD_SEARCH_PATHS}" \
	dotnet bin/OpenRA.Utility.dll ww3mod --dump-balance-json 2>/dev/null \
	| awk '/^\{/{seen=1} seen' \
	> "${OUT_FILE}.tmp"

# Sanity check: must be valid JSON with actors+weapons keys.
if ! python3 -c "
import json, sys
d = json.load(open('${OUT_FILE}.tmp'))
assert 'actors' in d and 'weapons' in d, 'missing top-level keys'
assert len(d['actors']) > 50, f\"too few actors: {len(d['actors'])}\"
assert len(d['weapons']) > 20, f\"too few weapons: {len(d['weapons'])}\"
" 2>&1; then
	echo "Dump produced invalid JSON or suspiciously small content — aborting." >&2
	rm -f "${OUT_FILE}.tmp"
	exit 1
fi

mv "${OUT_FILE}.tmp" "${OUT_FILE}"
SIZE=$(wc -c < "${OUT_FILE}" | tr -d ' ')
ACTORS=$(python3 -c "import json; print(len(json.load(open('${OUT_FILE}'))['actors']))")
WEAPONS=$(python3 -c "import json; print(len(json.load(open('${OUT_FILE}'))['weapons']))")

echo "Wrote ${OUT_FILE}"
echo "  ${ACTORS} actors, ${WEAPONS} weapons, ${SIZE} bytes"
